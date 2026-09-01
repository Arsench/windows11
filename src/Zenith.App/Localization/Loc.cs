using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using Zenith.Core.Settings;

namespace Zenith.App.Localization;

/// <summary>
/// Catálogo de textos de la interfaz.
///
/// Es un singleton porque las extensiones de marcado de XAML no pueden pedir
/// dependencias al contenedor. El mismo objeto se registra además en el
/// contenedor, así que los ViewModels lo reciben por inyección como cualquier
/// otro servicio.
///
/// El indexador más <see cref="INotifyPropertyChanged"/> es lo que permite
/// cambiar de idioma <b>sin reiniciar</b>: al notificar el indexador, WPF
/// reevalúa todos los enlaces que apuntan a él.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    private static readonly ResourceManager Resources =
        new("Zenith.App.Localization.Strings", typeof(Loc).Assembly);

    public static Loc Instance { get; } = new();

    private Loc() { }

    public CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    public AppLanguage Language { get; private set; } = AppLanguage.System;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Se dispara después de que el catálogo ya sirva el idioma nuevo.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>Texto por clave. Si falta, devuelve la clave entre corchetes para que el hueco se vea.</summary>
    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;

        try
        {
            return Resources.GetString(key, Culture) ?? "[" + key + "]";
        }
        catch (MissingManifestResourceException)
        {
            return "[" + key + "]";
        }
    }

    /// <summary>Texto con formato: <c>Format("DuplicatesGroupTitle", 3)</c>.</summary>
    public string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    public void Apply(AppLanguage language)
    {
        var culture = Resolve(language);
        if (Language == language && Culture.Name == culture.Name) return;

        Language = language;
        Culture = culture;

        // Los números, las fechas y los tamaños siguen al idioma elegido.
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        // Binding.IndexerName ("Item[]") refresca todos los enlaces al indexador.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Traduce la preferencia a una cultura concreta. En modo Sistema seguimos a
    /// Windows y recaemos en inglés si su idioma no está entre los traducidos.
    /// </summary>
    private static CultureInfo Resolve(AppLanguage language) => language switch
    {
        AppLanguage.Spanish => new CultureInfo("es"),
        AppLanguage.English => new CultureInfo("en"),
        _ => IsSupported(CultureInfo.InstalledUICulture) ? CultureInfo.InstalledUICulture : new CultureInfo("en")
    };

    private static bool IsSupported(CultureInfo culture)
    {
        var name = culture.TwoLetterISOLanguageName;
        return name is "es" or "en";
    }
}
