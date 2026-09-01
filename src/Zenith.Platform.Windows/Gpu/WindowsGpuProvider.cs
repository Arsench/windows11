using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;
using Zenith.Core.Primitives;
using Zenith.Platform.Windows.Interop;

namespace Zenith.Platform.Windows.Gpu;

/// <summary>
/// Uso de GPU independiente del fabricante: se leen los contadores WDDM que
/// Windows expone para cualquier adaptador (NVIDIA, AMD, Intel, integrada), en
/// lugar de depender de NVAPI/ADL. La identidad del adaptador viene de DXGI, que
/// da el mismo LUID que aparece en el nombre de las instancias del contador.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsGpuProvider : IGpuProvider, IDisposable
{
    private readonly ILogger<WindowsGpuProvider> _logger;

    private PdhQuery? _pdh;
    private IntPtr? _engineCounter;
    private IntPtr? _memoryCounter;
    private bool _primed;
    private bool _disposed;

    private IReadOnlyList<GpuInfo> _adapters = [];

    [GeneratedRegex(@"luid_0x[0-9A-Fa-f]+_0x[0-9A-Fa-f]+", RegexOptions.CultureInvariant)]
    private static partial Regex LuidPattern();

    [GeneratedRegex(@"eng_\d+_engtype_\w+", RegexOptions.CultureInvariant)]
    private static partial Regex EnginePattern();

    public WindowsGpuProvider(ILogger<WindowsGpuProvider> logger)
    {
        _logger = logger;

        _pdh = PdhQuery.TryCreate();
        _engineCounter = _pdh?.TryAddCounter(@"\GPU Engine(*)\Utilization Percentage");
        _memoryCounter = _pdh?.TryAddCounter(@"\GPU Adapter Memory(*)\Dedicated Usage");

        if (_engineCounter is null && _memoryCounter is null)
        {
            _pdh?.Dispose();
            _pdh = null;
        }
    }

    public Task<IReadOnlyList<GpuInfo>> GetAdaptersAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            _adapters = BuildAdapterList();
            return _adapters;
        }, ct);

    private IReadOnlyList<GpuInfo> BuildAdapterList()
    {
        var driverVersions = ReadDriverVersions();
        var adapters = new List<GpuInfo>();

        foreach (var descriptor in Dxgi.EnumerateAdapters())
        {
            driverVersions.TryGetValue(descriptor.Name, out var driver);

            adapters.Add(new GpuInfo(
                descriptor.Luid,
                descriptor.Name,
                descriptor.DedicatedVideoMemory > 0
                    ? Metric<long>.Available(descriptor.DedicatedVideoMemory)
                    : Metric<long>.NotSupported(MetricDetail.IntegratedGpuNoDedicatedMemory),
                driver));
        }

        if (adapters.Count > 0) return adapters;

        // Camino alternativo si DXGI no está disponible: al menos el nombre.
        _logger.LogInformation("DXGI no ha devuelto adaptadores; se usa WMI como alternativa");
        foreach (var (name, driver) in driverVersions)
        {
            adapters.Add(new GpuInfo(
                name,
                name,
                Metric<long>.NotSupported(MetricDetail.AdapterMemoryUnknown),
                driver));
        }

        return adapters;
    }

    private Dictionary<string, string?> ReadDriverVersions()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    var name = item["Name"]?.ToString()?.Trim();
                    if (string.IsNullOrEmpty(name)) continue;
                    result[name] = item["DriverVersion"]?.ToString()?.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se ha podido consultar Win32_VideoController");
        }

        return result;
    }

    public IReadOnlyList<GpuSample> Sample()
    {
        if (_disposed || _adapters.Count == 0) return [];

        var utilization = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var memory = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var hasData = false;

        if (_pdh is not null && _pdh.Collect())
        {
            if (!_primed)
            {
                // Primera pasada: los contadores de tasa aún no tienen delta.
                _primed = true;
            }
            else
            {
                hasData = true;
                if (_engineCounter is { } engine) ReadUtilization(engine, utilization);
                if (_memoryCounter is { } mem) ReadMemory(mem, memory);
            }
        }

        // Con un único adaptador y un único LUID visible, la correspondencia es
        // inequívoca aunque DXGI no haya podido darnos el LUID.
        string? soleLuid = null;
        if (_adapters.Count == 1 && utilization.Count == 1) soleLuid = utilization.Keys.First();

        var samples = new List<GpuSample>(_adapters.Count);
        foreach (var adapter in _adapters)
        {
            var key = utilization.ContainsKey(adapter.AdapterId) ? adapter.AdapterId : soleLuid;

            var usage = !hasData
                ? Metric<double>.Pending()
                : key is not null && utilization.TryGetValue(key, out var value)
                    ? Metric<double>.Available(Math.Clamp(value, 0, 100))
                    : Metric<double>.NotSupported(MetricDetail.AdapterNotInstrumented);

            var used = !hasData
                ? Metric<long>.Pending()
                : key is not null && memory.TryGetValue(key, out var bytes)
                    ? Metric<long>.Available((long)bytes)
                    : Metric<long>.NotSupported(MetricDetail.AdapterNotInstrumented);

            samples.Add(new GpuSample(
                adapter.AdapterId,
                usage,
                used,
                Metric<double>.NotSupported(MetricDetail.RequiresHardwareSensors)));
        }

        return samples;
    }

    /// <summary>
    /// Cada instancia es un motor de un proceso. Se suma por motor (3D, Copy,
    /// VideoDecode…) y se toma el motor más cargado, que es lo que muestra el
    /// Administrador de tareas como "uso de GPU".
    /// </summary>
    private void ReadUtilization(IntPtr counter, Dictionary<string, double> destination)
    {
        var perEngine = new Dictionary<(string Luid, string Engine), double>();

        foreach (var (instance, value) in _pdh!.ReadArray(counter))
        {
            var luid = LuidPattern().Match(instance);
            if (!luid.Success) continue;

            var engine = EnginePattern().Match(instance);
            var engineKey = engine.Success ? engine.Value : "eng_unknown";
            var key = (luid.Value, engineKey);

            perEngine[key] = perEngine.GetValueOrDefault(key) + value;
        }

        foreach (var ((luid, _), value) in perEngine)
        {
            if (!destination.TryGetValue(luid, out var current) || value > current) destination[luid] = value;
        }
    }

    private void ReadMemory(IntPtr counter, Dictionary<string, double> destination)
    {
        foreach (var (instance, value) in _pdh!.ReadArray(counter))
        {
            var luid = LuidPattern().Match(instance);
            if (!luid.Success) continue;

            destination[luid.Value] = destination.GetValueOrDefault(luid.Value) + value;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pdh?.Dispose();
        _pdh = null;
        _engineCounter = null;
        _memoryCounter = null;
    }
}
