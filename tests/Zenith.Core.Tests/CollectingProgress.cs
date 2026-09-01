namespace Zenith.Core.Tests;

/// <summary>
/// <see cref="Progress{T}"/> reparte las llamadas por el contexto de
/// sincronización, lo que en pruebas hace que lleguen tarde. Esta versión es
/// síncrona y determinista.
/// </summary>
public sealed class CollectingProgress<T> : IProgress<T>
{
    private readonly List<T> _reports = [];
    private readonly object _gate = new();

    public void Report(T value)
    {
        lock (_gate) _reports.Add(value);
    }

    public IReadOnlyList<T> Reports
    {
        get
        {
            lock (_gate) return [.. _reports];
        }
    }
}
