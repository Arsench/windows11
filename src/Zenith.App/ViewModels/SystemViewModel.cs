using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Zenith.App.Localization;
using Zenith.App.Services;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;
using Zenith.Core.Monitoring;
using Zenith.Core.Primitives;

namespace Zenith.App.ViewModels;

public sealed partial class CoreUsageViewModel(int index) : ObservableObject
{
    [ObservableProperty] private double _usage;

    public string Label => Loc.Instance.Format("SystemCoreLabel", index + 1);
}

public sealed partial class GpuViewModel(GpuInfo info) : ObservableObject
{
    [ObservableProperty] private string _usageText = "—";
    [ObservableProperty] private double _usage;
    [ObservableProperty] private string _memoryText = "—";
    [ObservableProperty] private string _temperatureText = "—";

    public string AdapterId => info.AdapterId;

    public string Name => string.IsNullOrWhiteSpace(info.Name)
        ? Loc.Instance["CommonGraphicsAdapter"]
        : info.Name;

    public string DriverText => string.IsNullOrWhiteSpace(info.DriverVersion)
        ? Loc.Instance["SystemNoDriverVersion"]
        : Loc.Instance.Format("SystemDriverVersion", info.DriverVersion);

    public string TotalMemoryText => MetricFormatter.Bytes(info.DedicatedMemoryBytes);
}

public sealed record MemoryModuleViewModel(string Slot, string Capacity, string Speed, string Manufacturer);

public sealed record ThermalReadingViewModel(string Name, string Value, string Source);

public sealed partial class ProcessViewModel(ProcessSample sample) : ObservableObject
{
    public int ProcessId => sample.ProcessId;

    public string Name => sample.Name;

    public string CpuText => MetricFormatter.Percent(sample.CpuPercent, 1);

    public string MemoryText => ByteSize.Format(sample.WorkingSetBytes);
}

/// <summary>Detalle en vivo de CPU, memoria, gráfica, sensores y procesos.</summary>
public sealed partial class SystemViewModel : MonitoringViewModelBase
{
    private static readonly TimeSpan ProcessRefreshInterval = TimeSpan.FromSeconds(3);
    private const int TopProcessCount = 8;

    private readonly IProcessProvider _processes;
    private readonly IMemoryProvider _memory;
    private readonly IThermalProvider _thermal;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SystemViewModel> _logger;
    private readonly DispatcherTimer _processTimer;

    private bool _modulesLoaded;
    private bool _processRefreshInFlight;

    [ObservableProperty] private string _cpuName = "—";
    [ObservableProperty] private string _cpuCoresText = "—";
    [ObservableProperty] private string _cpuBaseClockText = "—";
    [ObservableProperty] private string _cpuCurrentClockText = "—";
    [ObservableProperty] private string _cpuUsageText = "—";
    [ObservableProperty] private string _cpuTemperatureText = "—";
    [ObservableProperty] private double _cpuUsage;
    [ObservableProperty] private double[] _cpuHistory = [];

    [ObservableProperty] private string _memoryUsedText = "—";
    [ObservableProperty] private string _memoryAvailableText = "—";
    [ObservableProperty] private string _memoryTotalText = "—";
    [ObservableProperty] private string _memoryCommittedText = "—";
    [ObservableProperty] private string _memoryUsageText = "—";
    [ObservableProperty] private double _memoryUsage;
    [ObservableProperty] private double[] _memoryHistory = [];

    [ObservableProperty] private bool _areSensorsEnabled;
    [ObservableProperty] private string _sensorStatusText = string.Empty;

    public SystemViewModel(
        MonitoringService monitoring,
        IProcessProvider processes,
        IMemoryProvider memory,
        IThermalProvider thermal,
        ISettingsStore settings,
        IDialogService dialogs,
        ILogger<SystemViewModel> logger) : base(monitoring)
    {
        _processes = processes;
        _memory = memory;
        _thermal = thermal;
        _settings = settings;
        _dialogs = dialogs;
        _logger = logger;

        _processTimer = new DispatcherTimer { Interval = ProcessRefreshInterval };
        _processTimer.Tick += (_, _) => _ = RefreshProcessesAsync();

        SensorStatusText = Loc.Instance["SystemSensorsHint"];
        Loc.Instance.LanguageChanged += (_, _) => RefreshLocalizedText();
    }

