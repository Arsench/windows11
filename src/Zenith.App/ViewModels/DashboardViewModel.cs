using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;
using Zenith.Core.Monitoring;
using Zenith.Core.Primitives;

namespace Zenith.App.ViewModels;

/// <summary>Vista general del equipo: lo primero que se ve al abrir Zenith.</summary>
public sealed partial class DashboardViewModel : MonitoringViewModelBase
{
    private static readonly TimeSpan VolumeRefreshInterval = TimeSpan.FromSeconds(60);

    private readonly IStorageProvider _storage;
    private readonly ILogger<DashboardViewModel> _logger;
    private DateTimeOffset _volumesRefreshedAt = DateTimeOffset.MinValue;

    [ObservableProperty] private string _greeting = "Hola";
    [ObservableProperty] private string _machineSummary = string.Empty;

    [ObservableProperty] private string _cpuValueText = "—";
    [ObservableProperty] private string _cpuSecondaryText = string.Empty;
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double[] _cpuHistory = [];

    [ObservableProperty] private string _memoryValueText = "—";
    [ObservableProperty] private string _memorySecondaryText = string.Empty;
    [ObservableProperty] private double _memoryUsage;
    [ObservableProperty] private double[] _memoryHistory = [];

    [ObservableProperty] private string _gpuValueText = "—";
    [ObservableProperty] private string _gpuSecondaryText = string.Empty;
    [ObservableProperty] private double _gpuUsage;
    [ObservableProperty] private double[] _gpuHistory = [];

    [ObservableProperty] private string _temperatureValueText = "—";
    [ObservableProperty] private string _temperatureSecondaryText = string.Empty;
    [ObservableProperty] private bool _isTemperatureAvailable;

    [ObservableProperty] private bool _isLoadingVolumes = true;
    [ObservableProperty] private bool _showNoVolumes;

    public DashboardViewModel(
        MonitoringService monitoring,
        IStorageProvider storage,
        ILogger<DashboardViewModel> logger) : base(monitoring)
    {
        _storage = storage;
        _logger = logger;
        Greeting = BuildGreeting();
    }

    public ObservableCollection<VolumeViewModel> Volumes { get; } = [];

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();
        Greeting = BuildGreeting();
        MachineSummary = $"{Environment.MachineName} · {Monitoring.CpuInfo.Name}";
        _ = RefreshVolumesAsync(force: false);
    }

    [RelayCommand]
    private Task RefreshVolumes() => RefreshVolumesAsync(force: true);

    private async Task RefreshVolumesAsync(bool force)
    {
        // Enumerar unidades no es gratis (puede despertar discos): cadencia baja.
        if (!force && DateTimeOffset.UtcNow - _volumesRefreshedAt < VolumeRefreshInterval) return;

        try
        {
            IsLoadingVolumes = Volumes.Count == 0;
            var volumes = await _storage.GetVolumesAsync().ConfigureAwait(true);

            Volumes.Clear();
            foreach (var volume in volumes)
            {
                // Las unidades de red y ópticas no aportan nada al panel principal.
                if (volume.Media is DriveMedia.Network or DriveMedia.Optical) continue;
                Volumes.Add(new VolumeViewModel(volume));
            }

            _volumesRefreshedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se han podido actualizar las unidades");
        }
        finally
        {
            IsLoadingVolumes = false;
            ShowNoVolumes = Volumes.Count == 0;
        }
    }

    protected override void Apply(SystemSnapshot snapshot)
    {
        CpuValueText = MetricFormatter.Percent(snapshot.Cpu.TotalUsagePercent);
        CpuUsage = snapshot.Cpu.TotalUsagePercent.ValueOr(0);
        CpuSecondaryText = MetricFormatter.Ghz(snapshot.Cpu.CurrentClockGhz);
        CpuHistory = Monitoring.CpuHistory.ToArray();

        var memory = snapshot.Memory;
        if (memory.TotalBytes > 0)
        {
            MemoryValueText = memory.UsagePercent.ToString("N0") + " %";
            MemorySecondaryText = ByteSize.FormatPair(memory.UsedBytes, memory.TotalBytes);
            MemoryUsage = memory.UsagePercent;
        }
        else
        {
            MemoryValueText = MetricFormatter.Unavailable;
            MemorySecondaryText = string.Empty;
        }
        MemoryHistory = Monitoring.MemoryHistory.ToArray();

        ApplyGpu(snapshot);
        ApplyThermal(snapshot);
    }

    private void ApplyGpu(SystemSnapshot snapshot)
    {
        var sample = snapshot.Gpus.FirstOrDefault();
        if (sample is null)
        {
            GpuValueText = MetricFormatter.Unavailable;
            GpuSecondaryText = "No se ha detectado ningún adaptador";
            GpuUsage = 0;
            return;
        }

        GpuValueText = MetricFormatter.Percent(sample.UtilizationPercent);
        GpuUsage = sample.UtilizationPercent.ValueOr(0);
        GpuHistory = Monitoring.GpuHistory.ToArray();

        var adapter = Monitoring.GpuAdapters.FirstOrDefault(a => a.AdapterId == sample.AdapterId);
        GpuSecondaryText = adapter?.Name ?? "Adaptador gráfico";
    }

    private void ApplyThermal(SystemSnapshot snapshot)
    {
        var thermal = snapshot.Thermal;

        var cpuTemperature = thermal.HottestFor(ThermalComponent.Cpu);
        if (cpuTemperature is { } celsius)
        {
            TemperatureValueText = MetricFormatter.Celsius(celsius);
            TemperatureSecondaryText = "CPU";
            IsTemperatureAvailable = true;
            return;
        }

        var anyReading = thermal.Readings.Count > 0
            ? thermal.Readings.MaxBy(r => r.Celsius)
            : null;

        if (anyReading is not null)
        {
            TemperatureValueText = MetricFormatter.Celsius(anyReading.Celsius);
            TemperatureSecondaryText = anyReading.Source == ThermalSource.AcpiThermalZone
                ? "Zona térmica ACPI (no es la CPU)"
                : anyReading.SensorName;
            IsTemperatureAvailable = true;
            return;
        }

        TemperatureValueText = "—";
        TemperatureSecondaryText = thermal.UnavailableReason ?? "Sensor no disponible";
        IsTemperatureAvailable = false;
    }

    private static string BuildGreeting() => DateTime.Now.Hour switch
    {
        >= 6 and < 13 => "Buenos días",
        >= 13 and < 21 => "Buenas tardes",
        _ => "Buenas noches"
    };
}
