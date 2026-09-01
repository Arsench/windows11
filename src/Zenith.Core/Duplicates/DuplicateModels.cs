namespace Zenith.Core.Duplicates;

public enum DuplicatePhase
{
    Idle,
    Enumerating,
    GroupingBySize,
    PartialHashing,
    FullHashing,
    Verifying,
    Completed,
    Cancelled
}

public sealed record DuplicateScanOptions
{
    public required IReadOnlyList<string> Roots { get; init; }

    /// <summary>Los archivos por debajo de este tamaño se ignoran (por defecto 1 KB).</summary>
    public long MinFileSizeBytes { get; init; } = 1024;

    /// <summary>Los archivos vacíos son "iguales" entre sí y no aportan nada. Se ignoran salvo petición expresa.</summary>
    public bool IncludeEmptyFiles { get; init; }

    public bool IncludeHiddenFiles { get; init; }

    /// <summary>Extensiones a incluir (con punto, minúsculas). Null o vacío = todas.</summary>
    public IReadOnlyList<string>? ExtensionFilter { get; init; }

    /// <summary>Comparación byte a byte al final. Elimina cualquier duda por colisión de hash.</summary>
    public bool VerifyByteByByte { get; init; } = true;

    public int MaxParallelism { get; init; } = Math.Min(4, Environment.ProcessorCount);
}

public sealed record DuplicateProgress(
    DuplicatePhase Phase,
    long Processed,
    long Total,
    long FilesDiscovered,
    string? CurrentPath)
{
    public bool IsIndeterminate => Total <= 0;

    /// <summary>Progreso ponderado entre fases, para una sola barra continua en la UI.</summary>
    public double OverallPercent
    {
        get
        {
            var local = Total > 0 ? Math.Clamp(Processed * 100d / Total, 0, 100) : 0d;
            return Phase switch
            {
                DuplicatePhase.Enumerating => 0,
                DuplicatePhase.GroupingBySize => 10,
                DuplicatePhase.PartialHashing => 10 + local * 0.30,
                DuplicatePhase.FullHashing => 40 + local * 0.45,
                DuplicatePhase.Verifying => 85 + local * 0.15,
                DuplicatePhase.Completed => 100,
                _ => 0
            };
        }
    }

    public string PhaseDisplayName => Phase switch
    {
        DuplicatePhase.Enumerating => "Explorando carpetas",
        DuplicatePhase.GroupingBySize => "Agrupando por tamaño",
        DuplicatePhase.PartialHashing => "Comparando fragmentos",
        DuplicatePhase.FullHashing => "Calculando huellas",
        DuplicatePhase.Verifying => "Verificando byte a byte",
        DuplicatePhase.Completed => "Completado",
        DuplicatePhase.Cancelled => "Cancelado",
        _ => "En espera"
    };
}

public sealed record DuplicateFile(string Path, long SizeBytes, DateTime LastWriteUtc)
{
    public string FileName => System.IO.Path.GetFileName(Path);

    public string DirectoryName => System.IO.Path.GetDirectoryName(Path) ?? Path;
}

public sealed record DuplicateGroup(int Index, long FileSizeBytes, IReadOnlyList<DuplicateFile> Files)
{
    /// <summary>Espacio que se recupera conservando exactamente una copia.</summary>
    public long ReclaimableBytes => FileSizeBytes * Math.Max(0, Files.Count - 1);

    public int RedundantCount => Math.Max(0, Files.Count - 1);
}

public sealed record ScanError(string Path, string Message);

public sealed record DuplicateScanResult(
    IReadOnlyList<DuplicateGroup> Groups,
    long FilesScanned,
    long BytesHashed,
    IReadOnlyList<ScanError> Errors,
    bool WasCancelled,
    TimeSpan Elapsed)
{
    public long ReclaimableBytes => Groups.Sum(g => g.ReclaimableBytes);

    public int RedundantFileCount => Groups.Sum(g => g.RedundantCount);

    public static DuplicateScanResult Empty { get; } =
        new([], 0, 0, [], false, TimeSpan.Zero);
}
