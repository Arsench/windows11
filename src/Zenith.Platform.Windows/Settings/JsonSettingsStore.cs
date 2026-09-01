using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Settings;

namespace Zenith.Platform.Windows.Settings;

/// <summary>
/// Configuración en <c>%APPDATA%\Zenith\settings.json</c>. La escritura es
/// atómica (fichero temporal + reemplazo) para que un corte de luz no deje un
/// JSON a medias que impida arrancar.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    /// <summary>Versión actual del formato de configuración. Ver <see cref="Migrate"/>.</summary>
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILogger<JsonSettingsStore> _logger;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(ILogger<JsonSettingsStore> logger)
    {
        _logger = logger;
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Zenith");
        _filePath = Path.Combine(folder, "settings.json");
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_filePath))
            {
                // Instalación nueva: nace ya en la versión actual, sin migrar nada.
                Current = new AppSettings { SettingsVersion = CurrentVersion };
                return;
            }

            await using var stream = File.OpenRead(_filePath);
            var loaded = await JsonSerializer
                .DeserializeAsync<AppSettings>(stream, SerializerOptions, ct)
                .ConfigureAwait(false);

            if (loaded is not null) Current = Sanitize(Migrate(loaded));
        }
        catch (Exception ex)
        {
            // Un archivo corrupto no puede impedir abrir la aplicación.
            _logger.LogWarning(ex, "No se ha podido leer la configuración; se usan los valores por defecto");
            Current = new AppSettings { SettingsVersion = CurrentVersion };
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, Current);
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteAsync(Current, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> mutate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        AppSettings updated;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var draft = Current.Clone();
            mutate(draft);
            updated = Sanitize(draft);
            Current = updated;

            await WriteAsync(updated, ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        Changed?.Invoke(this, updated);
    }

    private async Task WriteAsync(AppSettings settings, CancellationToken ct)
    {
        try
        {
            var folder = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var temporary = _filePath + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, ct).ConfigureAwait(false);
            }

            File.Move(temporary, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se ha podido guardar la configuración");
        }
    }

    /// <summary>
    /// Corrige configuraciones guardadas con un valor por defecto equivocado.
    ///
    /// v0 → v1: el tamaño mínimo para duplicados venía en 1 KB, lo que descartaba
    /// en silencio archivos pequeños que sí eran duplicados. Solo se corrige si
    /// conserva ese valor exacto, para no pisar una elección deliberada del usuario.
    /// </summary>
    private static AppSettings Migrate(AppSettings settings)
    {
        if (settings.SettingsVersion < 1)
        {
            if (settings.DuplicateMinFileSizeBytes == 1024) settings.DuplicateMinFileSizeBytes = 0;
            settings.SettingsVersion = 1;
        }

        return settings;
    }

    /// <summary>Protege contra valores absurdos editados a mano en el JSON.</summary>
    private static AppSettings Sanitize(AppSettings settings)
    {
        settings.MonitorIntervalMs = Math.Clamp(settings.MonitorIntervalMs, 500, 10_000);
        settings.BackgroundMonitorIntervalMs = Math.Clamp(settings.BackgroundMonitorIntervalMs, 1_000, 60_000);
        settings.DuplicateMinFileSizeBytes = Math.Max(0, settings.DuplicateMinFileSizeBytes);
        settings.DuplicateCategories = [.. settings.DuplicateCategories.Distinct()];
        settings.ExcludedPaths = settings.ExcludedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return settings;
    }
}
