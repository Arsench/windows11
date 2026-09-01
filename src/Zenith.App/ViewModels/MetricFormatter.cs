using System.Globalization;
using Zenith.Core.Primitives;

namespace Zenith.App.ViewModels;

/// <summary>
/// Traduce <see cref="Metric{T}"/> a texto de pantalla. Punto único donde se
/// decide cómo se muestra "no hay dato", para que ninguna pantalla enseñe un 0
/// como si fuese una medición.
/// </summary>
public static class MetricFormatter
{
    public const string Unavailable = "No disponible";

    public static string StatusText(MetricStatus status, string? reason) => status switch
    {
        MetricStatus.Pending => "Midiendo…",
        MetricStatus.RequiresElevation => "Requiere administrador",
        MetricStatus.NotSupported => Unavailable,
        MetricStatus.Failed => "Error de lectura",
        _ => reason ?? Unavailable
    };

    public static string Percent(Metric<double> metric, int decimals = 0) => metric.HasValue
        ? metric.Value.ToString("N" + decimals, CultureInfo.CurrentCulture) + " %"
        : StatusText(metric.Status, metric.Reason);

    public static string Ghz(Metric<double> metric) => metric.HasValue
        ? metric.Value.ToString("N2", CultureInfo.CurrentCulture) + " GHz"
        : StatusText(metric.Status, metric.Reason);

    public static string Mhz(Metric<int> metric) => metric.HasValue
        ? metric.Value.ToString("N0", CultureInfo.CurrentCulture) + " MHz"
        : StatusText(metric.Status, metric.Reason);

    public static string Bytes(Metric<long> metric) => metric.HasValue
        ? ByteSize.Format(metric.Value)
        : StatusText(metric.Status, metric.Reason);

    public static string Integer(Metric<int> metric) => metric.HasValue
        ? metric.Value.ToString("N0", CultureInfo.CurrentCulture)
        : StatusText(metric.Status, metric.Reason);

    public static string Celsius(double? value) => value is { } celsius
        ? celsius.ToString("N0", CultureInfo.CurrentCulture) + " °C"
        : "Sensor no disponible";

    public static string Count(int value, string singular, string plural) =>
        $"{value:N0} {(value == 1 ? singular : plural)}";
}
