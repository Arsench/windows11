using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zenith.App.Localization;
using Zenith.App.Services;
using Zenith.Core.Abstractions;
using Zenith.Core.Primitives;
using Zenith.Core.Storage;

namespace Zenith.App.ViewModels;

public sealed record FolderEntryViewModel(FolderNode Node)
{
    public string Name => Node.Name;

    public string SizeText => ByteSize.Format(Node.TotalSizeBytes);

    public double Percent => Node.PercentOfParent;

    public string PercentText => MetricFormatter.Number(Node.PercentOfParent, 1) + " %";

    public bool HasErrors => Node.HasErrors;

    public bool CanDrillDown => Node.Children.Count > 0;
}

public sealed record CategoryEntryViewModel(string Name, string SizeText, double Percent, string FileCountText);

public sealed record LargeFileViewModel(string Name, string Path, string SizeText);

/// <summary>Unidades del equipo y análisis de en qué se está yendo el espacio.</summary>
public sealed partial class StorageViewModel : ObservableObject, INavigationAware
{
    private readonly IStorageProvider _storage;
    private readonly StorageAnalyzer _analyzer;
    private readonly IShellService _shell;
    private readonly IDialogService _dialogs;
    private readonly ILogger<StorageViewModel> _logger;

    private CancellationTokenSource? _scanCancellation;

