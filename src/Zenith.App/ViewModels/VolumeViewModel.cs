using CommunityToolkit.Mvvm.ComponentModel;
using Zenith.Core.Models;
using Zenith.Core.Primitives;

namespace Zenith.App.ViewModels;

/// <summary>Presentación de una unidad. Compartida por el panel y la sección de almacenamiento.</summary>
public sealed partial class VolumeViewModel(StorageVolume volume) : ObservableObject
{
    public StorageVolume Volume { get; } = volume;

    public string RootPath => Volume.RootPath;

    public string DriveLetter => Volume.DriveLetter;

    public string Label => Volume.Label;

    public string UsagePairText => ByteSize.FormatPair(Volume.UsedBytes, Volume.TotalBytes);

    public string FreeText => $"{ByteSize.Format(Volume.FreeBytes)} libres";

    public string UsedText => $"{ByteSize.Format(Volume.UsedBytes)} usados";

    public string TotalText => ByteSize.Format(Volume.TotalBytes);

    public double UsagePercent => Volume.UsagePercent;

    public string UsagePercentText => Volume.UsagePercent.ToString("N0") + " %";

    public string DetailsText => $"{Volume.MediaDisplayName} · {Volume.FileSystem}";

    public string? PhysicalDiskModel => Volume.PhysicalDiskModel;

    /// <summary>Por encima del 90 % conviene avisar; la unidad del sistema puede dar problemas.</summary>
    public bool IsNearlyFull => Volume.UsagePercent >= 90;

    public string AccessibleName =>
        $"Unidad {DriveLetter}, {UsagePercentText} ocupado, {FreeText}";
}
