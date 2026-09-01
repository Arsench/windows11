using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;
using Zenith.Core.Primitives;
using Zenith.Platform.Windows.Interop;

namespace Zenith.Platform.Windows.Cpu;

/// <summary>
/// Uso de CPU a partir de los tiempos del kernel (<c>NtQuerySystemInformation</c>).
/// Es lo mismo que usa el Administrador de tareas por debajo: exacto, muy barato
/// y — a diferencia de los contadores de rendimiento — independiente del idioma
/// de Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCpuProvider : ICpuProvider, IDisposable
{
    private readonly ILogger<WindowsCpuProvider> _logger;
    private readonly int _logicalProcessors;
    private readonly int _structSize;
    private readonly IntPtr _buffer;

    private long[] _previousIdle;
    private long[] _previousTotal;
    private bool _hasPrevious;
    private bool _disposed;

    private PdhQuery? _pdh;
    private IntPtr? _performanceCounter;
    private bool _pdhPrimed;
    private double _baseClockGhz;

    public WindowsCpuProvider(ILogger<WindowsCpuProvider> logger)
    {
        _logger = logger;
        _logicalProcessors = Environment.ProcessorCount;
        _structSize = Marshal.SizeOf<NativeMethods.SystemProcessorPerformance>();
        _buffer = Marshal.AllocHGlobal(_structSize * _logicalProcessors);
        _previousIdle = new long[_logicalProcessors];
        _previousTotal = new long[_logicalProcessors];

        _pdh = PdhQuery.TryCreate();
        // Porcentaje sobre la frecuencia base; puede pasar de 100 con turbo.
        _performanceCounter = _pdh?.TryAddCounter(@"\Processor Information(_Total)\% Processor Performance");
        if (_performanceCounter is null)
        {
            _pdh?.Dispose();
            _pdh = null;
        }
    }

    public Task<CpuStaticInfo> GetStaticInfoAsync(CancellationToken ct = default) =>
        Task.Run(GetStaticInfo, ct);

    private CpuStaticInfo GetStaticInfo()
    {
        var name = "Procesador desconocido";
        var physicalCores = Metric<int>.NotSupported("El equipo no informa del número de núcleos");
        var baseClock = Metric<double>.NotSupported("La frecuencia base no está disponible");

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, MaxClockSpeed FROM Win32_Processor");

            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    name = item["Name"]?.ToString()?.Trim() ?? name;

                    if (item["NumberOfCores"] is uint cores && cores > 0)
                        physicalCores = Metric<int>.Available((int)cores);

                    if (item["MaxClockSpeed"] is uint mhz && mhz > 0)
                    {
                        _baseClockGhz = mhz / 1000d;
                        baseClock = Metric<double>.Available(_baseClockGhz);
                    }
                }

                break; // Un solo socket: es lo normal en equipos de escritorio.
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI no ha devuelto la información del procesador");
        }

        if (_baseClockGhz <= 0 && TryGetMaxMhzFromPowerApi() is { } maxMhz)
        {
            _baseClockGhz = maxMhz / 1000d;
            baseClock = Metric<double>.Available(_baseClockGhz);
        }

        return new CpuStaticInfo(
            name,
            physicalCores,
            Metric<int>.Available(_logicalProcessors),
            baseClock);
    }

    public CpuSample Sample()
    {
        if (_disposed) return CpuSample.Empty;

        var perCore = ReadPerCoreUsage(out var totalUsage);
        return new CpuSample(totalUsage, ReadCurrentClock(), perCore);
    }

    private IReadOnlyList<double> ReadPerCoreUsage(out Metric<double> totalUsage)
    {
        totalUsage = Metric<double>.Failed("No se ha podido leer el uso de CPU");

        var status = NativeMethods.NtQuerySystemInformation(
            NativeMethods.SystemProcessorPerformanceInformation,
            _buffer,
            _structSize * _logicalProcessors,
            out _);

        if (status != 0) return [];

        var perCore = new double[_logicalProcessors];
        var idle = new long[_logicalProcessors];
        var total = new long[_logicalProcessors];

        long totalDelta = 0, idleDelta = 0;

        for (var i = 0; i < _logicalProcessors; i++)
        {
            var entry = Marshal.PtrToStructure<NativeMethods.SystemProcessorPerformance>(_buffer + i * _structSize);

            // KernelTime ya incluye IdleTime, así que el total es kernel + user.
            idle[i] = entry.IdleTime;
            total[i] = entry.KernelTime + entry.UserTime;

            if (!_hasPrevious) continue;

            var dt = total[i] - _previousTotal[i];
            var di = idle[i] - _previousIdle[i];
            totalDelta += dt;
            idleDelta += di;

            perCore[i] = dt > 0 ? Math.Clamp((dt - di) * 100d / dt, 0, 100) : 0;
        }

        var hadPrevious = _hasPrevious;
        _previousIdle = idle;
        _previousTotal = total;
        _hasPrevious = true;

        if (!hadPrevious)
        {
            totalUsage = Metric<double>.Pending();
            return [];
        }

        totalUsage = totalDelta > 0
            ? Metric<double>.Available(Math.Clamp((totalDelta - idleDelta) * 100d / totalDelta, 0, 100))
            : Metric<double>.Available(0);

        return perCore;
    }

    private Metric<double> ReadCurrentClock()
    {
        if (_baseClockGhz <= 0)
            return Metric<double>.NotSupported("Sin frecuencia base no se puede calcular la actual");

        if (_pdh is not null && _performanceCounter is { } counter)
        {
            if (_pdh.Collect())
            {
                if (!_pdhPrimed)
                {
                    // Los contadores de tasa necesitan dos muestras para dar valor.
                    _pdhPrimed = true;
                }
                else if (_pdh.TryReadSingle(counter) is { } performancePercent)
                {
                    return Metric<double>.Available(_baseClockGhz * performancePercent / 100d);
                }
            }
        }

        if (TryGetCurrentMhzFromPowerApi() is { } mhz) return Metric<double>.Available(mhz / 1000d);

        return Metric<double>.Pending();
    }

    private uint? TryGetMaxMhzFromPowerApi() => ReadPowerInformation(static p => p.MaxMhz);

    private uint? TryGetCurrentMhzFromPowerApi() => ReadPowerInformation(static p => p.CurrentMhz);

    /// <summary>
    /// Alternativa sin PDH. Menos precisa (muchos equipos devuelven la nominal),
    /// pero mejor que no mostrar nada.
    /// </summary>
    private uint? ReadPowerInformation(Func<NativeMethods.ProcessorPowerInformation, uint> selector)
    {
        var size = Marshal.SizeOf<NativeMethods.ProcessorPowerInformation>();
        var buffer = Marshal.AllocHGlobal(size * _logicalProcessors);
        try
        {
            var status = NativeMethods.CallNtPowerInformation(
                NativeMethods.ProcessorInformation,
                IntPtr.Zero, 0,
                buffer, (uint)(size * _logicalProcessors));

            if (status != 0) return null;

            double sum = 0;
            for (var i = 0; i < _logicalProcessors; i++)
            {
                sum += selector(Marshal.PtrToStructure<NativeMethods.ProcessorPowerInformation>(buffer + i * size));
            }

            var average = sum / _logicalProcessors;
            return average > 0 ? (uint)average : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CallNtPowerInformation ha fallado");
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _pdh?.Dispose();
        _pdh = null;
        _performanceCounter = null;

        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
    }
}
