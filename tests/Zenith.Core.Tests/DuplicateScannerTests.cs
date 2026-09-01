using Zenith.Core.Duplicates;
using Zenith.Core.Safety;

namespace Zenith.Core.Tests;

public sealed class DuplicateScannerTests
{
    private static DuplicateScanner CreateScanner() => new(new PathSafetyGuard());

    private static DuplicateScanOptions OptionsFor(params string[] roots) => new()
    {
        Roots = roots,
        MinFileSizeBytes = 0,
        VerifyByteByByte = true
    };

    [Fact]
    public async Task Agrupa_archivos_con_el_mismo_contenido_y_distinto_nombre()
    {
        using var workspace = new TempWorkspace();
        var content = new string('a', 5000);
        workspace.CreateFile("a/foto-vacaciones.jpg", content);
        workspace.CreateFile("b/copia-de-seguridad.jpg", content);

        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root));

        Assert.Single(result.Groups);
        Assert.Equal(2, result.Groups[0].Files.Count);
        Assert.Equal(1, result.Groups[0].RedundantCount);
    }

    [Fact]
    public async Task No_agrupa_archivos_con_el_mismo_nombre_y_distinto_contenido()
    {
        using var workspace = new TempWorkspace();
        // Mismo nombre y mismo tamaño: solo el contenido los distingue.
        workspace.CreateFile("a/informe.txt", new string('a', 4096));
        workspace.CreateFile("b/informe.txt", new string('b', 4096));

        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root));

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Distingue_archivos_grandes_que_solo_difieren_al_final()
    {
        using var workspace = new TempWorkspace();

        var baseline = new byte[512 * 1024];
        Random.Shared.NextBytes(baseline);

        var variant = (byte[])baseline.Clone();
        variant[^1] = (byte)(variant[^1] ^ 0xFF);

        workspace.CreateBinaryFile("a/video.bin", baseline);
        workspace.CreateBinaryFile("b/video.bin", variant);

        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root));

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Con_las_opciones_por_defecto_encuentra_duplicados_pequenos()
    {
        // El caso que falló en la vida real: dos ficheros de texto de prueba, de
        // pocos bytes. Con un mínimo distinto de cero se descartaban en silencio.
        using var workspace = new TempWorkspace();
        workspace.CreateFile("a/notas.txt", "hola mundo");
        workspace.CreateFile("b/copia.txt", "hola mundo");

        var options = new DuplicateScanOptions { Roots = [workspace.Root] };
        var result = await CreateScanner().ScanAsync(options);

        Assert.Equal(0, options.MinFileSizeBytes);
        Assert.Single(result.Groups);
        Assert.Equal(2, result.Groups[0].Files.Count);
    }

    [Fact]
    public async Task El_filtro_de_tipo_solo_compara_las_extensiones_indicadas()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("a/nota.txt", "mismo texto");
        workspace.CreateFile("b/nota.txt", "mismo texto");
        workspace.CreateBinaryFile("a/foto.jpg", new byte[] { 1, 2, 3, 4, 5 });
        workspace.CreateBinaryFile("b/foto.jpg", new byte[] { 1, 2, 3, 4, 5 });

        var options = OptionsFor(workspace.Root) with { ExtensionFilter = [".jpg"] };
        var result = await CreateScanner().ScanAsync(options);

        Assert.Single(result.Groups);
        Assert.All(result.Groups[0].Files, f => Assert.EndsWith(".jpg", f.Path, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Informa_de_cuantos_archivos_descarto_el_filtro_de_tamano()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("a/nota.txt", "corto");
        workspace.CreateFile("b/nota.txt", "corto");

        var options = OptionsFor(workspace.Root) with { MinFileSizeBytes = 1024 };
        var result = await CreateScanner().ScanAsync(options);

        // Sin duplicados, pero la aplicación puede explicar por qué.
        Assert.Empty(result.Groups);
        Assert.Equal(2, result.FilesSkippedBySize);
        Assert.Equal(2, result.FilesSkippedByFilters);
    }

    [Fact]
    public async Task Informa_de_cuantos_archivos_descarto_el_filtro_de_tipo()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("a/nota.txt", "mismo texto");
        workspace.CreateFile("b/nota.txt", "mismo texto");

        var options = OptionsFor(workspace.Root) with { ExtensionFilter = [".jpg"] };
        var result = await CreateScanner().ScanAsync(options);

        Assert.Empty(result.Groups);
        Assert.Equal(2, result.FilesSkippedByType);
    }

    [Fact]
    public async Task Respeta_el_tamano_minimo()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("a/pequeno.txt", "hola");
        workspace.CreateFile("b/pequeno.txt", "hola");

        var options = OptionsFor(workspace.Root) with { MinFileSizeBytes = 1024 };
        var result = await CreateScanner().ScanAsync(options);

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Ignora_los_archivos_vacios_por_defecto()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateFile("a/vacio.txt", string.Empty);
        workspace.CreateFile("b/vacio.txt", string.Empty);

        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root));

        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task No_cuenta_dos_veces_la_misma_carpeta_indicada_dos_veces()
    {
        using var workspace = new TempWorkspace();
        var content = new string('z', 3000);
        workspace.CreateFile("solo/archivo.dat", content);

        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root, workspace.Root));

        Assert.Empty(result.Groups);
        Assert.Equal(1, result.FilesScanned);
    }

    [Fact]
    public async Task La_cancelacion_devuelve_un_resultado_marcado_y_no_lanza()
    {
        using var workspace = new TempWorkspace();
        for (var i = 0; i < 40; i++) workspace.CreateFile($"f{i}.dat", new string('x', 2048));

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root), null, cancellation.Token);

        Assert.True(result.WasCancelled);
        Assert.Empty(result.Groups);
    }

    [Fact]
    public async Task Informa_del_progreso_por_fases()
    {
        using var workspace = new TempWorkspace();
        // Por encima de 128 KB para que se recorran todas las fases, incluida
        // la huella completa (los archivos pequeños se resuelven en la parcial).
        var content = new string('q', 200_000);
        for (var i = 0; i < 6; i++) workspace.CreateFile($"dir{i}/archivo.dat", content);

        var progress = new CollectingProgress<DuplicateProgress>();

        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root), progress);

        Assert.Single(result.Groups);
        Assert.Equal(6, result.Groups[0].Files.Count);
        Assert.Equal(5, result.Groups[0].RedundantCount);

        var phases = progress.Reports.Select(r => r.Phase).ToList();
        Assert.Contains(DuplicatePhase.Enumerating, phases);
        Assert.Contains(DuplicatePhase.FullHashing, phases);
        Assert.Equal(DuplicatePhase.Completed, phases[^1]);
    }

    [Fact]
    public async Task Una_carpeta_vacia_no_produce_grupos_ni_errores()
    {
        using var workspace = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Root, "vacia"));

        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root));

        Assert.Empty(result.Groups);
        Assert.Empty(result.Errors);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task Una_ruta_inexistente_se_registra_como_error_sin_romper_el_analisis()
    {
        using var workspace = new TempWorkspace();
        var content = new string('m', 2048);
        workspace.CreateFile("a/uno.dat", content);
        workspace.CreateFile("b/dos.dat", content);

        var missing = Path.Combine(workspace.Root, "no-existe");
        var result = await CreateScanner().ScanAsync(OptionsFor(workspace.Root, missing));

        Assert.Single(result.Groups);
        Assert.NotEmpty(result.Errors);
    }
}