    /// <summary>Rehace los textos que compone el ViewModel al cambiar de idioma.</summary>
    private void RefreshLocalizedText()
    {
        SensorStatusText = _thermal.IsEnabled
            ? Loc.Instance["SystemSensorsOn"]
            : Loc.Instance["SystemSensorsHint"];

        _modulesLoaded = false;
        MemoryModules.Clear();
        _ = LoadMemoryModulesAsync();

        ApplyStaticCpuInfo();
        Apply(Monitoring.Latest);
    }

    private void ApplyStaticCpuInfo()
    {
        var info = Monitoring.CpuInfo;

        CpuName = string.IsNullOrWhiteSpace(info.Name) ? Loc.Instance["CommonUnknownProcessor"] : info.Name;

        CpuCoresText = info.PhysicalCores.HasValue && info.LogicalProcessors.HasValue
            ? Loc.Instance.Format("SystemCoresValue", info.PhysicalCores.Value, info.LogicalProcessors.Value)
            : Loc.Instance.Format("SystemLogicalProcessors", MetricFormatter.Integer(info.LogicalProcessors));

        CpuBaseClockText = MetricFormatter.Ghz(info.BaseClockGhz);
    }

    public ObservableCollection<CoreUsageViewModel> Cores { get; } = [];

    public ObservableCollection<GpuViewModel> Gpus { get; } = [];

    public ObservableCollection<MemoryModuleViewModel> MemoryModules { get; } = [];

    public ObservableCollection<ThermalReadingViewModel> ThermalReadings { get; } = [];

    public ObservableCollection<ProcessViewModel> TopProcesses { get; } = [];

    public override void OnNavigatedTo()
    {
        base.OnNavigatedTo();

        ApplyStaticCpuInfo();
        AreSensorsEnabled = _thermal.IsEnabled;

        SyncGpuList();
        _ = LoadMemoryModulesAsync();
        _ = RefreshProcessesAsync();
        _processTimer.Start();
    }

    public override void OnNavigatedFrom()
    {
        base.OnNavigatedFrom();
        _processTimer.Stop();
    }

    private void SyncGpuList()
    {
        if (Gpus.Count == Monitoring.GpuAdapters.Count) return;

        Gpus.Clear();
        foreach (var adapter in Monitoring.GpuAdapters) Gpus.Add(new GpuViewModel(adapter));
    }

