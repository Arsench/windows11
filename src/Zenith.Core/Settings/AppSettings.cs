namespace Zenith.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>
/// Idioma de la interfaz. <see cref="System"/> sigue al de Windows y recae en
/// inglés si el del sistema no está traducido.
/// </summary>
public enum AppLanguage
{
    System,
    Spanish,
    English
}

/// <summary>
/// Espejo de <c>FileCategory</c> pensado para persistirse. Se guarda por nombre,
/// así que reordenar el enum del dominio no corrompe la configuración guardada.
/// </summary>
public enum FileCategoryFilter
{
    Images,
    Video,
    Audio,
    Documents,
    Archives,
    Code,
    DiskImages,
    Applications
}

public enum DeletionBehavior
{
    /// <summary>Enviar a la papelera de reciclaje. Recomendado y valor por defecto.</summary>
    RecycleBin,

    /// <summary>Borrado irreversible. Requiere confirmación reforzada en la UI.</summary>
    Permanent
}

/// <summary>
/// Configuración persistida. Clase mutable a propósito: se edita solo a través
/// de <c>ISettingsStore.UpdateAsync</c>.
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// Versión del formato. Sirve para corregir valores que se guardaron con un
    /// valor por defecto equivocado, sin pisar lo que el usuario haya elegido a mano.
    /// </summary>
    public int SettingsVersion { get; set; }

    public ThemePreference Theme { get; set; } = ThemePreference.System;

    public AppLanguage Language { get; set; } = AppLanguage.System;

    /// <summary>Clave de licencia introducida por el usuario. Vacío = uso personal.</summary>
    public string? LicenseKey { get; set; }

    /// <summary>Intervalo de muestreo en milisegundos. Mínimo razonable: 500 ms.</summary>
    public int MonitorIntervalMs { get; set; } = 1000;

    /// <summary>Intervalo cuando la ventana no está activa. Ahorra CPU en reposo.</summary>
    public int BackgroundMonitorIntervalMs { get; set; } = 4000;

    /// <summary>Sensores de hardware (LibreHardwareMonitor). Desactivado por defecto: exige administrador.</summary>
    public bool EnableHardwareSensors { get; set; }

    public DeletionBehavior DeletionBehavior { get; set; } = DeletionBehavior.RecycleBin;

    /// <summary>Carpeta propuesta por defecto al mover duplicados.</summary>
    public string? DefaultMoveFolder { get; set; }

    /// <summary>Rutas que el usuario excluye de cualquier análisis.</summary>
    public List<string> ExcludedPaths { get; set; } = [];

    /// <summary>
    /// Tamaño mínimo (bytes) para considerar un archivo en el escáner de duplicados.
    /// Cero significa "todos": cualquier otro valor esconde duplicados reales.
    /// </summary>
    public long DuplicateMinFileSizeBytes { get; set; }

    /// <summary>
    /// Categorías de archivo a las que limitar la búsqueda de duplicados.
    /// Lista vacía = sin filtro, se comparan todos los tipos.
    /// </summary>
    public List<FileCategoryFilter> DuplicateCategories { get; set; } = [];

    /// <summary>Verificación byte a byte al final del escaneo. Más lenta, exacta.</summary>
    public bool VerifyDuplicatesByteByByte { get; set; } = true;

    public AppSettings Clone() => new()
    {
        Theme = Theme,
        Language = Language,
        LicenseKey = LicenseKey,
        MonitorIntervalMs = MonitorIntervalMs,
        BackgroundMonitorIntervalMs = BackgroundMonitorIntervalMs,
        EnableHardwareSensors = EnableHardwareSensors,
        DeletionBehavior = DeletionBehavior,
        DefaultMoveFolder = DefaultMoveFolder,
        ExcludedPaths = [.. ExcludedPaths],
        DuplicateMinFileSizeBytes = DuplicateMinFileSizeBytes,
        DuplicateCategories = [.. DuplicateCategories],
        SettingsVersion = SettingsVersion,
        VerifyDuplicatesByteByByte = VerifyDuplicatesByteByByte
    };
}
