using Zenith.Core.Storage;

namespace Zenith.Core.Tests;

public sealed class FileCategoriesTests
{
    [Theory]
    [InlineData(".jpg", FileCategory.Images)]
    [InlineData(".MP4", FileCategory.Video)]
    [InlineData(".pdf", FileCategory.Documents)]
    [InlineData(".zip", FileCategory.Archives)]
    [InlineData(".desconocida", FileCategory.Other)]
    public void Clasifica_por_extension_sin_distinguir_mayusculas(string extension, FileCategory expected)
    {
        Assert.Equal(expected, FileCategories.FromExtension(extension));
    }

    [Fact]
    public void Devuelve_las_extensiones_de_una_categoria()
    {
        var images = FileCategories.ExtensionsFor(FileCategory.Images);

        Assert.Contains(".jpg", images);
        Assert.Contains(".png", images);
        Assert.DoesNotContain(".mp4", images);
    }

    [Fact]
    public void Toda_categoria_seleccionable_tiene_extensiones()
    {
        // Si una categoría del filtro no tuviera extensiones, marcarla dejaría la
        // búsqueda vacía sin que el usuario entendiera por qué.
        foreach (var category in FileCategories.Selectable)
        {
            Assert.NotEmpty(FileCategories.ExtensionsFor(category));
        }
    }

    [Fact]
    public void La_categoria_Otros_no_es_seleccionable()
    {
        // "Otros" es el cajón de sastre: no tiene extensiones propias.
        Assert.DoesNotContain(FileCategory.Other, FileCategories.Selectable);
        Assert.Empty(FileCategories.ExtensionsFor(FileCategory.Other));
    }
}
