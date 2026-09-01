using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Zenith.Core.Duplicates;
using Zenith.Core.Safety;

namespace Zenith.Core.Storage;

/// <summary>
/// Recorre una carpeta o unidad y construye el árbol de ocupación. Sincrónico por
/// dentro (la E/S de directorios lo es) y siempre invocado desde un hilo de fondo.
/// </summary>
public sealed class StorageAnalyzer(PathSafetyGuard safety, ILogger<StorageAnalyzer>? logger = null)
{
    private const int ProgressThrottle = 512;
    private const int MaxLargestFiles = 100;
    private const int MaxRecordedErrors = 500;
    private const int MaxDepth = 64;

    private readonly ILogger<StorageAnalyzer> _logger = logger ?? NullLogger<StorageAnalyzer>.Instance;

    public Task<StorageScanResult> AnalyzeAsync(
        string rootPath,
        IProgress<StorageScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        // El token NO se pasa a Task.Run a propósito: queremos que el cuerpo se
        // ejecute y devuelva un resultado marcado como cancelado, en lugar de
        // que la tarea muera con una excepción que el llamante tenga que cazar.
        return Task.Run(() => Analyze(rootPath, progress, ct), CancellationToken.None);
    }

    private StorageScanResult Analyze(
        string rootPath,
        IProgress<StorageScanProgress>? progress,
        CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var errors = new List<ScanError>();
        var categories = new Dictionary<FileCategory, (long Bytes, int Count)>();

        // Cola de prioridad mínima: mantiene solo los N archivos más grandes.
        var largest = new PriorityQueue<LargeFile, long>();

        long directories = 0, files = 0, bytes = 0;

        var full = Path.GetFullPath(rootPath);
        var root = new FolderNode(full, DisplayNameFor(full), null);

        void ReportProgress(string? current) =>
            progress?.Report(new StorageScanProgress(directories, files, bytes, current));

        long Walk(FolderNode node, int depth)
        {
            ct.ThrowIfCancellationRequested();
            directories++;

            if (depth > MaxDepth)
            {
                node.HasErrors = true;
                return 0;
            }

            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(node.FullPath).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                node.HasErrors = true;
                Record(errors, node.FullPath, DuplicateScanner.DescribeIoError(ex), ex);
                return 0;
            }

            long total = 0;
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
                    node.HasErrors = true;
                    Record(errors, node.FullPath, DuplicateScanner.DescribeIoError(ex), ex);
                    break;
                }

                try
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                    if (entry is DirectoryInfo dir)
                    {
                        if (safety.ShouldSkipDuringScan(dir.FullName)) continue;

                        var child = new FolderNode(dir.FullName, dir.Name, node);
                        node.AddChild(child);
                        total += Walk(child, depth + 1);
                        continue;
                    }

                    if (entry is not FileInfo file) continue;

                    var length = file.Length;
                    total += length;
                    node.OwnSizeBytes += length;
                    node.FileCount++;

                    files++;
                    bytes += length;

                    var category = FileCategories.FromExtension(file.Extension);
                    var current = categories.GetValueOrDefault(category);
                    categories[category] = (current.Bytes + length, current.Count + 1);

                    largest.Enqueue(new LargeFile(file.FullName, length), length);
                    if (largest.Count > MaxLargestFiles) largest.Dequeue();

                    if (files % ProgressThrottle == 0) ReportProgress(file.FullName);
                }
                catch (Exception ex)
                {
                    node.HasErrors = true;
                    Record(errors, entry.FullName, DuplicateScanner.DescribeIoError(ex), ex);
                }
            }

            node.TotalSizeBytes = total;
            return total;
        }

        try
        {
            Walk(root, 0);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new StorageScanResult(null, [], [], bytes, files, errors, true, stopwatch.Elapsed);
        }

        root.SortChildrenBySizeDescending();
        ReportProgress(null);
        stopwatch.Stop();

        var categoryTotals = categories
            .Select(kv => new CategoryTotal(kv.Key, kv.Value.Bytes, kv.Value.Count))
            .OrderByDescending(c => c.SizeBytes)
            .ToList();

        var largestFiles = new List<LargeFile>(largest.Count);
        while (largest.TryDequeue(out var item, out _)) largestFiles.Add(item);
        largestFiles.Reverse();

        return new StorageScanResult(
            root, categoryTotals, largestFiles, root.TotalSizeBytes, files, errors, false, stopwatch.Elapsed);
    }

    private static string DisplayNameFor(string fullPath)
    {
        var name = Path.GetFileName(fullPath);
        return string.IsNullOrEmpty(name) ? fullPath : name;
    }

    private void Record(List<ScanError> errors, string path, string message, Exception ex)
    {
        _logger.LogDebug(ex, "Error al analizar {Path}", path);
        if (errors.Count < MaxRecordedErrors) errors.Add(new ScanError(path, message));
    }
}
