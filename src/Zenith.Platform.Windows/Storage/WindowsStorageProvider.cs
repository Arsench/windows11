using System.Management;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;

namespace Zenith.Platform.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsStorageProvider(ILogger<WindowsStorageProvider> logger) : IStorageProvider
{
    private sealed record PhysicalDiskInfo(string Model, DriveMedia Media);

    public Task<IReadOnlyList<StorageVolume>> GetVolumesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<StorageVolume>>(() =>
        {
            var physical = ReadPhysicalDisks();
            var volumes = new List<StorageVolume>();

            foreach (var drive in SafeGetDrives())
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    // IsReady evita bloqueos con lectores de tarjetas vacíos o
                    // unidades de red desconectadas.
                    if (!drive.IsReady) continue;

                    var letter = drive.Name.Length >= 2 ? drive.Name[..2].ToUpperInvariant() : drive.Name;
                    physical.TryGetValue(letter, out var disk);

                    volumes.Add(new StorageVolume(
                        drive.Name,
                        drive.VolumeLabel ?? string.Empty,
                        drive.DriveFormat,
                        drive.TotalSize,
                        drive.AvailableFreeSpace,
                        disk?.Media ?? FromDriveType(drive.DriveType),
                        disk?.Model));
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "No se ha podido leer la unidad {Drive}", drive.Name);
                }
            }

            return volumes
                .OrderBy(v => v.RootPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, ct);

    private DriveInfo[] SafeGetDrives()
    {
        try
        {
            return DriveInfo.GetDrives();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se han podido enumerar las unidades");
            return [];
        }
    }

    private static DriveMedia FromDriveType(DriveType type) => type switch
    {
        DriveType.Removable => DriveMedia.Removable,
        DriveType.Network => DriveMedia.Network,
        DriveType.CDRom => DriveMedia.Optical,
        _ => DriveMedia.Unknown
    };

    /// <summary>
    /// Relaciona letra de unidad con disco físico para saber si es HDD, SSD o
    /// NVMe. El espacio de nombres Storage puede requerir privilegios: si falla,
    /// simplemente no mostramos el tipo, en vez de adivinarlo.
    /// </summary>
    private Dictionary<string, PhysicalDiskInfo> ReadPhysicalDisks()
    {
        var result = new Dictionary<string, PhysicalDiskInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();

            var disks = new Dictionary<uint, PhysicalDiskInfo>();

            using (var diskSearcher = new ManagementObjectSearcher(
                       scope, new ObjectQuery("SELECT DeviceId, FriendlyName, MediaType, BusType FROM MSFT_PhysicalDisk")))
            {
                foreach (var item in diskSearcher.Get().Cast<ManagementObject>())
                {
                    using (item)
                    {
                        if (!uint.TryParse(item["DeviceId"]?.ToString(), out var deviceId)) continue;

                        var mediaType = Convert.ToUInt16(item["MediaType"] ?? (ushort)0);
                        var busType = Convert.ToUInt16(item["BusType"] ?? (ushort)0);

                        var media = busType == 17
                            ? DriveMedia.Nvme
                            : mediaType switch
                            {
                                3 => DriveMedia.HardDisk,
                                4 => DriveMedia.SolidState,
                                _ => busType == 7 ? DriveMedia.Removable : DriveMedia.Unknown
                            };

                        disks[deviceId] = new PhysicalDiskInfo(
                            item["FriendlyName"]?.ToString()?.Trim() ?? string.Empty, media);
                    }
                }
            }

            using var partitionSearcher = new ManagementObjectSearcher(
                scope, new ObjectQuery("SELECT DiskNumber, DriveLetter FROM MSFT_Partition"));

            foreach (var item in partitionSearcher.Get().Cast<ManagementObject>())
            {
                using (item)
                {
                    var letter = item["DriveLetter"]?.ToString();
                    if (string.IsNullOrWhiteSpace(letter) || letter[0] == '\0') continue;
                    if (!uint.TryParse(item["DiskNumber"]?.ToString(), out var diskNumber)) continue;
                    if (!disks.TryGetValue(diskNumber, out var disk)) continue;

                    result[$"{char.ToUpperInvariant(letter[0])}:"] = disk;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "No se ha podido determinar el tipo físico de los discos");
        }

        return result;
    }
}
