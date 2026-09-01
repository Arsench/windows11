using System.Windows.Data;
using System.Windows.Markup;

namespace Zenith.App.Localization;

/// <summary>
/// Extensión de marcado para textos traducidos: <c>Text="{loc:T NavDashboard}"</c>.
///
/// Devuelve un enlace al indexador de <see cref="Loc"/> en lugar de una cadena
/// fija, que es lo que hace que el idioma se pueda cambiar en caliente.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Las claves no llevan puntos a propósito: el analizador de rutas de
        // enlace de WPF los interpretaría como navegación de propiedades.
        var binding = new Binding("[" + Key + "]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}
