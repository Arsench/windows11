using System.IO.Hashing;

namespace Zenith.Core.Duplicates;

/// <summary>
/// Huellas de contenido con XxHash128. No es criptográfico, pero para detectar
/// duplicados es órdenes de magnitud más rápido que SHA-256 y la probabilidad de
/// colisión a 128 bits es despreciable. Aun así, el escáner puede verificar
/// byte a byte al final: el hash solo sirve para descartar candidatos.
/// </summary>
public static class FileHasher
{
    private const int PartialChunkBytes = 64 * 1024;
    private const int StreamBufferBytes = 1024 * 1024;

    private static FileStream OpenRead(string path, FileOptions options) =>
        new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            // ReadWrite en Share: si otro proceso tiene el archivo abierto para
            // escritura, preferimos leerlo a fallar. El tamaño ya lo hemos fijado.
            Share = FileShare.ReadWrite | FileShare.Delete,
            Options = options,
            BufferSize = 0
        });

    /// <summary>Huella barata: cabecera + cola. Descarta la mayoría de falsos candidatos.</summary>
    public static async Task<string> ComputePartialAsync(string path, long sizeBytes, CancellationToken ct)
    {
        var hash = new XxHash128();
        var buffer = new byte[PartialChunkBytes];

        await using var stream = OpenRead(path, FileOptions.Asynchronous);

        var headLength = (int)Math.Min(PartialChunkBytes, sizeBytes);
        await ReadExactAsync(stream, buffer, headLength, ct).ConfigureAwait(false);
        hash.Append(buffer.AsSpan(0, headLength));

        if (sizeBytes > PartialChunkBytes * 2L)
        {
            stream.Seek(-PartialChunkBytes, SeekOrigin.End);
            await ReadExactAsync(stream, buffer, PartialChunkBytes, ct).ConfigureAwait(false);
            hash.Append(buffer.AsSpan(0, PartialChunkBytes));
        }

        // El tamaño entra en la huella para que dos fragmentos iguales de
        // archivos de distinto tamaño nunca colisionen. (Sin stackalloc: los
        // locales de tipo Span no están permitidos en métodos async.)
        hash.Append(BitConverter.GetBytes(sizeBytes));

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    /// <summary>Huella completa del contenido.</summary>
    public static async Task<string> ComputeFullAsync(string path, CancellationToken ct)
    {
        var hash = new XxHash128();
        await using var stream = OpenRead(path, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[StreamBufferBytes];

        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            hash.Append(buffer.AsSpan(0, read));
        }

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    /// <summary>Comparación exacta. La verdad definitiva sobre si dos archivos son idénticos.</summary>
    public static async Task<bool> AreIdenticalAsync(string pathA, string pathB, CancellationToken ct)
    {
        if (string.Equals(pathA, pathB, StringComparison.OrdinalIgnoreCase)) return true;

        await using var a = OpenRead(pathA, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var b = OpenRead(pathB, FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (a.Length != b.Length) return false;

        var bufferA = new byte[StreamBufferBytes];
        var bufferB = new byte[StreamBufferBytes];

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var readA = await FillAsync(a, bufferA, ct).ConfigureAwait(false);
            var readB = await FillAsync(b, bufferB, ct).ConfigureAwait(false);

            if (readA != readB) return false;
            if (readA == 0) return true;

            if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB))) return false;
        }
    }

    private static async Task<int> FillAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        var total = 0;
        while (total < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, count - total), ct).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
        }
    }
}
