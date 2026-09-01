using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Zenith.Core.Abstractions;
using Zenith.Platform.Windows.Interop;

namespace Zenith.Platform.Windows.Files;

/// <summary>
/// Identifica el archivo físico detrás de una ruta. Dos rutas con vínculo duro
/// apuntan al mismo contenido: presentarlas como duplicados haría creer al
/// usuario que va a recuperar espacio que en realidad no existe.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsFileIdentityResolver : IFileIdentityResolver
{
    public string? TryGetPhysicalId(string path)
    {
        try
        {
            using SafeFileHandle handle = File.OpenHandle(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.None);

            if (!NativeMethods.GetFileInformationByHandle(handle, out var info)) return null;

            // Solo importa si de verdad hay más de un vínculo; si no, ahorramos
            // trabajo al que consume esto.
            if (info.NumberOfLinks <= 1) return null;

            return $"{info.VolumeSerialNumber:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
        }
        catch (Exception)
        {
            // Sin permisos o archivo bloqueado: preferimos no saberlo a mentir.
            return null;
        }
    }
}
