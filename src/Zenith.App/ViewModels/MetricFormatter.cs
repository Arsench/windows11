using Zenith.App.Localization;
using Zenith.Core.Primitives;

namespace Zenith.App.ViewModels;

/// <summary>
/// Traduce <see cref="Metric{T}"/> a texto de pantalla. Punto único donde se
/// decide cómo se muestra "no hay dato", para que ninguna pantalla enseñe un 0
/// como si fuese una medición.
/// </summary>
public static class MetricFormatter
{
    private static Loc L => Loc.Instance;

    public static string Unavailable => L["CommonNotAvailable"];

    public static string Percent(Metric<double> metric, int decimals = 0) => metric.HasValue
        ? metric.Value.ToString("N" + decimals, L.Culture) + " %"
        : Present.MetricUnavailable(metric.Status, metric.Detail);

    public static string Ghz(Metric<double> metric) => metric.HasValue
        ? metric.Value.ToString("N2", L.Culture) + " GHz"
        : Present.MetricUnavailable(metric.Status, metric.Detail);

    public static string Mhz(Metric<int> metric) => metric.HasValue
        ? metric.Value.ToString("N0", L.Culture) + " MHz"
        : Present.MetricUnavailable(metric.Status, metric.Detail);

    public static string Bytes(Metric<long> metric) => metric.HasValue
        ? ByteSize.Format(metric.Value)
        : Present.MetricUnavailable(metric.Status, metric.Detail);

    public static string Integer(Metric<int> metric) => metric.HasValue
        ? metric.Value.ToString("N0", L.Culture)
        : Present.MetricUnavailable(metric.Status, metric.Detail);

    public static string Celsius(double? value) => value is { } celsius
        ? celsius.ToString("N0", L.Culture) + " °C"
        : L["CommonSensorUnavailable"];

    public static string Number(long value) => value.ToString("N0", L.Culture);

    public static string Number(double value, int decimals) => value.ToString("N" + decimals, L.Culture);

    /// <summary>Singular y plural con claves distintas: no todos los idiomas pluralizan igual.</summary>
    public static string Count(int value, string singularKey, string pluralKey) =>
        L.Format(value == 1 ? singularKey : pluralKey, value.ToString("N0", L.Culture));

    public static string Seconds(TimeSpan elapsed) =>
        elapsed.TotalSeconds.ToString("N1", L.Culture) + " s";
}
