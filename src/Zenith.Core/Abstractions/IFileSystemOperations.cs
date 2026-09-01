using Zenith.Core.Safety;

namespace Zenith.Core.Abstractions;

public enum FileActionKind
{
    RecycleBin,
    PermanentDelete,
    Move
}

public sealed record FileActionRequest(
    IReadOnlyList<string> Paths,
    FileActionKind Kind,
    string? DestinationFolder = null);

/// <summary>Por qué falló una operación sobre un archivo concreto.</summary>
public enum FileActionError
{
    Unknown,
    AccessDenied,
    FileMissing,
    DestinationMissing,
    PathTooLong,
    InUse,
    BlockedBySafety,
    Cancelled,
    CheckFailed,
    NameCollision
}

/// <param name="TechnicalDetail">Solo para el registro. Nunca se muestra al usuario.</param>
public sealed record FileActionFailure(
    string Path,
    FileActionError Error,
    string TechnicalDetail,
    SafetyVerdict? Verdict = null);

public sealed record FileActionResult(
    IReadOnlyList<string> Succeeded,
    IReadOnlyList<FileActionFailure> Failed,
    long BytesAffected)
{
    public bool IsCompleteSuccess => Failed.Count == 0;
    public bool IsPartial => Succeeded.Count > 0 && Failed.Count > 0;
}

/// <summary>
/// Operaciones destructivas sobre archivos. Aislado en su propia interfaz para
/// que sea imposible borrar algo por accidente desde otra capa.
/// </summary>
public interface IFileSystemOperations
{
    Task<FileActionResult> ExecuteAsync(
        FileActionRequest request,
        IProgress<double>? progress = null,
        CancellationToken ct = default);
}

/// <summary>Integración con el shell de Windows (abrir archivo / abrir carpeta).</summary>
public interface IShellService
{
    void OpenFile(string path);

    void RevealInExplorer(string path);

    void OpenFolder(string path);
}
