using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zenith.App.Localization;

namespace Zenith.App.Services;

/// <summary>Estado de un diálogo modal en curso.</summary>
public sealed partial class DialogViewModel : ObservableObject
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DialogViewModel(DialogRequest request) => Request = request;

    public DialogRequest Request { get; }

    public Task<bool> Result => _completion.Task;

    public string ConfirmText => Request.ConfirmText ?? Loc.Instance["CommonContinue"];

    public string CancelText => Request.CancelText ?? Loc.Instance["CommonCancel"];

    public bool HasDetails => Request.Details is { Count: > 0 };

    public bool HasWarning => !string.IsNullOrWhiteSpace(Request.WarningText);

    public bool HasSummary => !string.IsNullOrWhiteSpace(Request.Summary);

    [RelayCommand]
    private void Confirm() => _completion.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => _completion.TrySetResult(false);
}

public sealed partial class ToastViewModel(string message, ToastKind kind) : ObservableObject
{
    public string Message { get; } = message;

    public ToastKind Kind { get; } = kind;

    public string Glyph => Kind switch
    {
        ToastKind.Success => "\uE930",
        ToastKind.Warning => "\uE7BA",
        ToastKind.Error => "\uEA39",
        _ => "\uE946"
    };
}

/// <summary>
/// Implementación de los diálogos como capa dentro de la propia ventana, no como
/// <c>MessageBox</c> del sistema: mantiene el lenguaje visual y permite animar
/// la entrada y la salida.
/// </summary>
public sealed partial class DialogService : ObservableObject, IDialogService
{
    private readonly ILogger<DialogService> _logger;
    private readonly DispatcherTimer _toastTimer;

    [ObservableProperty]
    private DialogViewModel? _activeDialog;

    [ObservableProperty]
    private ToastViewModel? _activeToast;

    public DialogService(ILogger<DialogService> logger)
    {
        _logger = logger;
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ActiveToast = null;
        };
    }

    public async Task<bool> ConfirmAsync(DialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new DialogViewModel(request);
        ActiveDialog = dialog;

        try
        {
            return await dialog.Result.ConfigureAwait(true);
        }
        finally
        {
            ActiveDialog = null;
        }
    }

    public string? PickFolder(string title, string? initialDirectory = null)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = title,
                Multiselect = false
            };

            if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
            {
                dialog.InitialDirectory = initialDirectory;
            }

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se ha podido abrir el selector de carpetas");
            Notify(Loc.Instance["AppFolderPickerFailed"], ToastKind.Error);
            return null;
        }
    }

    public void Notify(string message, ToastKind kind = ToastKind.Info)
    {
        ActiveToast = new ToastViewModel(message, kind);
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    [RelayCommand]
    private void DismissToast()
    {
        _toastTimer.Stop();
        ActiveToast = null;
    }
}
