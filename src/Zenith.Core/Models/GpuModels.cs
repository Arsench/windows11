using Zenith.Core.Primitives;

namespace Zenith.Core.Models;

public sealed record GpuInfo(
    string AdapterId,
    string Name,
    Metric<long> DedicatedMemoryBytes,
    string? DriverVersion);

public sealed record GpuSample(
    string AdapterId,
    Metric<double> UtilizationPercent,
    Metric<long> DedicatedMemoryUsedBytes,
    Metric<double> CoreClockMhz);
