using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;

namespace Zenith.Core.Monitoring;

/// <summary>
/// Único bucle de muestreo de toda la aplicación. Las páginas se suscriben a
/// <see cref="SnapshotAvailable"/>; nadie más consulta hardware directamente,
/// de modo que abrir cinco pantallas no multiplica por cinco el coste.
/// </summary>
public sealed class MonitoringService : IAsyncDisposable
{
    private const int HistoryLength = 90;

    private readonly ICpuProvider _cpu;
    private readonly IMemoryProvider _memory;
    private readonly IGpuProvider _gpu;
    private readonly IThermalProvider _thermal;
    private readonly ISettingsStore _settings;
    private readonly ILogger<MonitoringService> _logger;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private volatile bool _foreground = true;
    private int _consecutiveFailures;

    public MonitoringService(
        ICpuProvider cpu,
        IMemoryProvider memory,
        IGpuProvider gpu,
        IThermalProvider thermal,
        ISettingsStore settings,
        ILogger<MonitoringService> logger)
    {
        _cpu = cpu;
        _memory = memory;
        _gpu = gpu;
        _thermal = thermal;
        _settings = settings;
        _logger = logger;
    }

    public MetricHistory CpuHistory { get; } = new(HistoryLength);

    public MetricHistory MemoryHistory { get; } = new(HistoryLength);

    public MetricHistory GpuHistory { get; } = new(HistoryLength);

    public SystemSnapshot Latest { get; private set; } = SystemSnapshot.Empty;

    public CpuStaticInfo CpuInfo { get; private set; } = CpuStaticInfo.Unknown;

    public IReadOnlyList<GpuInfo> GpuAdapters { get; private set; } = [];

    public event EventHandler<SystemSnapshot>? SnapshotAvailable;

    /// <summary>Cuando la ventana pierde el foco bajamos la cadencia. Requisito de consumo en reposo.</summary>
    public void SetForeground(bool isForeground) => _foreground = isForeground;

    public async Task StartAsync(CancellationToken ct = default)
    {
        CpuInfo = await SafeAsync(() => _cpu.GetStaticInfoAsync(ct), CpuStaticInfo.Unknown, "info de CPU").ConfigureAwait(false);
        GpuAdapters = await SafeAsync(() => _gpu.GetAdaptersAsync(ct), [], "adaptadores gráficos").ConfigureAwait(false);

        if (_settings.Current.EnableHardwareSensors)
        {
            var failure = await _thermal.TryEnableAsync(ct).ConfigureAwait(false);
            if (failure != ThermalUnavailableReason.None)
            {
                _logger.LogInformation("Sensores de hardware no disponibles: {Reason}", failure);
            }
        }

        lock (_gate)
        {
            if (_loop is not null) return;
            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Primer tick inmediato: la UI no debe esperar un segundo a tener datos.
        Tick();

        while (!ct.IsCancellationRequested)
        {
            var settings = _settings.Current;
            var interval = _foreground
                ? Math.Max(500, settings.MonitorIntervalMs)
                : Math.Max(settings.MonitorIntervalMs, settings.BackgroundMonitorIntervalMs);

            try
            {
                await Task.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Tick();
        }
    }

    private void Tick()
    {
        try
        {
            var cpu = _cpu.Sample();
            var memory = _memory.Sample();
            var gpus = _gpu.Sample();
            var thermal = _thermal.IsEnabled
                ? _thermal.Sample()
                : ThermalSnapshot.Unavailable(ThermalUnavailableReason.SensorsDisabled);

            if (cpu.TotalUsagePercent.HasValue) CpuHistory.Add(cpu.TotalUsagePercent.Value);
            if (memory.TotalBytes > 0) MemoryHistory.Add(memory.UsagePercent);

            var primaryGpu = gpus.FirstOrDefault();
            if (primaryGpu?.UtilizationPercent.HasValue == true) GpuHistory.Add(primaryGpu.UtilizationPercent.Value);

            var snapshot = new SystemSnapshot(DateTimeOffset.Now, cpu, memory, gpus, thermal);
            Latest = snapshot;
            _consecutiveFailures = 0;

            SnapshotAvailable?.Invoke(this, snapshot);
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            // Un fallo puntual no debe llenar el log ni parar el bucle.
            if (_consecutiveFailures is 1 or 10 or 100)
            {
                _logger.LogWarning(ex, "Fallo al muestrear el sistema (intento {Count})", _consecutiveFailures);
            }
        }
    }

    private async Task<T> SafeAsync<T>(Func<Task<T>> action, T fallback, string what)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return fallback;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo obtener {What}", what);
            return fallback;
        }
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
        }

        if (cts is not null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
        }

        if (loop is not null)
        {
            try
            {
                await loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cierre normal.
            }
        }

        cts?.Dispose();
        await _thermal.DisposeAsync().ConfigureAwait(false);
    }
}
