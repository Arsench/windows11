using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zenith.App.Services;
using Zenith.Core.Abstractions;
using Zenith.Core.Duplicates;
using Zenith.Core.Primitives;
using Zenith.Core.Safety;

namespace Zenith.App.ViewModels;

public sealed partial class DuplicateFileViewModel : ObservableObject
{
    private readonly Action _onSelectionChanged;

    public DuplicateFileViewModel(DuplicateFile file, SafetyVerdict verdict, Action onSelectionChanged)
    {
        File = file;
        Verdict = verdict;
        _onSelectionChanged = onSelectionChanged;
    }

    [ObservableProperty] private bool _isSelected;

    public DuplicateFile File { get; }

    public SafetyVerdict Verdict { get; }

    public string Path => File.Path;

    public string FileName => File.FileName;

    public string DirectoryName => File.DirectoryName;

    public string ModifiedText => File.LastWriteUtc.ToLocalTime().ToString("g");

    public bool IsBlocked => Verdict.Level == SafetyLevel.Blocked;

    public bool HasWarning => Verdict.Level == SafetyLevel.Warning;

    public string? WarningText => Verdict.Level == SafetyLevel.Allowed ? null : Verdict.Reason;

    partial void OnIsSelectedChanged(bool value)
    {
        // Una ruta protegida no se puede marcar, ni siquiera a mano.
        if (value && IsBlocked)
        {
            IsSelected = false;
            return;
        }

        _onSelectionChanged();
    }
}

public sealed partial class DuplicateGroupViewModel : ObservableObject
{
    public DuplicateGroupViewModel(DuplicateGroup group, IReadOnlyList<DuplicateFileViewModel> files)
    {
        Group = group;
        Files = new ObservableCollection<DuplicateFileViewModel>(files);
    }

    [ObservableProperty] private bool _isExpanded = true;

    public DuplicateGroup Group { get; }

    public ObservableCollection<DuplicateFileViewModel> Files { get; }

    public string Title => $"GRUPO {Group.Index:00}";

    public string FileName => Group.Files[0].FileName;

    public string SizeText => ByteSize.Format(Group.FileSizeBytes);

    public string CopiesText => MetricFormatter.Count(Group.Files.Count, "copia", "copias");

    public string ReclaimableText => $"{ByteSize.Format(Group.ReclaimableBytes)} recuperables";
}

/// <summary>
/// Buscador de duplicados. La regla que gobierna toda la pantalla: nada se borra
/// sin que el usuario haya visto exactamente qué se va a borrar.
/// </summary>
public sealed partial class DuplicatesViewModel : ObservableObject, INavigationAware
{
    private readonly DuplicateScanner _scanner;
    private readonly DuplicateActionPlanner _planner;
    private readonly PathSafetyGuard _safety;
    private readonly IFileSystemOperations _files;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;
    private readonly ISettingsStore _settings;
    private readonly ILogger<DuplicatesViewModel> _logger;

    private CancellationTokenSource? _scanCancellation;
    private DuplicateScanResult _result = DuplicateScanResult.Empty;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _phaseText = string.Empty;
    [ObservableProperty] private string _progressDetailText = string.Empty;
    [ObservableProperty] private bool _hasScanned;

    [ObservableProperty] private string _summaryHeadline = string.Empty;
    [ObservableProperty] private string _summaryDetail = string.Empty;
    [ObservableProperty] private string _selectionText = "No has marcado ningún archivo";
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private int _errorCount;

    [ObservableProperty] private long _minimumSizeKilobytes = 1;
    [ObservableProperty] private bool _verifyByteByByte = true;

    public DuplicatesViewModel(
        DuplicateScanner scanner,
        DuplicateActionPlanner planner,
        PathSafetyGuard safety,
        IFileSystemOperations files,
        IShellService shell,
        IDialogService dialogs,
        ISettingsStore settings,
        ILogger<DuplicatesViewModel> logger)
    {
        _scanner = scanner;
        _planner = planner;
        _safety = safety;
        _files = files;
        _shell = shell;
        _dialogs = dialogs;
        _settings = settings;
        _logger = logger;
    }

    public ObservableCollection<string> Folders { get; } = [];

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    public bool CanScan => Folders.Count > 0 && !IsScanning && !IsBusy;

    public void OnNavigatedTo()
    {
        var settings = _settings.Current;
        MinimumSizeKilobytes = Math.Max(1, settings.DuplicateMinFileSizeBytes / 1024);
        VerifyByteByByte = settings.VerifyDuplicatesByteByByte;
        _safety.SetUserExclusions(settings.ExcludedPaths);
    }

    public void OnNavigatedFrom()
    {
        if (IsScanning) CancelScan();
    }

    // ---------------------------------------------------------------- carpetas

