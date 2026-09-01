namespace Zenith.Core.Safety;

public enum SafetyLevel
{
    /// <summary>Operación normal sobre datos del usuario.</summary>
    Allowed,

    /// <summary>Permitida, pero la UI debe advertir explícitamente antes de continuar.</summary>
    Warning,

    /// <summary>Nunca. La aplicación se niega, independientemente de lo que pulse el usuario.</summary>
    Blocked
}

/// <summary>
/// Motivo del veredicto. Código, no frase: la interfaz lo traduce y decide cómo
/// redactarlo.
/// </summary>
public enum SafetyReason
{
    None,
    EmptyPath,
    InvalidPath,
    DriveRoot,
    SystemFolder,
    UserExclusion,
    ApplicationDataFolder,
    SuspiciousSegment,
    SystemFile,
    ReparsePoint,
    AttributesUnreadable
}

/// <param name="Detail">
/// Dato que acompaña al motivo: la carpeta protegida, el segmento sospechoso o
/// la exclusión concreta. Es una ruta, no texto traducible.
/// </param>
public sealed record SafetyVerdict(SafetyLevel Level, SafetyReason Reason, string? Detail = null)
{
    public static SafetyVerdict Ok { get; } = new(SafetyLevel.Allowed, SafetyReason.None);
}

/// <summary>
/// Última línea de defensa antes de mover o borrar. Se consulta siempre, incluso
/// si el usuario eligió la ruta a mano.
/// </summary>
public sealed class PathSafetyGuard
{
    private readonly string[] _blockedRoots;
    private readonly string[] _warnRoots;
    private readonly string[] _warnSegments;
    private readonly List<string> _userExclusions = [];

    public PathSafetyGuard(
        IEnumerable<string>? blockedRoots = null,
        IEnumerable<string>? warnRoots = null,
        IEnumerable<string>? warnSegments = null)
    {
        _blockedRoots = Normalize(blockedRoots);
        _warnRoots = Normalize(warnRoots);
        _warnSegments = (warnSegments ?? DefaultWarnSegments).ToArray();
    }

    private static readonly string[] DefaultWarnSegments =
    [
        "AppData", ".git", "node_modules", "WinSxS", "$Recycle.Bin", "OneDriveTemp"
    ];

