namespace Zenith.Core.Monitoring;

/// <summary>
/// Buffer circular de tamaño fijo para las series de los gráficos. Sin
/// asignaciones por muestra: la app puede estar horas abierta sin crecer.
/// </summary>
public sealed class MetricHistory(int capacity)
{
    private readonly double[] _buffer = new double[capacity > 0 ? capacity : 1];
    private readonly object _gate = new();
    private int _start;
    private int _count;

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            lock (_gate) return _count;
        }
    }

    public void Add(double value)
    {
        lock (_gate)
        {
            if (_count < _buffer.Length)
            {
                _buffer[(_start + _count) % _buffer.Length] = value;
                _count++;
            }
            else
            {
                _buffer[_start] = value;
                _start = (_start + 1) % _buffer.Length;
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _start = 0;
            _count = 0;
        }
    }

    /// <summary>Copia la serie en orden cronológico. Devuelve el número de elementos escritos.</summary>
    public int CopyTo(Span<double> destination)
    {
        lock (_gate)
        {
            var n = Math.Min(_count, destination.Length);
            // Se copian las n muestras más recientes.
            var skip = _count - n;
            for (var i = 0; i < n; i++)
            {
                destination[i] = _buffer[(_start + skip + i) % _buffer.Length];
            }
            return n;
        }
    }

    public double[] ToArray()
    {
        lock (_gate)
        {
            var result = new double[_count];
            for (var i = 0; i < _count; i++) result[i] = _buffer[(_start + i) % _buffer.Length];
            return result;
        }
    }
}
