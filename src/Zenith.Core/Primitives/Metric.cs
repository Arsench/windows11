namespace Zenith.Core.Primitives;

/// <summary>Por qué un valor puede no estar presente.</summary>
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
/// Matiz concreto de por qué falta el dato, cuando el estado por sí solo no lo
/// explica. Es un código, no un texto: la traducción vive en la capa de interfaz.
/// </summary>
public enum MetricDetail
{
    None,

    /// <summary>El dispositivo simplemente no informa de este dato.</summary>
    NotReportedByDevice,

    /// <summary>Sin frecuencia base no se puede derivar la frecuencia actual.</summary>
    NoBaseClock,

    /// <summary>Gráfica integrada: no tiene memoria dedicada.</summary>
    IntegratedGpuNoDedicatedMemory,

    /// <summary>Windows no publica contadores para este adaptador.</summary>
    AdapterNotInstrumented,

    /// <summary>No se ha podido determinar la memoria del adaptador.</summary>
    AdapterMemoryUnknown,

    /// <summary>Solo disponible con los sensores de hardware activados.</summary>
    RequiresHardwareSensors
}

/// <summary>
/// Un valor numérico que puede no existir, con el motivo. Sustituye a
/// "devolver 0" y a "devolver null sin explicación".
/// </summary>
public readonly record struct Metric<T> where T : struct
{
    private readonly T _value;

    private Metric(T value, MetricStatus status, MetricDetail detail)
    {
        _value = value;
        Status = status;
        Detail = detail;
    }

    public MetricStatus Status { get; }

    public MetricDetail Detail { get; }

    public bool HasValue => Status == MetricStatus.Available;

    /// <summary>Válido únicamente si <see cref="HasValue"/> es verdadero.</summary>
    public T Value => HasValue
        ? _value
        : throw new InvalidOperationException($"La métrica no tiene valor ({Status}/{Detail}).");

    public T? ValueOrNull => HasValue ? _value : null;

    public T ValueOr(T fallback) => HasValue ? _value : fallback;

    public static Metric<T> Available(T value) => new(value, MetricStatus.Available, MetricDetail.None);

    public static Metric<T> NotSupported(MetricDetail detail = MetricDetail.NotReportedByDevice)
        => new(default, MetricStatus.NotSupported, detail);

    public static Metric<T> RequiresElevation(MetricDetail detail = MetricDetail.None)
        => new(default, MetricStatus.RequiresElevation, detail);

    public static Metric<T> Failed(MetricDetail detail = MetricDetail.None)
        => new(default, MetricStatus.Failed, detail);

    public static Metric<T> Pending() => new(default, MetricStatus.Pending, MetricDetail.None);

    public Metric<TOut> Map<TOut>(Func<T, TOut> selector) where TOut : struct
        => HasValue ? Metric<TOut>.Available(selector(_value)) : new Metric<TOut>(default, Status, Detail);
}

public static class Metric
{
    public static Metric<T> From<T>(T? value, MetricDetail detailIfMissing = MetricDetail.NotReportedByDevice)
        where T : struct
        => value.HasValue ? Metric<T>.Available(value.Value) : Metric<T>.NotSupported(detailIfMissing);
}
