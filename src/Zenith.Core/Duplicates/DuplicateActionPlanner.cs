using Zenith.Core.Abstractions;
using Zenith.Core.Safety;

namespace Zenith.Core.Duplicates;

public sealed record PlannedFile(string Path, long SizeBytes, SafetyVerdict Verdict);

public sealed record ActionPlan(
    FileActionKind Kind,
    string? DestinationFolder,
    IReadOnlyList<PlannedFile> Included,
    IReadOnlyList<PlannedFile> Rejected,
    IReadOnlyList<string> Blockers)
{
    public long TotalBytes => Included.Sum(f => f.SizeBytes);

    public bool HasWarnings => Included.Any(f => f.Verdict.Level == SafetyLevel.Warning);

    /// <summary>Solo se puede ejecutar si hay algo que hacer y ningún impedimento estructural.</summary>
    public bool CanExecute => Blockers.Count == 0 && Included.Count > 0;
}

/// <summary>
/// Convierte "lo que el usuario ha marcado" en "lo que la aplicación está
/// dispuesta a hacer". Aquí viven las dos reglas irrenunciables: nunca se
/// eliminan todas las copias de un grupo y nunca se toca una ruta protegida.
/// </summary>
public sealed class DuplicateActionPlanner(PathSafetyGuard safety)
{
    public ActionPlan Build(
        IReadOnlyList<DuplicateGroup> groups,
        ISet<string> selectedPaths,
        FileActionKind kind,
        string? destinationFolder)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(selectedPaths);

        var included = new List<PlannedFile>();
        var rejected = new List<PlannedFile>();
        var blockers = new List<string>();

        foreach (var group in groups)
        {
            var selectedInGroup = group.Files.Where(f => selectedPaths.Contains(f.Path)).ToList();
            if (selectedInGroup.Count == 0) continue;

            if (selectedInGroup.Count >= group.Files.Count)
            {
                blockers.Add($"En el grupo {group.Index:00} has marcado todas las copias. Debes conservar al menos una.");
                continue;
            }

            foreach (var file in selectedInGroup)
            {
                var verdict = safety.EvaluateFile(file.Path);
                var planned = new PlannedFile(file.Path, file.SizeBytes, verdict);
                if (verdict.Level == SafetyLevel.Blocked) rejected.Add(planned);
                else included.Add(planned);
            }
        }

        if (kind == FileActionKind.Move)
        {
            if (string.IsNullOrWhiteSpace(destinationFolder))
            {
                blockers.Add("Elige una carpeta de destino.");
            }
            else
            {
                var destinationVerdict = safety.Evaluate(destinationFolder);
                if (destinationVerdict.Level == SafetyLevel.Blocked)
                    blockers.Add($"La carpeta de destino no es válida: {destinationVerdict.Reason}");
            }
        }

        if (included.Count == 0 && blockers.Count == 0 && rejected.Count > 0)
            blockers.Add("Ninguno de los archivos marcados se puede tocar de forma segura.");

        return new ActionPlan(kind, destinationFolder, included, rejected, blockers);
    }

    /// <summary>
    /// Sugerencia automática: conserva la copia de la ruta más corta (suele ser
    /// el original) y marca el resto. Nunca marca nada bloqueado.
    /// </summary>
    public HashSet<string> SuggestSelection(IReadOnlyList<DuplicateGroup> groups)
    {
        var selection = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            var ordered = group.Files
                .OrderBy(f => f.Path.Count(c => c == Path.DirectorySeparatorChar))
                .ThenBy(f => f.Path.Length)
                .ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in ordered.Skip(1))
            {
                if (safety.Evaluate(file.Path).Level != SafetyLevel.Blocked) selection.Add(file.Path);
            }
        }

        return selection;
    }
}
