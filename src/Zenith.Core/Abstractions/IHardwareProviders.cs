using Zenith.Core.Models;

namespace Zenith.Core.Abstractions;

/// <summary>
/// Contratos de acceso al hardware. Zenith.Core no sabe nada de Windows;
/// Zenith.Platform.Windows los implementa.
/// </summary>
public interface ICpuProvider
{
    /// <summary>Datos estáticos. Puede tardar (WMI): llamar una sola vez al arrancar.</summary>
    Task<CpuStaticInfo> GetStaticInfoAsync(CancellationToken ct = default);

    /// <summary>Muestra barata. Debe poder llamarse cada segundo sin coste apreciable.</summary>
    CpuSample Sample();
}

public interface IMemoryProvider
{
    MemorySample Sample();

    Task<IReadOnlyList<MemoryModuleInfo>> GetModulesAsync(CancellationToken ct = default);
}

public interface IGpuProvider
{
    Task<IReadOnlyList<GpuInfo>> GetAdaptersAsync(CancellationToken ct = default);

    IReadOnlyList<GpuSample> Sample();
}

/// <summary>
/// Sensores térmicos. Implementación opt-in: leer temperaturas reales exige un
/// driver en kernel, así que el usuario debe activarlo explícitamente.
/// </summary>
public interface IThermalProvider : IAsyncDisposable
{
    bool IsEnabled { get; }

    /// <summary>Intenta activar los sensores. Devuelve el motivo si no puede.</summary>
    Task<string?> TryEnableAsync(CancellationToken ct = default);

    void Disable();

    ThermalSnapshot Sample();
}

public interface IStorageProvider
{
    Task<IReadOnlyList<StorageVolume>> GetVolumesAsync(CancellationToken ct = default);
}

public interface IProcessProvider
{
    /// <summary>Top N por CPU. El primer muestreo devuelve CPU "Pending" (hace falta un delta).</summary>
    Task<IReadOnlyList<ProcessSample>> GetTopProcessesAsync(int count, CancellationToken ct = default);
}
