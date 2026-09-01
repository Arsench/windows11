using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zenith.App.Localization;
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

    public string ModifiedText => File.LastWriteUtc.ToLocalTime().ToString("g", Loc.Instance.Culture);

    public bool IsBlocked => Verdict.Level == SafetyLevel.Blocked;

    public bool HasWarning => Verdict.Level == SafetyLevel.Warning;

    public string? WarningText => Verdict.Level == SafetyLevel.Allowed ? null : Present.Safety(Verdict);

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

    public string Title => Loc.Instance.Format("DuplicatesGroupTitle", Group.Index.ToString("00", Loc.Instance.Culture));

    public string FileName => Group.Files[0].FileName;

    public string SizeText => ByteSize.Format(Group.FileSizeBytes);

    public string CopiesText => MetricFormatter.Count(Group.Files.Count, "CountCopyOne", "CountCopyMany");

    public string ReclaimableText => Loc.Instance.Format("DuplicatesReclaimable", ByteSize.Format(Group.ReclaimableBytes));
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
    [ObservableProperty] private string _selectionText = string.Empty;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string _errorText = string.Empty;

    [ObservableProperty] private long _minimumSizeKilobytes = 1;
    [ObservableProperty] private bool _verifyByteByByte = true;

    private static Loc L => Loc.Instance;

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

        SelectionText = L["DuplicatesNoSelection"];
        L.LanguageChanged += (_, _) => RefreshLocalizedText();
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

    /// <summary>Rehace los textos ya compuestos cuando cambia el idioma.</summary>
    private void RefreshLocalizedText()
    {
        if (HasScanned) BuildSummary();
        else SelectionText = L["DuplicatesNoSelection"];

        // Los grupos exponen su texto a través de propiedades calculadas: basta
        // con reconstruir la lista para que se reevalúen.
        if (Groups.Count > 0) BuildGroups();
        UpdateSelectionSummary();
    }

    // ---------------------------------------------------------------- carpetas

    [RelayCommand]
    private void AddFolder()
    {
        var folder = _dialogs.PickFolder(L["DuplicatesPickFolder"]);
        if (folder is null) return;

        if (Folders.Any(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase)))
        {
            _dialogs.Notify(L["DuplicatesFolderAlreadyAdded"], ToastKind.Info);
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
        PhaseText = Present.Phase(DuplicatePhase.Idle);
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
            PhaseText = Present.Phase(p.Phase);
            ProgressDetailText = p.Phase == DuplicatePhase.Enumerating
                ? L.Format("ProgressFound", MetricFormatter.Number(p.FilesDiscovered))
                : p.Total > 0
                    ? L.Format("ProgressOf", MetricFormatter.Number(p.Processed), MetricFormatter.Number(p.Total))
                    : string.Empty;
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
                PhaseText = Present.Phase(DuplicatePhase.Cancelled);
                _dialogs.Notify(L["DuplicatesCancelled"], ToastKind.Info);
                return;
            }

            BuildGroups();
            BuildSummary();
            HasScanned = true;
        }
        catch (OperationCanceledException)
        {
            PhaseText = Present.Phase(DuplicatePhase.Cancelled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo durante la búsqueda de duplicados");
            _dialogs.Notify(L["DuplicatesScanFailed"], ToastKind.Error);
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
        // Se conserva lo que el usuario tenía marcado al rehacer la lista.
        var selected = Groups
            .SelectMany(g => g.Files)
            .Where(f => f.IsSelected)
            .Select(f => f.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Groups.Clear();

        foreach (var group in _result.Groups)
        {
            var files = group.Files
                .Select(f => new DuplicateFileViewModel(f, _safety.Evaluate(f.Path), UpdateSelectionSummary)
                {
                    IsSelected = selected.Contains(f.Path)
                })
                .ToList();

            Groups.Add(new DuplicateGroupViewModel(group, files));
        }
    }

    private void BuildSummary()
    {
        ErrorCount = _result.Errors.Count;
        ErrorText = ErrorCount == 0
            ? string.Empty
            : L.Format("DuplicatesUnreadable", MetricFormatter.Number(ErrorCount));

        SummaryHeadline = Groups.Count == 0
            ? L["DuplicatesNone"]
            : L.Format("DuplicatesSummary",
                MetricFormatter.Number(_result.RedundantFileCount),
                ByteSize.Format(_result.ReclaimableBytes));

        SummaryDetail = Groups.Count == 0
            ? L.Format("DuplicatesNoneDetail", MetricFormatter.Number(_result.FilesScanned))
            : L.Format("DuplicatesSummaryDetail",
                MetricFormatter.Number(Groups.Count),
                MetricFormatter.Number(_result.FilesScanned),
                MetricFormatter.Seconds(_result.Elapsed));

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
            ? L["DuplicatesNoSelection"]
            : L.Format("DuplicatesSelection", MetricFormatter.Number(selected.Count), ByteSize.Format(bytes));
    }

    [RelayCommand]
    private void SelectSuggested()
    {
        var suggestion = _planner.SuggestSelection(_result.Groups);
        foreach (var file in AllFiles) file.IsSelected = suggestion.Contains(file.Path);

        UpdateSelectionSummary();
        _dialogs.Notify(L["DuplicatesSuggestedToast"], ToastKind.Info);
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
        var destination = _dialogs.PickFolder(L["DuplicatesMovePick"], _settings.Current.DefaultMoveFolder);
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
            _dialogs.Notify(L["ActionNothingSelected"], ToastKind.Info);
            return;
        }

        var plan = _planner.Build(_result.Groups, selection, kind, destination);

        if (!plan.CanExecute)
        {
            await _dialogs.ConfirmAsync(new DialogRequest(
                L["ActionCannotContinue"],
                string.Join(Environment.NewLine, plan.Blockers.Select(Present.Blocker)),
                ConfirmText: L["CommonUnderstood"],
                CancelText: L["CommonClose"])).ConfigureAwait(true);
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
            _dialogs.Notify(L["ActionFailedGeneric"], ToastKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task<bool> ConfirmPlanAsync(ActionPlan plan)
    {
        var title = L[plan.Kind switch
        {
            FileActionKind.RecycleBin => "ActionRecycleTitle",
            FileActionKind.PermanentDelete => "ActionDeleteTitle",
            _ => "ActionMoveTitle"
        }];

        var verb = plan.Kind switch
        {
            FileActionKind.RecycleBin => L["ActionRecycleVerb"],
            FileActionKind.PermanentDelete => L["ActionDeleteVerb"],
            _ => L.Format("ActionMoveVerb", plan.DestinationFolder)
        };

        var warnings = new List<string>();
        if (plan.HasWarnings) warnings.Add(L["ActionWarnMixed"]);
        if (plan.Rejected.Count > 0) warnings.Add(L.Format("ActionWarnRejected", plan.Rejected.Count));
        if (plan.Kind == FileActionKind.PermanentDelete) warnings.Add(L["ActionWarnPermanent"]);

        return _dialogs.ConfirmAsync(new DialogRequest(
            title,
            L.Format("ActionSummaryLine", MetricFormatter.Number(plan.Included.Count), verb),
            ConfirmText: plan.Kind == FileActionKind.Move ? L["ActionMoveConfirm"] : title,
            CancelText: L["CommonCancel"],
            IsDestructive: plan.Kind != FileActionKind.Move,
            WarningText: warnings.Count > 0 ? string.Join(" ", warnings) : null,
            Details: [.. plan.Included.Select(f => $"{ByteSize.Format(f.SizeBytes)}   {f.Path}")],
            Summary: L.Format("ActionSpaceAffected", ByteSize.Format(plan.TotalBytes))));
    }

    private async Task ReportResultAsync(FileActionKind kind, FileActionResult result)
    {
        var action = L[kind switch
        {
            FileActionKind.RecycleBin => "ActionDoneRecycled",
            FileActionKind.PermanentDelete => "ActionDoneDeleted",
            _ => "ActionDoneMoved"
        }];

        if (result.IsCompleteSuccess)
        {
            _dialogs.Notify(
                L.Format("ActionSuccessToast",
                    MetricFormatter.Number(result.Succeeded.Count), action, ByteSize.Format(result.BytesAffected)),
                ToastKind.Success);
            return;
        }

        // Nunca se deja una operación a medias sin decir exactamente qué falló.
        await _dialogs.ConfirmAsync(new DialogRequest(
            L[result.Succeeded.Count > 0 ? "ActionPartialTitle" : "ActionFailedTitle"],
            L.Format("ActionPartialMessage",
                MetricFormatter.Number(result.Succeeded.Count), action, MetricFormatter.Number(result.Failed.Count)),
            ConfirmText: L["CommonUnderstood"],
            CancelText: L["CommonClose"],
            Details: [.. result.Failed.Select(f => $"{Present.FileActionFailure(f)}   {f.Path}")],
            Summary: L.Format("ActionSpaceFreed", ByteSize.Format(result.BytesAffected)))).ConfigureAwait(true);
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
            ? L["DuplicatesNothingLeft"]
            : L.Format("DuplicatesSummary",
                MetricFormatter.Number(_result.RedundantFileCount),
                ByteSize.Format(_result.ReclaimableBytes));

        UpdateSelectionSummary();
    }
}
