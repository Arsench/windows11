using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using Zenith.Core.Abstractions;
using Zenith.Core.Duplicates;
using Zenith.Core.Monitoring;
using Zenith.Core.Safety;
using Zenith.Core.Storage;
using Zenith.Platform.Windows.Cpu;
using Zenith.Platform.Windows.Files;
using Zenith.Platform.Windows.Gpu;
using Zenith.Platform.Windows.Memory;
using Zenith.Platform.Windows.Processes;
using Zenith.Platform.Windows.Settings;
using Zenith.Platform.Windows.Storage;
using Zenith.Platform.Windows.Thermal;

namespace Zenith.Platform.Windows;

public static class WindowsPlatformExtensions
{
    /// <summary>
    /// Registra la implementación Windows de todos los contratos de Zenith.Core
    /// más los servicios de dominio. Único punto donde se decide qué se usa.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddZenithWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();

        services.AddSingleton<ICpuProvider, WindowsCpuProvider>();
        services.AddSingleton<IMemoryProvider, WindowsMemoryProvider>();
        services.AddSingleton<IGpuProvider, WindowsGpuProvider>();
        services.AddSingleton<IThermalProvider, HardwareThermalProvider>();
        services.AddSingleton<IStorageProvider, WindowsStorageProvider>();
        services.AddSingleton<IProcessProvider, WindowsProcessProvider>();

        services.AddSingleton<IFileIdentityResolver, WindowsFileIdentityResolver>();
        services.AddSingleton<IShellService, WindowsShellService>();
        services.AddSingleton<IFileSystemOperations, WindowsFileSystemOperations>();

        services.AddSingleton(_ => PathSafetyGuard.CreateForWindows());
        services.AddSingleton<DuplicateActionPlanner>();
        services.AddSingleton<DuplicateScanner>();
        services.AddSingleton<StorageAnalyzer>();
        services.AddSingleton<MonitoringService>();

        return services;
    }
}
