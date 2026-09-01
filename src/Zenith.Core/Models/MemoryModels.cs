using Zenith.Core.Primitives;

namespace Zenith.Core.Models;

public sealed record MemorySample(
    long TotalBytes,
    long UsedBytes,
    long AvailableBytes,
    Metric<long> CommittedBytes)
{
    public double UsagePercent => TotalBytes > 0 ? UsedBytes * 100d / TotalBytes : 0d;

    public static MemorySample Empty { get; } = new(0, 0, 0, Metric<long>.Pending());
}

/// <summary>Un módulo físico de RAM. Todo opcional: muchos equipos no lo reportan.</summary>
public sealed record MemoryModuleInfo(
    string? BankLabel,
    long CapacityBytes,
    Metric<int> SpeedMhz,
    string? Manufacturer,
    string? PartNumber);
