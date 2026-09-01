using Zenith.Core.Primitives;

namespace Zenith.Core.Models;

/// <summary>Datos de la CPU que no cambian mientras la app está abierta.</summary>
public sealed record CpuStaticInfo(
    string Name,
    Metric<int> PhysicalCores,
    Metric<int> LogicalProcessors,
    Metric<double> BaseClockGhz)
{
    /// <summary>Nombre vacío: el texto de relleno lo pone la capa de interfaz, traducido.</summary>
    public static CpuStaticInfo Unknown { get; } = new(
        string.Empty,
        Metric<int>.Failed(),
        Metric<int>.Failed(),
        Metric<double>.Failed());
}

/// <summary>Una muestra instantánea de uso de CPU.</summary>
public sealed record CpuSample(
    Metric<double> TotalUsagePercent,
    Metric<double> CurrentClockGhz,
    IReadOnlyList<double> PerCoreUsagePercent)
{
    public static CpuSample Empty { get; } = new(
        Metric<double>.Pending(),
        Metric<double>.Pending(),
        Array.Empty<double>());
}