    private async Task LoadMemoryModulesAsync()
    {
        if (_modulesLoaded) return;
        _modulesLoaded = true;

        try
        {
            var modules = await _memory.GetModulesAsync().ConfigureAwait(true);
            MemoryModules.Clear();

            foreach (var module in modules)
            {
                MemoryModules.Add(new MemoryModuleViewModel(
                    string.IsNullOrWhiteSpace(module.BankLabel) ? Loc.Instance["SystemMemoryModule"] : module.BankLabel,
                    ByteSize.Format(module.CapacityBytes),
                    MetricFormatter.Mhz(module.SpeedMhz),
                    string.IsNullOrWhiteSpace(module.Manufacturer)
                        ? Loc.Instance["SystemUnknownManufacturer"]
                        : module.Manufacturer));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se han podido leer los módulos de memoria");
        }
    }

    private async Task RefreshProcessesAsync()
    {
        // Si una consulta tarda más que el intervalo, no encolamos otra encima.
        if (_processRefreshInFlight) return;
        _processRefreshInFlight = true;

        try
        {
            var processes = await _processes.GetTopProcessesAsync(TopProcessCount).ConfigureAwait(true);

            TopProcesses.Clear();
            foreach (var process in processes) TopProcesses.Add(new ProcessViewModel(process));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se han podido actualizar los procesos");
        }
        finally
        {
            _processRefreshInFlight = false;
        }
    }

    [RelayCommand]
    private async Task ToggleSensorsAsync()
    {
        if (_thermal.IsEnabled)
        {
            _thermal.Disable();
            await _settings.UpdateAsync(s => s.EnableHardwareSensors = false).ConfigureAwait(true);

            AreSensorsEnabled = false;
            ThermalReadings.Clear();
            SensorStatusText = Loc.Instance["SystemSensorsOff"];
            return;
        }

        var l = Loc.Instance;
        var confirmed = await _dialogs.ConfirmAsync(new DialogRequest(
            l["SystemSensorsDialogTitle"],
            l["SystemSensorsDialogMessage"],
            ConfirmText: l["SystemSensorsDialogConfirm"],
            CancelText: l["CommonCancel"],
            WarningText: l["SystemSensorsDialogWarning"])).ConfigureAwait(true);

        if (!confirmed) return;

        var failure = await _thermal.TryEnableAsync().ConfigureAwait(true);
        if (failure != ThermalUnavailableReason.None)
        {
            SensorStatusText = Present.Thermal(failure);
            _dialogs.Notify(SensorStatusText, ToastKind.Warning);
            return;
        }

        await _settings.UpdateAsync(s => s.EnableHardwareSensors = true).ConfigureAwait(true);
        AreSensorsEnabled = true;
        SensorStatusText = l["SystemSensorsOn"];
        _dialogs.Notify(l["SystemSensorsEnabledToast"], ToastKind.Success);
    }

    protected override void Apply(SystemSnapshot snapshot)
    {
        ApplyCpu(snapshot.Cpu, snapshot.Thermal);
        ApplyMemory(snapshot.Memory);
        ApplyGpus(snapshot);
        ApplyThermal(snapshot.Thermal);
    }

    private void ApplyCpu(CpuSample cpu, ThermalSnapshot thermal)
    {
        CpuUsageText = MetricFormatter.Percent(cpu.TotalUsagePercent);
        CpuUsage = cpu.TotalUsagePercent.ValueOr(0);
        CpuCurrentClockText = MetricFormatter.Ghz(cpu.CurrentClockGhz);
        CpuHistory = Monitoring.CpuHistory.ToArray();
        CpuTemperatureText = MetricFormatter.Celsius(thermal.HottestFor(ThermalComponent.Cpu));

        if (Cores.Count != cpu.PerCoreUsagePercent.Count)
        {
            Cores.Clear();
            for (var i = 0; i < cpu.PerCoreUsagePercent.Count; i++) Cores.Add(new CoreUsageViewModel(i));
        }

        for (var i = 0; i < Cores.Count; i++) Cores[i].Usage = cpu.PerCoreUsagePercent[i];
    }

    private void ApplyMemory(MemorySample memory)
    {
        if (memory.TotalBytes <= 0) return;

        MemoryUsedText = ByteSize.Format(memory.UsedBytes);
        MemoryAvailableText = ByteSize.Format(memory.AvailableBytes);
        MemoryTotalText = ByteSize.Format(memory.TotalBytes);
        MemoryCommittedText = MetricFormatter.Bytes(memory.CommittedBytes);
        MemoryUsageText = MetricFormatter.Number(memory.UsagePercent, 0) + " %";
        MemoryUsage = memory.UsagePercent;
        MemoryHistory = Monitoring.MemoryHistory.ToArray();
    }

    private void ApplyGpus(SystemSnapshot snapshot)
    {
        SyncGpuList();

        foreach (var gpu in Gpus)
        {
            var sample = snapshot.Gpus.FirstOrDefault(s => s.AdapterId == gpu.AdapterId);
            if (sample is null) continue;

            gpu.UsageText = MetricFormatter.Percent(sample.UtilizationPercent);
            gpu.Usage = sample.UtilizationPercent.ValueOr(0);
            gpu.MemoryText = MetricFormatter.Bytes(sample.DedicatedMemoryUsedBytes);
            gpu.TemperatureText = MetricFormatter.Celsius(snapshot.Thermal.HottestFor(ThermalComponent.Gpu));
        }
    }

    private void ApplyThermal(ThermalSnapshot thermal)
    {
        if (thermal.Readings.Count == 0)
        {
            if (ThermalReadings.Count > 0) ThermalReadings.Clear();
            if (_thermal.IsEnabled && thermal.UnavailableReason != ThermalUnavailableReason.None)
            {
                SensorStatusText = Present.Thermal(thermal.UnavailableReason);
            }
            return;
        }

        // Reconstruir la lista solo si cambia el conjunto de sensores.
        if (ThermalReadings.Count != thermal.Readings.Count)
        {
            ThermalReadings.Clear();
            foreach (var reading in thermal.Readings) ThermalReadings.Add(ToViewModel(reading));
            return;
        }

        for (var i = 0; i < thermal.Readings.Count; i++) ThermalReadings[i] = ToViewModel(thermal.Readings[i]);
    }

    private static ThermalReadingViewModel ToViewModel(ThermalReading reading) => new(
        Present.SensorName(reading),
        MetricFormatter.Celsius(reading.Celsius),
        Present.ThermalSourceLabel(reading.Source));
}
