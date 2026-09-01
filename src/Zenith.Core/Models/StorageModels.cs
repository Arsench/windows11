using Zenith.Core.Primitives;

namespace Zenith.Core.Models;

public enum DriveMedia
{
    Unknown,
    HardDisk,
    SolidState,
    Nvme,
    Removable,
    Network,
    Optical
}

/// <summary>Un volumen montado (C:, D:, …) con sus datos de capacidad.</summary>
public sealed record StorageVolume(
    string RootPath,
    string Label,
    string FileSystem,
    long TotalBytes,
    long FreeBytes,
    DriveMedia Media,
    string? PhysicalDiskModel)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsagePercent => TotalBytes > 0 ? UsedBytes * 100d / TotalBytes : 0d;

    /// <summary>"C:" — para titulares.</summary>
    public string DriveLetter => RootPath.Length >= 2 ? RootPath[..2] : RootPath;
}

public sealed record ProcessSample(
    int ProcessId,
    string Name,
    Metric<double> CpuPercent,
    long WorkingSetBytes);