    [RelayCommand]
    private void AddFolder()
    {
        var folder = _dialogs.PickFolder("Añade una carpeta a la búsqueda");
        if (folder is null) return;

        if (Folders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.Notify("Esa carpeta ya está en la lista.", ToastKind.Info);
            return;
        }

        Folders.Add(folder);
        ScanCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanScan));
    }

    [RelayCommand]
    private void RemoveFolder(string? folder)
    {
        if (folder is null) return;
        Folders.Remove(folder);
        ScanCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanScan));
    }

    // ---------------------------------------------------------------- análisis

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();

        IsScanning = true;
        HasScanned = false;
        ProgressPercent = 0;
        PhaseText = "Preparando";
        ProgressDetailText = string.Empty;
        Groups.Clear();
        UpdateSelectionSummary();
        ScanCommand.NotifyCanExecuteChanged();

        var options = new DuplicateScanOptions
        {
            Roots = [.. Folders],
            MinFileSizeBytes = Math.Max(0, MinimumSizeKilobytes) * 1024,
            VerifyByteByByte = VerifyByteByByte
        };

        var progress = new Progress<DuplicateProgress>(p =>
        {
            ProgressPercent = p.OverallPercent;
            PhaseText = p.PhaseDisplayName;
            ProgressDetailText = p.Phase == DuplicatePhase.Enumerating
                ? $"{p.FilesDiscovered:N0} archivos encontrados"
                : p.Total > 0 ? $"{p.Processed:N0} de {p.Total:N0}" : string.Empty;
        });

        try
        {
            await _settings.UpdateAsync(s =>
            {
                s.DuplicateMinFileSizeBytes = options.MinFileSizeBytes;
                s.VerifyDuplicatesByteByByte = VerifyByteByByte;
            }).ConfigureAwait(true);

            _result = await _scanner.ScanAsync(options, progress, _scanCancellation.Token).ConfigureAwait(true);

            if (_result.WasCancelled)
            {
                PhaseText = "Cancelado";
                _dialogs.Notify("Búsqueda cancelada.", ToastKind.Info);
                return;
            }

            BuildGroups();
        }
        catch (OperationCanceledException)
        {
            PhaseText = "Cancelado";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo durante la búsqueda de duplicados");
            _dialogs.Notify("No hemos podido completar la búsqueda.", ToastKind.Error);
        }
        finally
        {
            IsScanning = false;
            ScanCommand.NotifyCanExecuteChanged();
            OnPropertyChanged(nameof(CanScan));
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        try
        {
            _scanCancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ya había terminado.
        }
    }

    private void BuildGroups()
    {
        Groups.Clear();

        foreach (var group in _result.Groups)
        {
            var files = group.Files
                .Select(f => new DuplicateFileViewModel(f, _safety.Evaluate(f.Path), UpdateSelectionSummary))
                .ToList();

            Groups.Add(new DuplicateGroupViewModel(group, files));
        }

        HasScanned = true;
        ErrorCount = _result.Errors.Count;

        SummaryHeadline = Groups.Count == 0
            ? "Sin duplicados"
            : $"{_result.RedundantFileCount:N0} duplicados · {ByteSize.Format(_result.ReclaimableBytes)} recuperables";

        SummaryDetail = Groups.Count == 0
            ? $"Se han comparado {_result.FilesScanned:N0} archivos y no hay contenido repetido."
            : $"{Groups.Count:N0} grupos · {_result.FilesScanned:N0} archivos analizados en {_result.Elapsed.TotalSeconds:N1} s";

        UpdateSelectionSummary();
    }

    // ---------------------------------------------------------------- selección

    private IEnumerable<DuplicateFileViewModel> AllFiles => Groups.SelectMany(g => g.Files);

    private void UpdateSelectionSummary()
    {
        var selected = AllFiles.Where(f => f.IsSelected).ToList();
        var bytes = selected.Sum(f => f.File.SizeBytes);

        HasSelection = selected.Count > 0;
        SelectionText = selected.Count == 0
            ? "No has marcado ningún archivo"
            : $"{selected.Count:N0} archivos marcados · {ByteSize.Format(bytes)}";
    }

    [RelayCommand]
    private void SelectSuggested()
    {
        var suggestion = _planner.SuggestSelection(_result.Groups);
        foreach (var file in AllFiles) file.IsSelected = suggestion.Contains(file.Path);

        UpdateSelectionSummary();
        _dialogs.Notify("Se conserva la copia con la ruta más corta de cada grupo.", ToastKind.Info);
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var file in AllFiles) file.IsSelected = false;
        UpdateSelectionSummary();
    }

    [RelayCommand]
    private void OpenFile(DuplicateFileViewModel? file)
    {
        if (file is not null) _shell.OpenFile(file.Path);
    }

    [RelayCommand]
    private void RevealFile(DuplicateFileViewModel? file)
    {
        if (file is not null) _shell.RevealInExplorer(file.Path);
    }

    // ---------------------------------------------------------------- acciones

    [RelayCommand]
    private Task RecycleSelectedAsync() => ExecuteAsync(FileActionKind.RecycleBin, null);

    [RelayCommand]
    private Task DeletePermanentlyAsync() => ExecuteAsync(FileActionKind.PermanentDelete, null);

    [RelayCommand]
    private async Task MoveSelectedAsync()
    {
        var destination = _dialogs.PickFolder(
            "Elige dónde mover los duplicados",
            _settings.Current.DefaultMoveFolder);

        if (destination is null) return;

        await _settings.UpdateAsync(s => s.DefaultMoveFolder = destination).ConfigureAwait(true);
        await ExecuteAsync(FileActionKind.Move, destination).ConfigureAwait(true);
    }

    private async Task ExecuteAsync(FileActionKind kind, string? destination)
    {
        if (IsBusy) return;

        var selection = AllFiles.Where(f => f.IsSelected).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selection.Count == 0)
        {
            _dialogs.Notify("No has marcado ningún archivo.", ToastKind.Info);
            return;
        }

        var plan = _planner.Build(_result.Groups, selection, kind, destination);

        if (!plan.CanExecute)
        {
            await _dialogs.ConfirmAsync(new DialogRequest(
                "No se puede continuar",
                string.Join(Environment.NewLine, plan.Blockers),
                ConfirmText: "Entendido",
                CancelText: "Cerrar")).ConfigureAwait(true);
            return;
        }

        if (!await ConfirmPlanAsync(plan).ConfigureAwait(true)) return;

        IsBusy = true;
        try
        {
            var request = new FileActionRequest([.. plan.Included.Select(f => f.Path)], kind, destination);
            var result = await _files.ExecuteAsync(request).ConfigureAwait(true);
            await ReportResultAsync(kind, result).ConfigureAwait(true);
            RemoveProcessedFiles(result.Succeeded);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al ejecutar la operación {Kind}", kind);
            _dialogs.Notify("La operación no se ha podido completar.", ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task<bool> ConfirmPlanAsync(ActionPlan plan)
    {
        var (title, verb) = plan.Kind switch
        {
            FileActionKind.RecycleBin => ("Enviar a la papelera", "se enviarán a la papelera de reciclaje"),
            FileActionKind.PermanentDelete => ("Eliminar definitivamente", "se eliminarán de forma irreversible"),
            _ => ("Mover archivos", $"se moverán a {plan.DestinationFolder}")
        };

        var warnings = new List<string>();
        if (plan.HasWarnings)
        {
            warnings.Add("Alguno de los archivos está en una ubicación que no suele contener datos personales.");
        }
        if (plan.Rejected.Count > 0)
        {
            warnings.Add($"{plan.Rejected.Count} archivo(s) se han descartado por estar en carpetas protegidas.");
        }
        if (plan.Kind == FileActionKind.PermanentDelete)
        {
            warnings.Add("Esta acción no se puede deshacer: los archivos no pasarán por la papelera.");
        }

        return _dialogs.ConfirmAsync(new DialogRequest(
            title,
            $"{plan.Included.Count:N0} archivos {verb}.",
            ConfirmText: plan.Kind == FileActionKind.Move ? "Mover" : title,
            IsDestructive: plan.Kind != FileActionKind.Move,
            WarningText: warnings.Count > 0 ? string.Join(" ", warnings) : null,
            Details: [.. plan.Included.Select(f => $"{ByteSize.Format(f.SizeBytes)}   {f.Path}")],
            Summary: $"Espacio afectado: {ByteSize.Format(plan.TotalBytes)}"));
    }

    private async Task ReportResultAsync(FileActionKind kind, FileActionResult result)
    {
        var action = kind switch
        {
            FileActionKind.RecycleBin => "enviados a la papelera",
            FileActionKind.PermanentDelete => "eliminados",
            _ => "movidos"
        };

        if (result.IsCompleteSuccess)
        {
            _dialogs.Notify(
                $"{result.Succeeded.Count:N0} archivos {action} · {ByteSize.Format(result.BytesAffected)} liberados.",
                ToastKind.Success);
            return;
        }

        // Nunca se deja una operación a medias sin decir exactamente qué falló.
        await _dialogs.ConfirmAsync(new DialogRequest(
            result.Succeeded.Count > 0 ? "Operación completada con incidencias" : "No se ha podido completar",
            $"{result.Succeeded.Count:N0} archivos {action}. {result.Failed.Count:N0} no se han podido procesar.",
            ConfirmText: "Entendido",
            CancelText: "Cerrar",
            Details: [.. result.Failed.Select(f => $"{f.UserMessage}   {f.Path}")],
            Summary: $"Espacio liberado: {ByteSize.Format(result.BytesAffected)}")).ConfigureAwait(true);
    }

    /// <summary>Quita de la lista lo ya procesado y descarta los grupos que dejan de serlo.</summary>
    private void RemoveProcessedFiles(IReadOnlyList<string> paths)
    {
        var processed = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var group in Groups.ToList())
        {
            foreach (var file in group.Files.Where(f => processed.Contains(f.Path)).ToList())
            {
                group.Files.Remove(file);
            }

            if (group.Files.Count < 2) Groups.Remove(group);
        }

        _result = _result with
        {
            Groups = [.. _result.Groups
                .Select(g => g with { Files = [.. g.Files.Where(f => !processed.Contains(f.Path))] })
                .Where(g => g.Files.Count > 1)]
        };

        SummaryHeadline = Groups.Count == 0
            ? "Sin duplicados pendientes"
            : $"{_result.RedundantFileCount:N0} duplicados · {ByteSize.Format(_result.ReclaimableBytes)} recuperables";

        UpdateSelectionSummary();
    }
}
