using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Models;
using Zenith.Core.Primitives;

namespace Zenith.Platform.Windows.Processes;

/// <summary>
/// Lista de procesos por consumo. Deliberadamente simple: no pretende sustituir
/// al Administrador de tareas. El % de CPU se calcula con el delta de tiempo de
/// procesador entre dos consultas, normalizado por número de núcleos.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsProcessProvider(ILogger<WindowsProcessProvider> logger) : IProcessProvider
{
    private readonly Dictionary<int, (TimeSpan Cpu, DateTimeOffset At)> _previous = [];
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<IReadOnlyList<ProcessSample>> GetTopProcessesAsync(int count, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run<IReadOnlyList<ProcessSample>>(() => Collect(count), ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<ProcessSample> Collect(int count)
    {
        var now = DateTimeOffset.UtcNow;
        var cores = Math.Max(1, Environment.ProcessorCount);
        var samples = new List<ProcessSample>();
        var alive = new HashSet<int>();

        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se han podido enumerar los procesos");
            return samples;
        }

        foreach (var process in processes)
        {
            using (process)
            {
                try
                {
                    alive.Add(process.Id);

                    var cpuTime = process.TotalProcessorTime;
                    var workingSet = process.WorkingSet64;
                    var name = process.ProcessName;

                    var cpu = Metric<double>.Pending();
                    if (_previous.TryGetValue(process.Id, out var last))
                    {
                        var elapsed = (now - last.At).TotalMilliseconds;
                        if (elapsed > 0)
                        {
                            var used = (cpuTime - last.Cpu).TotalMilliseconds;
                            cpu = Metric<double>.Available(Math.Clamp(used * 100 / (elapsed * cores), 0, 100));
                        }
                    }

                    _previous[process.Id] = (cpuTime, now);
                    samples.Add(new ProcessSample(process.Id, name, cpu, workingSet));
                }
                catch (Exception)
                {
                    // Procesos protegidos o que terminan mientras los leemos:
                    // se omiten sin ruido, es lo normal.
                }
            }
        }

        // Evita que el diccionario crezca sin límite con procesos ya cerrados.
        foreach (var id in _previous.Keys.Where(id => !alive.Contains(id)).ToList()) _previous.Remove(id);

        return samples
            .OrderByDescending(p => p.CpuPercent.ValueOr(-1))
            .ThenByDescending(p => p.WorkingSetBytes)
            .Take(count)
            .ToList();
    }
}
