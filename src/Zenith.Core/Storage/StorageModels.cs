using Zenith.Core.Duplicates;

namespace Zenith.Core.Storage;

public enum FileCategory
{
    Video,
    Images,
    Audio,
    Documents,
    Archives,
    Applications,
    DiskImages,
    Code,
    Other
}

public static class FileCategories
{
    private static readonly Dictionary<string, FileCategory> Map = BuildMap();

    public static FileCategory FromExtension(string? extension)
    {
        if (string.IsNullOrEmpty(extension)) return FileCategory.Other;
        return Map.TryGetValue(extension, out var category) ? category : FileCategory.Other;
    }

    public static string DisplayName(FileCategory category) => category switch
    {
        FileCategory.Video => "Vídeo",
        FileCategory.Images => "Imágenes",
        FileCategory.Audio => "Audio",
        FileCategory.Documents => "Documentos",
        FileCategory.Archives => "Comprimidos",
        FileCategory.Applications => "Aplicaciones",
        FileCategory.DiskImages => "Imágenes de disco",
        FileCategory.Code => "Código",
        _ => "Otros"
    };

    private static Dictionary<string, FileCategory> BuildMap()
    {
        var map = new Dictionary<string, FileCategory>(StringComparer.OrdinalIgnoreCase);

        void Add(FileCategory category, params string[] extensions)
        {
            foreach (var e in extensions) map[e] = category;
        }

        Add(FileCategory.Video, ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts");
        Add(FileCategory.Images, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".heic", ".raw", ".cr2", ".nef", ".dng", ".svg", ".psd");
        Add(FileCategory.Audio, ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".wma", ".opus");
        Add(FileCategory.Documents, ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".odt", ".ods", ".epub", ".md", ".csv");
        Add(FileCategory.Archives, ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".cab");
        Add(FileCategory.Applications, ".exe", ".msi", ".dll", ".appx", ".msix", ".bat", ".cmd");
        Add(FileCategory.DiskImages, ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".wim", ".esd");
        Add(FileCategory.Code, ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".go", ".rs", ".rb", ".php", ".html", ".css", ".json", ".xml", ".yml", ".yaml", ".sql");

        return map;
    }
}

/// <summary>
/// Un nodo del árbol de carpetas. Guardamos agregados por carpeta, no un objeto
/// por archivo: analizar un disco de 1 TB no puede costar gigabytes de RAM.
/// </summary>
public sealed class FolderNode(string fullPath, string name, FolderNode? parent)
{
    private readonly List<FolderNode> _children = [];

    public string FullPath { get; } = fullPath;

    public string Name { get; } = name;

    public FolderNode? Parent { get; } = parent;

    /// <summary>Tamaño acumulado, incluidas todas las subcarpetas.</summary>
    public long TotalSizeBytes { get; internal set; }

    /// <summary>Bytes de los archivos que están directamente en esta carpeta.</summary>
    public long OwnSizeBytes { get; internal set; }

    public int FileCount { get; internal set; }

    public bool HasErrors { get; internal set; }

    public IReadOnlyList<FolderNode> Children => _children;

    internal void AddChild(FolderNode child) => _children.Add(child);

    internal void SortChildrenBySizeDescending()
    {
        _children.Sort(static (a, b) => b.TotalSizeBytes.CompareTo(a.TotalSizeBytes));
        foreach (var child in _children) child.SortChildrenBySizeDescending();
    }

    /// <summary>Porcentaje que representa esta carpeta dentro de su padre.</summary>
    public double PercentOfParent =>
        Parent is { TotalSizeBytes: > 0 } ? TotalSizeBytes * 100d / Parent.TotalSizeBytes : 100d;
}

public sealed record LargeFile(string Path, long SizeBytes)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed record CategoryTotal(FileCategory Category, long SizeBytes, int FileCount)
{
    public string DisplayName => FileCategories.DisplayName(Category);
}

public sealed record StorageScanProgress(
    long DirectoriesScanned,
    long FilesScanned,
    long BytesScanned,
    string? CurrentPath);

public sealed record StorageScanResult(
    FolderNode? Root,
    IReadOnlyList<CategoryTotal> Categories,
    IReadOnlyList<LargeFile> LargestFiles,
    long TotalBytes,
    long FileCount,
    IReadOnlyList<ScanError> Errors,
    bool WasCancelled,
    TimeSpan Elapsed)
{
    public static StorageScanResult Empty { get; } =
        new(null, [], [], 0, 0, [], false, TimeSpan.Zero);
}
