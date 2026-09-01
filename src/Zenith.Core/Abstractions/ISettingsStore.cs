using Zenith.Core.Settings;

namespace Zenith.Core.Abstractions;

public interface ISettingsStore
{
    AppSettings Current { get; }

    Task LoadAsync(CancellationToken ct = default);

    Task SaveAsync(CancellationToken ct = default);

    event EventHandler<AppSettings>? Changed;

    /// <summary>Aplica una mutación y persiste. Único punto de escritura.</summary>
    Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default);
}

/// <summary>
/// Resuelve la identidad física de un archivo para no confundir un vínculo duro
/// (hardlink) con un duplicado real. Opcional: si no hay implementación, el
/// escáner solo deduplica por ruta.
/// </summary>
public interface IFileIdentityResolver
{
    /// <summary>Clave estable del archivo físico, o null si no se puede determinar.</summary>
    string? TryGetPhysicalId(string path);
}
