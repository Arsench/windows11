using Zenith.Core.Safety;

namespace Zenith.App.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error
}

public sealed record DialogRequest(
    string Title,
    string Message,
    string ConfirmText = "Continuar",
    string CancelText = "Cancelar",
    bool IsDestructive = false,
    string? WarningText = null,
    IReadOnlyList<string>? Details = null,
    string? Summary = null);

/// <summary>
/// Diálogos y avisos. Se declara como interfaz para que los ViewModels sean
/// testeables y para que no dependan de <c>MessageBox</c>.
/// </summary>
public interface IDialogService
{
    Task<bool> ConfirmAsync(DialogRequest request);

    /// <summary>Devuelve null si el usuario cancela.</summary>
    string? PickFolder(string title, string? initialDirectory = null);

    void Notify(string message, ToastKind kind = ToastKind.Info);
}

/// <summary>Páginas que necesitan saber cuándo están visibles para no trabajar de más.</summary>
public interface INavigationAware
{
    void OnNavigatedTo();

    void OnNavigatedFrom();
}

public static class SafetyPresentation
{
    public static string GlyphFor(SafetyLevel level) => level switch
    {
        SafetyLevel.Blocked => "\uE783",  // Error
        SafetyLevel.Warning => "\uE7BA",  // Warning
        _ => "\uE73E"                     // Accept
    };
}
