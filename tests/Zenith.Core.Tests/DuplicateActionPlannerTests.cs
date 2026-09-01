using Zenith.Core.Abstractions;
using Zenith.Core.Duplicates;
using Zenith.Core.Safety;

namespace Zenith.Core.Tests;

public sealed class DuplicateActionPlannerTests
{
    private static DuplicateGroup GroupOf(params string[] paths) => new(
        1, 1024, [.. paths.Select(p => new DuplicateFile(p, 1024, DateTime.UtcNow))]);

    [Fact]
    public void Impide_borrar_todas_las_copias_de_un_grupo()
    {
        using var workspace = new TempWorkspace();
        var a = workspace.CreateFile("a.txt", "contenido");
        var b = workspace.CreateFile("b.txt", "contenido");

        var planner = new DuplicateActionPlanner(new PathSafetyGuard());
        var plan = planner.Build([GroupOf(a, b)], new HashSet<string> { a, b }, FileActionKind.RecycleBin, null);

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Blockers, b => b.Kind == PlanBlockerKind.WholeGroupSelected && b.GroupIndex == 1);
    }

    [Fact]
    public void Permite_borrar_dejando_una_copia()
    {
        using var workspace = new TempWorkspace();
        var a = workspace.CreateFile("a.txt", "contenido");
        var b = workspace.CreateFile("b.txt", "contenido");

        var planner = new DuplicateActionPlanner(new PathSafetyGuard());
        var plan = planner.Build([GroupOf(a, b)], new HashSet<string> { b }, FileActionKind.RecycleBin, null);

        Assert.True(plan.CanExecute);
        Assert.Single(plan.Included);
        Assert.Equal(b, plan.Included[0].Path);
    }

    [Fact]
    public void Descarta_los_archivos_de_carpetas_protegidas()
    {
        using var workspace = new TempWorkspace();
        var protectedRoot = Path.Combine(workspace.Root, "Sistema");
        var safe = workspace.CreateFile("datos/a.txt", "contenido");
        var unsafePath = workspace.CreateFile("Sistema/b.txt", "contenido");

        var planner = new DuplicateActionPlanner(new PathSafetyGuard([protectedRoot]));
        var plan = planner.Build(
            [GroupOf(safe, unsafePath)], new HashSet<string> { unsafePath }, FileActionKind.RecycleBin, null);

        Assert.Single(plan.Rejected);
        Assert.False(plan.CanExecute);
    }

    [Fact]
    public void Mover_exige_carpeta_de_destino()
    {
        using var workspace = new TempWorkspace();
        var a = workspace.CreateFile("a.txt", "contenido");
        var b = workspace.CreateFile("b.txt", "contenido");

        var planner = new DuplicateActionPlanner(new PathSafetyGuard());
        var plan = planner.Build([GroupOf(a, b)], new HashSet<string> { b }, FileActionKind.Move, null);

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Blockers, b => b.Kind == PlanBlockerKind.MissingDestination);
    }

    [Fact]
    public void La_sugerencia_conserva_la_copia_de_ruta_mas_corta()
    {
        using var workspace = new TempWorkspace();
        var shallow = workspace.CreateFile("foto.jpg", "contenido");
        var deep = workspace.CreateFile("copias/2024/backup/foto.jpg", "contenido");

        var planner = new DuplicateActionPlanner(new PathSafetyGuard());
        var selection = planner.SuggestSelection([GroupOf(shallow, deep)]);

        Assert.Contains(deep, selection);
        Assert.DoesNotContain(shallow, selection);
    }
}
