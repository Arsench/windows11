using System.Runtime.InteropServices;

namespace Zenith.Platform.Windows.Interop;

/// <summary>
/// Envoltorio mínimo sobre PDH usando <c>PdhAddEnglishCounterW</c>. Es la única
/// forma fiable de leer contadores por su nombre en inglés en un Windows
/// localizado (español, francés, alemán…), donde <c>PerformanceCounter</c> falla.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const int PDH_MORE_DATA = unchecked((int)0x800007D2);

    private IntPtr _query;
    private bool _disposed;

    private PdhQuery(IntPtr query) => _query = query;

    /// <summary>Devuelve null si PDH no está disponible en este equipo.</summary>
    public static PdhQuery? TryCreate()
    {
        try
        {
            return PdhOpenQueryW(null, IntPtr.Zero, out var handle) == 0 ? new PdhQuery(handle) : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Añade un contador por su ruta en inglés. Devuelve null si no existe aquí.</summary>
    public IntPtr? TryAddCounter(string englishPath)
    {
        if (_disposed) return null;
        return PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var counter) == 0 ? counter : null;
    }

    /// <summary>Toma una muestra. Los contadores de tasa necesitan dos llamadas para dar valor.</summary>
    public bool Collect() => !_disposed && PdhCollectQueryData(_query) == 0;

    public double? TryReadSingle(IntPtr counter)
    {
        if (_disposed) return null;
        var status = PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, IntPtr.Zero, out var value);
        if (status != 0 || value.CStatus != 0) return null;
        return double.IsFinite(value.DoubleValue) ? value.DoubleValue : null;
    }

    /// <summary>Lee un contador con comodín, devolviendo cada instancia con su valor.</summary>
    public IReadOnlyList<(string Instance, double Value)> ReadArray(IntPtr counter)
    {
        if (_disposed) return [];

        const uint format = PDH_FMT_DOUBLE | PDH_FMT_NOCAP100;
        uint bufferSize = 0;

        var status = PdhGetFormattedCounterArrayW(counter, format, ref bufferSize, out _, IntPtr.Zero);
        if (status != PDH_MORE_DATA || bufferSize == 0) return [];

        var buffer = Marshal.AllocHGlobal((int)bufferSize);
        try
        {
            status = PdhGetFormattedCounterArrayW(counter, format, ref bufferSize, out var itemCount, buffer);
            if (status != 0 || itemCount == 0) return [];

            var results = new List<(string, double)>((int)itemCount);
            var itemSize = Marshal.SizeOf<PdhFmtCounterValueItem>();

            for (var i = 0; i < itemCount; i++)
            {
                var item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(buffer + i * itemSize);
                if (item.CStatus != 0 || item.NamePtr == IntPtr.Zero) continue;

                var name = Marshal.PtrToStringUni(item.NamePtr);
                if (string.IsNullOrEmpty(name) || !double.IsFinite(item.DoubleValue)) continue;

                results.Add((name, item.DoubleValue));
            }

            return results;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr NamePtr;
        public uint CStatus;
        private readonly uint _padding;
        public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string counterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArrayW(
        IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);
}
