using System.Globalization;

namespace Zenith.Core.Primitives;

/// <summary>Formateo consistente de tamaños en toda la aplicación.</summary>
public static class ByteSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>Ej.: 1536 -> "1,5 KB". Usa base 1024 (igual que el Explorador de Windows).</summary>
    public static string Format(long bytes, int maxDecimals = 1)
    {
        if (bytes < 0) return "—";
        if (bytes < 1024) return string.Create(CultureInfo.CurrentCulture, $"{bytes} B");

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        // Menos decimales cuando el número ya es grande: 623 GB, no 623,4 GB.
        var decimals = value >= 100 ? 0 : value >= 10 ? Math.Min(1, maxDecimals) : maxDecimals;
        return value.ToString("N" + decimals, CultureInfo.CurrentCulture) + " " + Units[unit];
    }

    /// <summary>Formatea el par usado/total con una sola unidad: "12,4 / 32 GB".</summary>
    public static string FormatPair(long used, long total)
    {
        if (total <= 0) return "—";

        double divisor = 1;
        var unit = 0;
        while (total / divisor >= 1024 && unit < Units.Length - 1)
        {
            divisor *= 1024;
            unit++;
        }

        var u = used / divisor;
        var t = total / divisor;
        var ud = u >= 100 ? 0 : 1;
        var td = t >= 100 ? 0 : 1;
        return string.Create(
            CultureInfo.CurrentCulture,
            $"{u.ToString("N" + ud, CultureInfo.CurrentCulture)} / {t.ToString("N" + td, CultureInfo.CurrentCulture)} {Units[unit]}");
    }
}
