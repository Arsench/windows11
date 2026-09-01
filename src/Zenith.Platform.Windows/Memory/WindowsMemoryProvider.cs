using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;
using Zenith.Core.Primitives;
using Zenith.Platform.Windows.Interop;

namespace Zenith.Platform.Windows.Memory;

[SupportedOSPlatform("windows")]
public sealed class WindowsMemoryProvider(ILogger<WindowsMemoryProvider> logger) : IMemoryProvider
{
    public MemorySample Sample()
    {
        var status = new NativeMethods.MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<NativeMethods.MemoryStatusEx>()
        };

        if (!NativeMethods.GlobalMemoryStatusEx(ref status)) return MemorySample.Empty;

        var total = (long)status.TotalPhys;
        var available = (long)status.AvailPhys;

        return new MemorySample(total, Math.Max(0, total - available), available, ReadCommitted());
    }

    private Metric<long> ReadCommitted()
    {
        try
        {
            var info = new NativeMethods.PerformanceInformation
            {
                cb = (uint)Marshal.SizeOf<NativeMethods.PerformanceInformation>()
            };

            if (!NativeMethods.GetPerformanceInfo(ref info, info.cb))
                return Metric<long>.Failed();

            var pageSize = (long)info.PageSize;
            return Metric<long>.Available((long)info.CommitTotal * pageSize);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "GetPerformanceInfo ha fallado");
            return Metric<long>.Failed();
        }
    }

    public Task<IReadOnlyList<MemoryModuleInfo>> GetModulesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<MemoryModuleInfo>>(() =>
        {
            var modules = new List<MemoryModuleInfo>();

            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT BankLabel, Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");

                foreach (var item in searcher.Get().Cast<ManagementObject>())
                {
                    using (item)
                    {
                        var capacity = item["Capacity"] is not null && ulong.TryParse(item["Capacity"].ToString(), out var c)
                            ? (long)c
                            : 0L;

                        var speed = item["Speed"] is uint mhz && mhz > 0
                            ? Metric<int>.Available((int)mhz)
                            : Metric<int>.NotSupported();

                        modules.Add(new MemoryModuleInfo(
                            item["BankLabel"]?.ToString()?.Trim(),
                            capacity,
                            speed,
                            item["Manufacturer"]?.ToString()?.Trim(),
                            item["PartNumber"]?.ToString()?.Trim()));
                    }
                }
            }
            catch (Exception ex)
            {
                // Sin permisos o WMI degradado: la UI simplemente no muestra la sección.
                logger.LogInformation(ex, "No se han podido enumerar los módulos de memoria");
            }

            return modules;
        }, ct);
}
