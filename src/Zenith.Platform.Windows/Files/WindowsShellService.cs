using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Zenith.Core.Abstractions;

namespace Zenith.Platform.Windows.Files;

[SupportedOSPlatform("windows")]
public sealed class WindowsShellService(ILogger<WindowsShellService> logger) : IShellService
{
    public void OpenFile(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true }, path);

    public void OpenFolder(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true }, path);

    public void RevealInExplorer(string path)
    {
        // /select, abre el Explorador con el archivo ya resaltado.
        var info = new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true };
        Start(info, path);
    }

    private void Start(ProcessStartInfo info, string path)
    {
        try
        {
            Process.Start(info)?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se ha podido abrir {Path}", path);
        }
    }
}
