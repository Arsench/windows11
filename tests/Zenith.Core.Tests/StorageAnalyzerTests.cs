using Zenith.Core.Safety;
using Zenith.Core.Storage;

namespace Zenith.Core.Tests;

public sealed class StorageAnalyzerTests
{
    private static StorageAnalyzer CreateAnalyzer() => new(new PathSafetyGuard());

    [Fact]
    public async Task Suma_el_tamano_de_todo_el_arbol()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateBinaryFile("videos/pelicula.mp4", new byte[3000]);
        workspace.CreateBinaryFile("videos/2024/corto.mp4", new byte[1000]);
        workspace.CreateBinaryFile("documentos/informe.pdf", new byte[500]);

        var result = await CreateAnalyzer().AnalyzeAsync(workspace.Root);

        Assert.NotNull(result.Root);
        Assert.Equal(4500, result.TotalBytes);
        Assert.Equal(3, result.FileCount);
    }

    [Fact]
    public async Task Ordena_las_carpetas_de_mayor_a_menor()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateBinaryFile("pequena/a.bin", new byte[100]);
        workspace.CreateBinaryFile("grande/b.bin", new byte[9000]);

        var result = await CreateAnalyzer().AnalyzeAsync(workspace.Root);

        Assert.Equal("grande", result.Root!.Children[0].Name);
        Assert.Equal("pequena", result.Root.Children[1].Name);
    }

    [Fact]
    public async Task Agrupa_por_categoria_de_archivo()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateBinaryFile("a.mp4", new byte[2000]);
        workspace.CreateBinaryFile("b.jpg", new byte[500]);

        var result = await CreateAnalyzer().AnalyzeAsync(workspace.Root);

        Assert.Equal(FileCategory.Video, result.Categories[0].Category);
        Assert.Equal(2000, result.Categories[0].SizeBytes);
    }

    [Fact]
    public async Task Lista_los_archivos_mas_grandes()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateBinaryFile("mediano.bin", new byte[2000]);
        workspace.CreateBinaryFile("enorme.bin", new byte[9000]);
        workspace.CreateBinaryFile("minusculo.bin", new byte[10]);

        var result = await CreateAnalyzer().AnalyzeAsync(workspace.Root);

        Assert.Equal("enorme.bin", result.LargestFiles[0].FileName);
        Assert.Equal(9000, result.LargestFiles[0].SizeBytes);
    }

    [Fact]
    public async Task Una_carpeta_vacia_devuelve_cero_sin_errores()
    {
        using var workspace = new TempWorkspace();

        var result = await CreateAnalyzer().AnalyzeAsync(workspace.Root);

        Assert.Equal(0, result.TotalBytes);
        Assert.Empty(result.Errors);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task La_cancelacion_marca_el_resultado()
    {
        using var workspace = new TempWorkspace();
        workspace.CreateBinaryFile("a.bin", new byte[100]);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var result = await CreateAnalyzer().AnalyzeAsync(workspace.Root, null, cancellation.Token);

        Assert.True(result.WasCancelled);
    }

    [Fact]
    public async Task Una_ruta_inexistente_se_registra_como_error()
    {
        using var workspace = new TempWorkspace();
        var missing = Path.Combine(workspace.Root, "no-existe");

        var result = await CreateAnalyzer().AnalyzeAsync(missing);

        Assert.NotEmpty(result.Errors);
        Assert.Equal(0, result.TotalBytes);
    }
}
