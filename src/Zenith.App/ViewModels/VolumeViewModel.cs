using CommunityToolkit.Mvvm.ComponentModel;
using Zenith.App.Localization;
using Zenith.Core.Models;
using Zenith.Core.Primitives;

namespace Zenith.App.ViewModels;

/// <summary>Presentación de una unidad. Compartida por el panel y la sección de almacenamiento.</summary>
public sealed partial class VolumeViewModel(StorageVolume volume) : ObservableObject
{
    private static Loc L => Loc.Instance;

    public StorageVolume Volume { get; } = volume;

    public string RootPath => Volume.RootPath;

    public string DriveLetter => Volume.DriveLetter;

    public string Label => string.IsNullOrWhiteSpace(Volume.Label) ? L["CommonUnnamed"] : Volume.Label;

    public string UsagePairText => ByteSize.FormatPair(Volume.UsedBytes, Volume.TotalBytes);

    public string FreeText => L.Format("StorageFreeSuffix", ByteSize.Format(Volume.FreeBytes));

    public string UsedText => L.Format("StorageUsedSuffix", ByteSize.Format(Volume.UsedBytes));

    public string TotalText => ByteSize.Format(Volume.TotalBytes);

    public double UsagePercent => Volume.UsagePercent;

    public string UsagePercentText => MetricFormatter.Number(Volume.UsagePercent, 0) + " %";

    public string DetailsText => $"{Present.Media(Volume.Media)} · {Volume.FileSystem}";

    public string? PhysicalDiskModel => Volume.PhysicalDiskModel;

    /// <summary>Por encima del 90 % conviene avisar; la unidad del sistema puede dar problemas.</summary>
    public bool IsNearlyFull => Volume.UsagePercent >= 90;

    public string AccessibleName =>
        $"{DriveLetter} · {UsagePercentText} · {FreeText}";
}
