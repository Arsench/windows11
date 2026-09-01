using Zenith.Core.Safety;

namespace Zenith.Core.Tests;

public sealed class PathSafetyGuardTests
{
    /// <summary>
    /// Base neutra para las pruebas de rutas.
    ///
    /// No se usa la carpeta temporal: en Windows cuelga de
    /// <c>…\AppData\Local\Temp</c>, y "AppData" es precisamente uno de los
    /// segmentos que el guardián marca como advertencia. Una prueba que use esa
    /// ruta mide el entorno, no el código. <see cref="PathSafetyGuard.Evaluate"/>
    /// solo analiza la cadena, así que la ruta no necesita existir.
    /// </summary>
    private static string Base { get; } = Path.Combine(
        Path.GetPathRoot(Path.GetTempPath()) ?? Path.DirectorySeparatorChar.ToString(),
        "zenith-guard-tests");

    [Fact]
    public void Bloquea_las_rutas_dentro_de_una_carpeta_protegida()
    {
        var protectedRoot = Path.Combine(Base, "Windows");
        var guard = new PathSafetyGuard([protectedRoot]);

        var verdict = guard.Evaluate(Path.Combine(protectedRoot, "System32", "kernel32.dll"));

        Assert.Equal(SafetyLevel.Blocked, verdict.Level);
        Assert.Equal(SafetyReason.SystemFolder, verdict.Reason);
    }

    [Fact]
    public void No_bloquea_una_carpeta_que_solo_comparte_prefijo()
    {
        var guard = new PathSafetyGuard([Path.Combine(Base, "Windows")]);

        // "WindowsFotos" NO está dentro de "Windows": la comparación es por segmento.
        var verdict = guard.Evaluate(Path.Combine(Base, "WindowsFotos", "foto.jpg"));

        Assert.Equal(SafetyLevel.Allowed, verdict.Level);
        Assert.Equal(SafetyReason.None, verdict.Reason);
    }

    [Fact]
    public void Bloquea_la_raiz_de_la_unidad()
    {
        var guard = new PathSafetyGuard();
        var root = Path.GetPathRoot(Path.GetTempPath());

        var verdict = guard.Evaluate(root);

        Assert.Equal(SafetyLevel.Blocked, verdict.Level);
        Assert.Equal(SafetyReason.DriveRoot, verdict.Reason);
    }

    [Fact]
    public void Bloquea_una_ruta_vacia_o_invalida()
    {
        var guard = new PathSafetyGuard();

        Assert.Equal(SafetyReason.EmptyPath, guard.Evaluate(null).Reason);
        Assert.Equal(SafetyReason.EmptyPath, guard.Evaluate("   ").Reason);
    }

    [Fact]
    public void Respeta_las_exclusiones_del_usuario()
    {
        var excluded = Path.Combine(Base, "Trabajo");
        var guard = new PathSafetyGuard();
        guard.SetUserExclusions([excluded]);

        var verdict = guard.Evaluate(Path.Combine(excluded, "informe.docx"));

        Assert.Equal(SafetyLevel.Blocked, verdict.Level);
        Assert.Equal(SafetyReason.UserExclusion, verdict.Reason);
        Assert.True(guard.ShouldSkipDuringScan(Path.Combine(excluded, "subcarpeta")));
    }

    [Fact]
    public void La_exclusion_del_usuario_pesa_mas_que_una_simple_advertencia()
    {
        // Bloqueado gana a advertencia: el orden de comprobación importa.
        var excluded = Path.Combine(Base, "AppData");
        var guard = new PathSafetyGuard();
        guard.SetUserExclusions([excluded]);

        var verdict = guard.Evaluate(Path.Combine(excluded, "algo.txt"));

        Assert.Equal(SafetyReason.UserExclusion, verdict.Reason);
    }

    [Fact]
    public void Avisa_cuando_la_ruta_contiene_un_segmento_sospechoso()
    {
        var guard = new PathSafetyGuard();

        var verdict = guard.Evaluate(Path.Combine(Base, "proyecto", ".git", "objects", "ab", "cdef"));

        Assert.Equal(SafetyLevel.Warning, verdict.Level);
        Assert.Equal(SafetyReason.SuspiciousSegment, verdict.Reason);
        Assert.Equal(".git", verdict.Detail);
    }

    [Fact]
    public void Avisa_de_las_rutas_dentro_de_AppData()
    {
        // Comportamiento real y buscado: en Windows casi todo lo temporal cuelga
        // de AppData, y ahí no suele haber contenido personal del usuario.
        var guard = new PathSafetyGuard();

        var verdict = guard.Evaluate(Path.Combine(Base, "AppData", "Local", "Temp", "algo.tmp"));

        Assert.Equal(SafetyLevel.Warning, verdict.Level);
        Assert.Equal(SafetyReason.SuspiciousSegment, verdict.Reason);
        Assert.Equal("AppData", verdict.Detail);
    }

    [Fact]
    public void Un_archivo_que_no_existe_no_se_da_por_seguro()
    {
        var guard = new PathSafetyGuard();

        // No se pueden leer sus atributos: preferimos advertir antes que dar vía libre.
        var verdict = guard.EvaluateFile(Path.Combine(Base, "fantasma.txt"));

        Assert.NotEqual(SafetyLevel.Allowed, verdict.Level);
        Assert.Equal(SafetyReason.AttributesUnreadable, verdict.Reason);
    }
}
