namespace Zenith.Core.Primitives;

/// <summary>
/// Por qué un valor puede no estar presente. Existe para que la UI nunca tenga
/// que decidir si un 0 es "cero de verdad" o "no lo sabemos".
/// </summary>
public enum MetricStatus
{
    /// <summary>El valor es real y se puede mostrar.</summary>
    Available,

    /// <summary>El hardware o Windows no expone este dato en este equipo.</summary>
    NotSupported,

    /// <summary>Existe, pero requiere permisos que la app no tiene ahora mismo.</summary>
    RequiresElevation,

    /// <summary>La lectura falló (excepción, sensor caído, timeout).</summary>
    Failed,

    /// <summary>Todavía no se ha tomado la primera muestra.</summary>
    Pending
}

/// <summary>
/// Un valor numérico que puede no existir, con el motivo. Sustituye a
/// "devolver 0" y a "devolver null sin explicación".
/// </summary>
public readonly record struct Metric<T> where T : struct
{
    private readonly T _value;

    private Metric(T value, MetricStatus status, string? reason)
    {
        _value = value;
        Status = status;
        Reason = reason;
    }

    public MetricStatus Status { get; }

    /// <summary>Texto corto y legible para el usuario cuando no hay dato.</summary>
    public string? Reason { get; }

    public bool HasValue => Status == MetricStatus.Available;

    /// <summary>Válido únicamente si <see cref="HasValue"/> es verdadero.</summary>
    public T Value => HasValue
        ? _value
        : throw new InvalidOperationException($"La métrica no tiene valor ({Status}: {Reason ?? "sin motivo"}).");

    public T? ValueOrNull => HasValue ? _value : null;

    public T ValueOr(T fallback) => HasValue ? _value : fallback;

    public static Metric<T> Available(T value) => new(value, MetricStatus.Available, null);

    public static Metric<T> NotSupported(string reason = "No disponible en este equipo")
        => new(default, MetricStatus.NotSupported, reason);

    public static Metric<T> RequiresElevation(string reason = "Requiere permisos de administrador")
        => new(default, MetricStatus.RequiresElevation, reason);

    public static Metric<T> Failed(string reason = "No se pudo leer")
        => new(default, MetricStatus.Failed, reason);

    public static Metric<T> Pending() => new(default, MetricStatus.Pending, "Midiendo…");

    public Metric<TOut> Map<TOut>(Func<T, TOut> selector) where TOut : struct
        => HasValue ? Metric<TOut>.Available(selector(_value)) : new Metric<TOut>(default, Status, Reason);
}

public static class Metric
{
    public static Metric<T> From<T>(T? value, string reasonIfMissing = "No disponible") where T : struct
        => value.HasValue ? Metric<T>.Available(value.Value) : Metric<T>.NotSupported(reasonIfMissing);
}
