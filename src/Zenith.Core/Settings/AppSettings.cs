namespace Zenith.Core.Settings;

public enum ThemePreference
{
    System,
    Light,
    Dark
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
    public ThemePreference Theme { get; set; } = ThemePreference.System;

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

    /// <summary>Tamaño mínimo (bytes) para considerar un archivo en el escáner de duplicados.</summary>
    public long DuplicateMinFileSizeBytes { get; set; } = 1024;

    /// <summary>Verificación byte a byte al final del escaneo. Más lenta, exacta.</summary>
    public bool VerifyDuplicatesByteByByte { get; set; } = true;

    public AppSettings Clone() => new()
    {
        Theme = Theme,
        MonitorIntervalMs = MonitorIntervalMs,
        BackgroundMonitorIntervalMs = BackgroundMonitorIntervalMs,
        EnableHardwareSensors = EnableHardwareSensors,
        DeletionBehavior = DeletionBehavior,
        DefaultMoveFolder = DefaultMoveFolder,
        ExcludedPaths = [.. ExcludedPaths],
        DuplicateMinFileSizeBytes = DuplicateMinFileSizeBytes,
        VerifyDuplicatesByteByByte = VerifyDuplicatesByteByByte
    };
}
