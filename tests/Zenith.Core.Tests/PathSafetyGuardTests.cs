using Zenith.Core.Safety;

namespace Zenith.Core.Tests;

public sealed class PathSafetyGuardTests
{
    [Fact]
    public void Bloquea_las_rutas_dentro_de_una_carpeta_protegida()
    {
        using var workspace = new TempWorkspace();
        var protectedRoot = Path.Combine(workspace.Root, "Windows");
        var guard = new PathSafetyGuard([protectedRoot]);

        var verdict = guard.Evaluate(Path.Combine(protectedRoot, "System32", "kernel32.dll"));

        Assert.Equal(SafetyLevel.Blocked, verdict.Level);
    }

    [Fact]
    public void No_bloquea_una_carpeta_que_solo_comparte_prefijo()
    {
        using var workspace = new TempWorkspace();
        var protectedRoot = Path.Combine(workspace.Root, "Windows");
        var guard = new PathSafetyGuard([protectedRoot]);

        // "WindowsFotos" NO está dentro de "Windows": la comparación es por segmento.
        var verdict = guard.Evaluate(Path.Combine(workspace.Root, "WindowsFotos", "foto.jpg"));

        Assert.Equal(SafetyLevel.Allowed, verdict.Level);
    }

    [Fact]
    public void Bloquea_la_raiz_de_la_unidad()
    {
        var guard = new PathSafetyGuard();
        var root = Path.GetPathRoot(Path.GetTempPath());

        Assert.Equal(SafetyLevel.Blocked, guard.Evaluate(root).Level);
    }

    [Fact]
    public void Bloquea_una_ruta_vacia_o_invalida()
    {
        var guard = new PathSafetyGuard();

        Assert.Equal(SafetyLevel.Blocked, guard.Evaluate(null).Level);
        Assert.Equal(SafetyLevel.Blocked, guard.Evaluate("   ").Level);
    }

    [Fact]
    public void Respeta_las_exclusiones_del_usuario()
    {
        using var workspace = new TempWorkspace();
        var excluded = Path.Combine(workspace.Root, "Trabajo");
        Directory.CreateDirectory(excluded);

        var guard = new PathSafetyGuard();
        guard.SetUserExclusions([excluded]);

        Assert.Equal(SafetyLevel.Blocked, guard.Evaluate(Path.Combine(excluded, "informe.docx")).Level);
        Assert.True(guard.ShouldSkipDuringScan(Path.Combine(excluded, "subcarpeta")));
    }

    [Fact]
    public void Avisa_cuando_la_ruta_contiene_un_segmento_sospechoso()
    {
        using var workspace = new TempWorkspace();
        var guard = new PathSafetyGuard();

        var verdict = guard.Evaluate(Path.Combine(workspace.Root, "proyecto", ".git", "objects", "ab", "cdef"));

        Assert.Equal(SafetyLevel.Warning, verdict.Level);
    }

    [Fact]
    public void Bloquea_un_archivo_que_no_existe_al_comprobar_atributos()
    {
        using var workspace = new TempWorkspace();
        var guard = new PathSafetyGuard();

        // No se puede leer: preferimos advertir antes que dar vía libre.
        var verdict = guard.EvaluateFile(Path.Combine(workspace.Root, "fantasma.txt"));

        Assert.NotEqual(SafetyLevel.Allowed, verdict.Level);
    }
}