    /// <summary>Construye el guardián con las carpetas críticas reales de este Windows.</summary>
    public static PathSafetyGuard CreateForWindows()
    {
        var blocked = new List<string>();
        var warn = new List<string>();

        void AddBlocked(Environment.SpecialFolder folder)
        {
            var p = Environment.GetFolderPath(folder);
            if (!string.IsNullOrWhiteSpace(p)) blocked.Add(p);
        }

        AddBlocked(Environment.SpecialFolder.Windows);
        AddBlocked(Environment.SpecialFolder.System);
        AddBlocked(Environment.SpecialFolder.SystemX86);
        AddBlocked(Environment.SpecialFolder.ProgramFiles);
        AddBlocked(Environment.SpecialFolder.ProgramFilesX86);
        AddBlocked(Environment.SpecialFolder.CommonApplicationData);

        var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        if (!string.IsNullOrWhiteSpace(systemDrive))
        {
            foreach (var name in new[]
                     {
                         "$Recycle.Bin", "System Volume Information", "Recovery",
                         "Boot", "PerfLogs", "$WinREAgent", "Config.Msi"
                     })
            {
                blocked.Add(Path.Combine(systemDrive, name));
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            blocked.Add(Path.Combine(programFiles, "WindowsApps"));
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            warn.Add(Path.Combine(userProfile, "AppData"));
        }

        return new PathSafetyGuard(blocked, warn);
    }

    /// <summary>Rutas que el usuario ha excluido a mano en Configuración.</summary>
    public void SetUserExclusions(IEnumerable<string> paths)
    {
        _userExclusions.Clear();
        _userExclusions.AddRange(Normalize(paths));
    }

    public IReadOnlyList<string> BlockedRoots => _blockedRoots;

    /// <summary>Veredicto para mover o borrar una ruta.</summary>
    public SafetyVerdict Evaluate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new SafetyVerdict(SafetyLevel.Blocked, SafetyReason.EmptyPath);

        string full;
        try
        {
            full = NormalizeOne(path);
        }
        catch (Exception)
        {
            return new SafetyVerdict(SafetyLevel.Blocked, SafetyReason.InvalidPath);
        }

        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) && string.Equals(TrimEnd(root), full, StringComparison.OrdinalIgnoreCase))
            return new SafetyVerdict(SafetyLevel.Blocked, SafetyReason.DriveRoot);

        foreach (var blockedRoot in _blockedRoots)
        {
            if (IsSameOrUnder(full, blockedRoot))
                return new SafetyVerdict(SafetyLevel.Blocked, SafetyReason.SystemFolder, blockedRoot);
        }

        foreach (var excluded in _userExclusions)
        {
            if (IsSameOrUnder(full, excluded))
                return new SafetyVerdict(SafetyLevel.Blocked, SafetyReason.UserExclusion, excluded);
        }

        foreach (var warnRoot in _warnRoots)
        {
            if (IsSameOrUnder(full, warnRoot))
                return new SafetyVerdict(SafetyLevel.Warning, SafetyReason.ApplicationDataFolder, warnRoot);
        }

        var segments = full.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            foreach (var warn in _warnSegments)
            {
                if (string.Equals(segment, warn, StringComparison.OrdinalIgnoreCase))
                    return new SafetyVerdict(SafetyLevel.Warning, SafetyReason.SuspiciousSegment, warn);
            }
        }

        return SafetyVerdict.Ok;
    }

    /// <summary>Veredicto reforzado: además de la ruta, mira los atributos del archivo.</summary>
    public SafetyVerdict EvaluateFile(string path)
    {
        var verdict = Evaluate(path);
        if (verdict.Level == SafetyLevel.Blocked) return verdict;

        try
        {
            var attributes = File.GetAttributes(path);
            if (attributes.HasFlag(FileAttributes.System))
                return new SafetyVerdict(SafetyLevel.Blocked, SafetyReason.SystemFile);

            if (attributes.HasFlag(FileAttributes.ReparsePoint))
                return new SafetyVerdict(SafetyLevel.Blocked, SafetyReason.ReparsePoint);
        }
        catch (Exception)
        {
            // No poder leer los atributos no debe habilitar la operación.
            return new SafetyVerdict(SafetyLevel.Warning, SafetyReason.AttributesUnreadable);
        }

        return verdict;
    }

    /// <summary>¿Merece la pena recorrer esta carpeta durante un análisis?</summary>
    public bool ShouldSkipDuringScan(string directoryPath)
    {
        string full;
        try
        {
            full = NormalizeOne(directoryPath);
        }
        catch (Exception)
        {
            return true;
        }

        foreach (var excluded in _userExclusions)
        {
            if (IsSameOrUnder(full, excluded)) return true;
        }

        var name = Path.GetFileName(full);
        return name is "$Recycle.Bin" or "System Volume Information";
    }

    internal static bool IsSameOrUnder(string candidate, string root)
    {
        if (string.IsNullOrEmpty(root)) return false;
        if (candidate.Length < root.Length) return false;
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
        return candidate.Length == root.Length || candidate[root.Length] == Path.DirectorySeparatorChar;
    }

    private static string[] Normalize(IEnumerable<string>? paths)
    {
        if (paths is null) return [];
        var result = new List<string>();
        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            try
            {
                result.Add(NormalizeOne(p));
            }
            catch (Exception)
            {
                // Una entrada corrupta en la configuración no debe tumbar el guardián.
            }
        }
        return [.. result.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    private static string NormalizeOne(string path) => TrimEnd(Path.GetFullPath(path));

    private static string TrimEnd(string path)
    {
        if (path.Length <= 1) return path;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        // "C:\" debe seguir siendo "C:", pero "/" en pruebas no puede quedar vacío.
        return trimmed.Length == 0 ? path : trimmed;
    }
}
