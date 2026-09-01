using Zenith.Core.Abstractions;
using Zenith.Core.Settings;

namespace Zenith.Core.Tests;

/// <summary>Almacén de configuración en memoria, para probar sin tocar el disco.</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? Changed;

    public Task LoadAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SaveAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
    {
        var draft = Current.Clone();
        mutate(draft);
        Current = draft;
        Changed?.Invoke(this, draft);
        return Task.CompletedTask;
    }
}
