namespace Zenith.Core.Models;

/// <summary>
/// Fotografía completa de un tick de monitorización. Es inmutable: la UI puede
/// leerla desde cualquier hilo sin sincronización.
/// </summary>
public sealed record SystemSnapshot(
    DateTimeOffset Timestamp,
    CpuSample Cpu,
    MemorySample Memory,
    IReadOnlyList<GpuSample> Gpus,
    ThermalSnapshot Thermal)
{
    public static SystemSnapshot Empty { get; } = new(
        DateTimeOffset.MinValue,
        CpuSample.Empty,
        MemorySample.Empty,
        Array.Empty<GpuSample>(),
        ThermalSnapshot.Unavailable("Midiendo…"));
}
