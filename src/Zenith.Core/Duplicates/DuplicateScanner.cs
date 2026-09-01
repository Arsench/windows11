using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zenith.Core.Abstractions;
using Zenith.Core.Safety;

namespace Zenith.Core.Duplicates;

/// <summary>
/// Detección de duplicados en cascada: cada fase solo recibe lo que sobrevivió a
/// la anterior, así el trabajo caro (leer contenido) se hace sobre un conjunto
/// mínimo. El nombre del archivo NUNCA interviene en la decisión.
/// </summary>
public sealed class DuplicateScanner
{
    private const int ProgressThrottle = 64;
    private const int MaxRecordedErrors = 500;

    private readonly PathSafetyGuard _safety;
    private readonly IFileIdentityResolver? _identity;
    private readonly ILogger<DuplicateScanner> _logger;

    public DuplicateScanner(
        PathSafetyGuard safety,
        IFileIdentityResolver? identity = null,
        ILogger<DuplicateScanner>? logger = null)
    {
        _safety = safety;
        _identity = identity;
        _logger = logger ?? NullLogger<DuplicateScanner>.Instance;
    }

    public async Task<DuplicateScanResult> ScanAsync(
        DuplicateScanOptions options,
        IProgress<DuplicateProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();
        var errors = new ConcurrentBag<ScanError>();
        long bytesHashed = 0;

        try
        {
            // ---- Fase 1: enumerar ------------------------------------------------
            var files = Enumerate(options, errors, progress, ct, out var skippedBySize, out var skippedByType);

            // ---- Fase 2: agrupar por tamaño --------------------------------------
            progress?.Report(new DuplicateProgress(DuplicatePhase.GroupingBySize, 0, 0, files.Count, null));

            var bySize = files
                .GroupBy(f => f.SizeBytes)
                .Where(g => g.Count() > 1)
                .Select(g => (IReadOnlyList<DuplicateFile>)g.ToList())
                .ToList();

            // Los vínculos duros apuntan al mismo contenido físico: borrar uno no
            // libera nada, así que no son duplicados.
            bySize = CollapseHardLinks(bySize);

            // ---- Fase 3: huella parcial ------------------------------------------
            var afterPartial = await RefineByHashAsync(
                bySize,
                DuplicatePhase.PartialHashing,
                (file, token) => FileHasher.ComputePartialAsync(file.Path, file.SizeBytes, token),
                files.Count,
                errors,
                progress,
                options.MaxParallelism,
                ct).ConfigureAwait(false);

            // ---- Fase 4: huella completa -----------------------------------------
            // Solo para archivos mayores que el fragmento parcial: por debajo,
            // la huella parcial ya cubrió el archivo entero.
            var needFullHash = afterPartial.Where(g => g[0].SizeBytes > 64 * 1024).ToList();
            var alreadyExact = afterPartial.Where(g => g[0].SizeBytes <= 64 * 1024).ToList();

            var afterFull = await RefineByHashAsync(
                needFullHash,
                DuplicatePhase.FullHashing,
                async (file, token) =>
                {
                    var hash = await FileHasher.ComputeFullAsync(file.Path, token).ConfigureAwait(false);
                    Interlocked.Add(ref bytesHashed, file.SizeBytes);
                    return hash;
                },
                files.Count,
                errors,
                progress,
                options.MaxParallelism,
                ct).ConfigureAwait(false);

            var candidates = alreadyExact.Concat(afterFull).ToList();

            // ---- Fase 5: verificación byte a byte --------------------------------
            if (options.VerifyByteByByte)
            {
                candidates = await VerifyAsync(candidates, files.Count, errors, progress, ct).ConfigureAwait(false);
            }

            var groups = candidates
                .Where(g => g.Count > 1)
                .OrderByDescending(g => g[0].SizeBytes * (g.Count - 1))
                .Select((g, i) => new DuplicateGroup(i + 1, g[0].SizeBytes, [.. g.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)]))
                .ToList();

            progress?.Report(new DuplicateProgress(DuplicatePhase.Completed, 1, 1, files.Count, null));

            stopwatch.Stop();
            return new DuplicateScanResult(
                groups, files.Count, bytesHashed, [.. errors], false, stopwatch.Elapsed,
                skippedBySize, skippedByType);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            progress?.Report(new DuplicateProgress(DuplicatePhase.Cancelled, 0, 0, 0, null));
            return new DuplicateScanResult([], 0, bytesHashed, [.. errors], true, stopwatch.Elapsed);
        }
    }

    // ------------------------------------------------------------------ fase 1

    private List<DuplicateFile> Enumerate(
        DuplicateScanOptions options,
        ConcurrentBag<ScanError> errors,
        IProgress<DuplicateProgress>? progress,
        CancellationToken ct,
        out long skippedBySize,
        out long skippedByType)
    {
        // Se cuentan los descartes para poder explicar un "sin duplicados" que en
        // realidad es "no he mirado nada".
        long bySize = 0, byType = 0;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<DuplicateFile>();
        var extensions = options.ExtensionFilter is { Count: > 0 }
            ? new HashSet<string>(options.ExtensionFilter, StringComparer.OrdinalIgnoreCase)
            : null;

        var pending = new Stack<string>();
        foreach (var root in options.Roots)
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            try
            {
                pending.Push(Path.GetFullPath(root));
            }
            catch (Exception ex)
            {
                Record(errors, root, ScanErrorKind.InvalidPath, ex);
            }
        }

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            if (_safety.ShouldSkipDuringScan(current)) continue;

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(current).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                Record(errors, current, ClassifyIoError(ex), ex);
                continue;
            }

            // El enumerador puede lanzar durante la iteración (permisos, disco
            // desconectado), no solo al crearse: por eso el bucle es manual.
            using var enumerator = entries.GetEnumerator();
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                FileSystemInfo entry;
                try
                {
                    if (!enumerator.MoveNext()) break;
                    entry = enumerator.Current;
                }
                catch (Exception ex)
                {
                    Record(errors, current, ClassifyIoError(ex), ex);
                    break;
                }

                try
                {
                    var attributes = entry.Attributes;

                    // Nunca seguimos enlaces: evitan ciclos infinitos y falsos duplicados.
                    if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (!options.IncludeHiddenFiles && attributes.HasFlag(FileAttributes.Hidden)) continue;
                    if (attributes.HasFlag(FileAttributes.System)) continue;

                    if (entry is DirectoryInfo dir)
                    {
                        pending.Push(dir.FullName);
                        continue;
                    }

                    if (entry is not FileInfo file) continue;

                    if (file.Length == 0 && !options.IncludeEmptyFiles) continue;

                    if (file.Length < options.MinFileSizeBytes)
                    {
                        bySize++;
                        continue;
                    }

                    if (extensions is not null && !extensions.Contains(file.Extension))
                    {
                        byType++;
                        continue;
                    }

                    if (!seen.Add(file.FullName)) continue;

                    results.Add(new DuplicateFile(file.FullName, file.Length, file.LastWriteTimeUtc));

                    if (results.Count % ProgressThrottle == 0)
                    {
                        progress?.Report(new DuplicateProgress(
                            DuplicatePhase.Enumerating, 0, 0, results.Count, file.FullName));
                    }
                }
                catch (Exception ex)
                {
                    Record(errors, entry.FullName, ClassifyIoError(ex), ex);
                }
            }
        }

        progress?.Report(new DuplicateProgress(DuplicatePhase.Enumerating, 0, 0, results.Count, null));

        skippedBySize = bySize;
        skippedByType = byType;
        return results;
    }

    // ------------------------------------------------------------------ fase 2b

    private List<IReadOnlyList<DuplicateFile>> CollapseHardLinks(List<IReadOnlyList<DuplicateFile>> groups)
    {
        if (_identity is null) return groups;

        var result = new List<IReadOnlyList<DuplicateFile>>(groups.Count);
        foreach (var group in groups)
        {
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var kept = new List<DuplicateFile>(group.Count);

            foreach (var file in group)
            {
                var id = _identity.TryGetPhysicalId(file.Path);
                if (id is null || seenIds.Add(id)) kept.Add(file);
            }

            if (kept.Count > 1) result.Add(kept);
        }

        return result;
    }

    // ------------------------------------------------------------------ fases 3 y 4

    private async Task<List<IReadOnlyList<DuplicateFile>>> RefineByHashAsync(
        List<IReadOnlyList<DuplicateFile>> groups,
        DuplicatePhase phase,
        Func<DuplicateFile, CancellationToken, Task<string>> hashFunc,
        long filesDiscovered,
        ConcurrentBag<ScanError> errors,
        IProgress<DuplicateProgress>? progress,
        int maxParallelism,
        CancellationToken ct)
    {
        var work = groups.SelectMany(g => g.Select(f => (Group: g, File: f))).ToList();
        var total = work.Count;
        if (total == 0) return [];

        var hashes = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        long processed = 0;

        await Parallel.ForEachAsync(
            work,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, maxParallelism), CancellationToken = ct },
            async (item, token) =>
            {
                try
                {
                    var hash = await hashFunc(item.File, token).ConfigureAwait(false);
                    hashes[item.File.Path] = hash;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Un archivo ilegible se descarta: nunca se marca como duplicado a ciegas.
                    Record(errors, item.File.Path, ClassifyIoError(ex), ex);
                }

                var done = Interlocked.Increment(ref processed);
                if (done % ProgressThrottle == 0 || done == total)
                {
                    progress?.Report(new DuplicateProgress(phase, done, total, filesDiscovered, item.File.Path));
                }
            }).ConfigureAwait(false);

        var refined = new List<IReadOnlyList<DuplicateFile>>();
        foreach (var group in groups)
        {
            foreach (var subgroup in group
                         .Where(f => hashes.ContainsKey(f.Path))
                         .GroupBy(f => hashes[f.Path], StringComparer.Ordinal)
                         .Where(g => g.Count() > 1))
            {
                refined.Add(subgroup.ToList());
            }
        }

        return refined;
    }

    // ------------------------------------------------------------------ fase 5

    private async Task<List<IReadOnlyList<DuplicateFile>>> VerifyAsync(
        List<IReadOnlyList<DuplicateFile>> groups,
        long filesDiscovered,
        ConcurrentBag<ScanError> errors,
        IProgress<DuplicateProgress>? progress,
        CancellationToken ct)
    {
        var verified = new List<IReadOnlyList<DuplicateFile>>();
        var total = groups.Count;
        var processed = 0;

        foreach (var group in groups)
        {
            ct.ThrowIfCancellationRequested();
            processed++;

            // Dentro de un grupo pueden convivir contenidos distintos si hubiera
            // colisión de hash: se reparte en subgrupos exactos por comparación real.
            var buckets = new List<List<DuplicateFile>>();

            foreach (var file in group)
            {
                var placed = false;
                foreach (var bucket in buckets)
                {
                    try
                    {
                        if (await FileHasher.AreIdenticalAsync(bucket[0].Path, file.Path, ct).ConfigureAwait(false))
                        {
                            bucket.Add(file);
                            placed = true;
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Record(errors, file.Path, ClassifyIoError(ex), ex);
                        placed = true; // Se descarta: no lo agrupamos sin haberlo podido leer.
                        break;
                    }
                }

                if (!placed) buckets.Add([file]);
            }

            foreach (var bucket in buckets.Where(b => b.Count > 1))
            {
                verified.Add(bucket);
            }

            if (processed % 8 == 0 || processed == total)
            {
                progress?.Report(new DuplicateProgress(
                    DuplicatePhase.Verifying, processed, total, filesDiscovered, group[0].Path));
            }
        }

        return verified;
    }

    // ------------------------------------------------------------------ utilidades

    private void Record(ConcurrentBag<ScanError> errors, string path, ScanErrorKind kind, Exception ex)
    {
        _logger.LogDebug(ex, "Error al procesar {Path}", path);
        if (errors.Count < MaxRecordedErrors) errors.Add(new ScanError(path, kind));
    }

    /// <summary>
    /// Clasifica una excepción de E/S. Devuelve un código, no una frase: quien
    /// pinta la pantalla decide en qué idioma se cuenta.
    /// </summary>
    internal static ScanErrorKind ClassifyIoError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => ScanErrorKind.AccessDenied,
        DirectoryNotFoundException => ScanErrorKind.DirectoryMissing,
        FileNotFoundException => ScanErrorKind.FileMissing,
        PathTooLongException => ScanErrorKind.PathTooLong,
        IOException => ScanErrorKind.InUse,
        _ => ScanErrorKind.Unknown
    };
}
