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

/// <summary>
/// Una lectura de temperatura concreta. Nunca se sintetiza: si no hay sensor,
/// no hay <see cref="ThermalReading"/>.
/// </summary>
public sealed record ThermalReading(
    ThermalComponent Component,
    string SensorName,
    double Celsius,
    ThermalSource Source);

/// <summary>Resultado de una pasada del subsistema térmico, incluido el "no hay nada".</summary>
public sealed record ThermalSnapshot(
    IReadOnlyList<ThermalReading> Readings,
    string? UnavailableReason)
{
    public static ThermalSnapshot Unavailable(string reason) => new(Array.Empty<ThermalReading>(), reason);

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