    [ObservableProperty] private bool _isLoadingVolumes = true;
    [ObservableProperty] private VolumeViewModel? _selectedVolume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAnalyzerPlaceholder))]
    private bool _isScanning;
    [ObservableProperty] private string _scanStatusText = string.Empty;
    [ObservableProperty] private string _scanTargetText = string.Empty;
    [ObservableProperty] private string _scanSummaryText = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAnalyzerPlaceholder))]
    private bool _hasResult;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private string _skippedText = string.Empty;

    public StorageViewModel(
        IStorageProvider storage,
        StorageAnalyzer analyzer,
        IShellService shell,
        IDialogService dialogs,
        ILogger<StorageViewModel> logger)
    {
        _storage = storage;
        _analyzer = analyzer;
        _shell = shell;
        _dialogs = dialogs;
        _logger = logger;
    }

    /// <summary>El estado vacío solo aparece cuando de verdad no hay nada que enseñar.</summary>
    public bool ShowAnalyzerPlaceholder => !HasResult && !IsScanning;

    public ObservableCollection<VolumeViewModel> Volumes { get; } = [];

    public ObservableCollection<FolderNode> Breadcrumb { get; } = [];

    public ObservableCollection<FolderEntryViewModel> Folders { get; } = [];

    public ObservableCollection<CategoryEntryViewModel> Categories { get; } = [];

    public ObservableCollection<LargeFileViewModel> LargestFiles { get; } = [];

    public void OnNavigatedTo() => _ = LoadVolumesAsync();

    public void OnNavigatedFrom()
    {
        // Un análisis en curso se detiene al salir: nadie va a ver el resultado.
        if (IsScanning) CancelScan();
    }

    private async Task LoadVolumesAsync()
    {
        try
        {
            IsLoadingVolumes = Volumes.Count == 0;
            var volumes = await _storage.GetVolumesAsync().ConfigureAwait(true);

            var previous = SelectedVolume?.RootPath;
            Volumes.Clear();
            foreach (var volume in volumes) Volumes.Add(new VolumeViewModel(volume));

            SelectedVolume = Volumes.FirstOrDefault(v => v.RootPath == previous) ?? Volumes.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se han podido cargar las unidades");
            _dialogs.Notify(Loc.Instance["StorageDrivesFailed"], ToastKind.Error);
        }
        finally
        {
            IsLoadingVolumes = false;
        }
    }

    [RelayCommand]
    private Task AnalyzeVolume()
    {
        if (SelectedVolume is null)
        {
            _dialogs.Notify(Loc.Instance["StorageSelectDriveFirst"], ToastKind.Info);
            return Task.CompletedTask;
        }

        return AnalyzeAsync(SelectedVolume.RootPath);
    }

    [RelayCommand]
    private Task AnalyzeFolder()
    {
        var folder = _dialogs.PickFolder(Loc.Instance["StorageChooseFolderPick"], SelectedVolume?.RootPath);
        return folder is null ? Task.CompletedTask : AnalyzeAsync(folder);
    }

    private async Task AnalyzeAsync(string path)
    {
        if (IsScanning) return;

        _scanCancellation?.Dispose();
        _scanCancellation = new CancellationTokenSource();

        IsScanning = true;
        HasResult = false;
        ScanTargetText = path;
        ScanStatusText = Loc.Instance["StorageAnalysing"];
        Folders.Clear();
        Categories.Clear();
        LargestFiles.Clear();
        Breadcrumb.Clear();

        // Progress<T> captura el contexto de la UI: los informes llegan ya al hilo correcto.
        var progress = new Progress<StorageScanProgress>(p => ScanStatusText = Loc.Instance.Format(
            "StorageScanProgress",
            MetricFormatter.Number(p.FilesScanned),
            ByteSize.Format(p.BytesScanned),
            MetricFormatter.Number(p.DirectoriesScanned)));

        try
        {
            var result = await _analyzer
                .AnalyzeAsync(path, progress, _scanCancellation.Token)
                .ConfigureAwait(true);

            if (result.WasCancelled)
            {
                ScanStatusText = Loc.Instance["StorageScanCancelled"];
                _dialogs.Notify(ScanStatusText, ToastKind.Info);
                return;
            }

            ApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            ScanStatusText = Loc.Instance["StorageScanCancelled"];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo al analizar {Path}", path);
            _dialogs.Notify(Loc.Instance["StorageAnalyseFailed"], ToastKind.Error);
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void ApplyResult(StorageScanResult result)
    {
        HasResult = result.Root is not null;
        SkippedCount = result.Errors.Count;
        SkippedText = SkippedCount == 0
            ? string.Empty
            : Loc.Instance.Format("StorageSkipped", MetricFormatter.Number(SkippedCount));

        ScanSummaryText = Loc.Instance.Format(
            "StorageScanSummary",
            ByteSize.Format(result.TotalBytes),
            MetricFormatter.Number(result.FileCount),
            MetricFormatter.Seconds(result.Elapsed));
        ScanStatusText = string.Empty;

        Categories.Clear();
        foreach (var category in result.Categories)
        {
            Categories.Add(new CategoryEntryViewModel(
                Present.Category(category.Category),
                ByteSize.Format(category.SizeBytes),
                result.TotalBytes > 0 ? category.SizeBytes * 100d / result.TotalBytes : 0,
                MetricFormatter.Count(category.FileCount, "CountFileOne", "CountFileMany")));
        }

        LargestFiles.Clear();
        foreach (var file in result.LargestFiles.Take(20))
        {
            LargestFiles.Add(new LargeFileViewModel(file.FileName, file.Path, ByteSize.Format(file.SizeBytes)));
        }

        if (result.Root is not null) NavigateTo(result.Root);
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
            // La operación ya había terminado.
        }
    }

    [RelayCommand]
    private void DrillDown(FolderEntryViewModel? entry)
    {
        if (entry is null || !entry.CanDrillDown) return;
        NavigateTo(entry.Node);
    }

    [RelayCommand]
    private void NavigateBreadcrumb(FolderNode? node)
    {
        if (node is null) return;
        NavigateTo(node);
    }

    [RelayCommand]
    private void OpenCurrentFolder()
    {
        var node = Breadcrumb.LastOrDefault();
        if (node is not null) _shell.OpenFolder(node.FullPath);
    }

    [RelayCommand]
    private void RevealFile(LargeFileViewModel? file)
    {
        if (file is not null) _shell.RevealInExplorer(file.Path);
    }

    private void NavigateTo(FolderNode node)
    {
        // La miga de pan se reconstruye desde el nodo hacia la raíz analizada.
        var chain = new List<FolderNode>();
        for (var current = node; current is not null; current = current.Parent) chain.Insert(0, current);

        Breadcrumb.Clear();
        foreach (var item in chain) Breadcrumb.Add(item);

        Folders.Clear();
        foreach (var child in node.Children.Take(200)) Folders.Add(new FolderEntryViewModel(child));
    }
}
