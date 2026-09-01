using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;
using Zenith.Core.Safety;
using Zenith.Platform.Windows.Interop;

namespace Zenith.Platform.Windows.Files;

/// <summary>
/// Ejecuta las operaciones destructivas. Vuelve a comprobar la seguridad de cada
/// ruta justo antes de tocarla: el plan pudo construirse hace minutos y el
/// usuario pudo cambiar la selección por el camino.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileSystemOperations(
    PathSafetyGuard safety,
    ILogger<WindowsFileSystemOperations> logger) : IFileSystemOperations
{
    public Task<FileActionResult> ExecuteAsync(
        FileActionRequest request,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.Kind == FileActionKind.RecycleBin
            ? StaExecutor.RunAsync(() => Execute(request, progress, ct))
            : Task.Run(() => Execute(request, progress, ct), ct);
    }

    private FileActionResult Execute(FileActionRequest request, IProgress<double>? progress, CancellationToken ct)
    {
        var succeeded = new List<string>();
        var failed = new List<FileActionFailure>();
        long bytes = 0;
        var index = 0;

        foreach (var path in request.Paths)
        {
            if (ct.IsCancellationRequested)
            {
                failed.Add(new FileActionFailure(path, FileActionError.Cancelled, "Cancelled before processing"));
                continue;
            }

            index++;
            progress?.Report(index * 100d / Math.Max(1, request.Paths.Count));

            // Segunda verificación, inmediatamente antes de actuar.
            var verdict = safety.EvaluateFile(path);
            if (verdict.Level == SafetyLevel.Blocked)
            {
                failed.Add(new FileActionFailure(
                    path, FileActionError.BlockedBySafety, "Blocked by PathSafetyGuard", verdict));
                continue;
            }

            long size;
            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    failed.Add(new FileActionFailure(path, FileActionError.FileMissing, "File not found"));
                    continue;
                }
                size = info.Length;
            }
            catch (Exception ex)
            {
                failed.Add(new FileActionFailure(path, FileActionError.CheckFailed, ex.ToString()));
                continue;
            }

            try
            {
                switch (request.Kind)
                {
                    case FileActionKind.RecycleBin:
                        SendToRecycleBin(path);
                        break;

                    case FileActionKind.PermanentDelete:
                        File.Delete(path);
                        break;

                    case FileActionKind.Move:
                        MoveFile(path, request.DestinationFolder);
                        break;

                    default:
                        throw new NotSupportedException($"Operación no soportada: {request.Kind}");
                }

                succeeded.Add(path);
                bytes += size;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fallo al procesar {Path}", path);
                failed.Add(new FileActionFailure(path, Classify(ex), ex.ToString()));
            }
        }

        progress?.Report(100);
        return new FileActionResult(succeeded, failed, bytes);
    }

    private static void SendToRecycleBin(string path)
    {
        var operation = new NativeMethods.ShFileOpStruct
        {
            hwnd = IntPtr.Zero,
            wFunc = NativeMethods.FO_DELETE,
            // pFrom es una lista terminada en doble nulo.
            pFrom = path + "\0\0",
            pTo = null,
            fFlags = NativeMethods.FOF_ALLOWUNDO
                     | NativeMethods.FOF_NOCONFIRMATION
                     | NativeMethods.FOF_NOERRORUI
                     | NativeMethods.FOF_SILENT,
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null
        };

        var result = NativeMethods.SHFileOperation(ref operation);
        if (result != 0) throw new IOException($"SHFileOperation ha devuelto 0x{result:X}.");
        if (operation.fAnyOperationsAborted) throw new IOException("La operación ha sido interrumpida por el shell.");
    }

    private static void MoveFile(string path, string? destinationFolder)
    {
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new InvalidOperationException("No se ha indicado carpeta de destino.");

        Directory.CreateDirectory(destinationFolder);

        var target = BuildUniqueDestination(destinationFolder, Path.GetFileName(path));

        // Sin sobrescribir nunca: si el nombre existe, ya se ha resuelto arriba.
        File.Move(path, target, overwrite: false);
    }

    /// <summary>Evita colisiones al aplanar varias carpetas en una sola de destino.</summary>
    internal static string BuildUniqueDestination(string folder, string fileName)
    {
        var candidate = Path.Combine(folder, fileName);
        if (!File.Exists(candidate)) return candidate;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var i = 2; i < 10_000; i++)
        {
            candidate = Path.Combine(folder, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }

        throw new NameCollisionException(folder);
    }

    /// <summary>
    /// Clasifica el fallo. Devuelve un código: la frase que ve el usuario la
    /// compone la interfaz, en su idioma. El detalle técnico va al registro.
    /// </summary>
    private static FileActionError Classify(Exception ex) => ex switch
    {
        NameCollisionException => FileActionError.NameCollision,
        UnauthorizedAccessException => FileActionError.AccessDenied,
        FileNotFoundException => FileActionError.FileMissing,
        DirectoryNotFoundException => FileActionError.DestinationMissing,
        PathTooLongException => FileActionError.PathTooLong,
        IOException => FileActionError.InUse,
        _ => FileActionError.Unknown
    };
}

/// <summary>Demasiados archivos con el mismo nombre en la carpeta de destino.</summary>
internal sealed class NameCollisionException(string folder)
    : IOException($"Too many name collisions in '{folder}'.");
