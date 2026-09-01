namespace Zenith.Core.Models;

public enum ThermalSource
{
    /// <summary>Sensor real del hardware (SuperIO / MSR / SMART). Requiere driver.</summary>
    Hardware,

    /// <summary>Zona térmica ACPI. NO es la temperatura del die de la CPU.</summary>
    AcpiThermalZone
}

public enum ThermalComponent
{
    Cpu,
    Gpu,
    Storage,
    Motherboard,
    Other
}

/// <summary>Motivo por el que no hay lecturas. Código, no texto.</summary>
public enum ThermalUnavailableReason
{
    None,

    /// <summary>El usuario no ha activado los sensores.</summary>
    SensorsDisabled,

    /// <summary>Aún no se ha tomado la primera muestra.</summary>
    Measuring,

    /// <summary>Hace falta ejecutar la aplicación como administrador.</summary>
    RequiresElevation,

    /// <summary>El hardware no expone sensores compatibles.</summary>
    NoCompatibleSensors,

    /// <summary>El equipo no publica zonas térmicas ACPI.</summary>
    NoAcpiZones,

    /// <summary>La lectura falló.</summary>
    ReadFailed,

    /// <summary>La capa de sensores no llegó a inicializarse.</summary>
    NotInitialised
}

/// <summary>
/// Una lectura de temperatura concreta. Nunca se sintetiza: si no hay sensor,
/// no hay <see cref="ThermalReading"/>.
/// </summary>
/// <param name="SensorName">
/// Nombre que da el propio hardware. Null en las zonas térmicas ACPI, que se
/// numeran con <paramref name="Index"/>.
/// </param>
public sealed record ThermalReading(
    ThermalComponent Component,
    string? SensorName,
    int Index,
    double Celsius,
    ThermalSource Source);

/// <summary>Resultado de una pasada del subsistema térmico, incluido el "no hay nada".</summary>
public sealed record ThermalSnapshot(
    IReadOnlyList<ThermalReading> Readings,
    ThermalUnavailableReason UnavailableReason)
{
    public static ThermalSnapshot Unavailable(ThermalUnavailableReason reason) =>
        new(Array.Empty<ThermalReading>(), reason);

    public double? HottestFor(ThermalComponent component)
    {
        double? max = null;
        foreach (var r in Readings)
        {
            if (r.Component != component) continue;
            if (max is null || r.Celsius > max) max = r.Celsius;
        }
        return max;
    }
}
