using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Zenith.Core.Settings;

namespace Zenith.App.Services;

/// <summary>
/// Aplica el tema claro/oscuro y el color de acento de Windows. Escucha los
/// cambios del sistema para que, en modo "Sistema", la aplicación cambie con él
/// sin reiniciar.
/// </summary>
public sealed class ThemeService : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    private readonly ILogger<ThemeService> _logger;
    private ThemePreference _preference = ThemePreference.System;
    private bool _subscribed;

    public ThemeService(ILogger<ThemeService> logger) => _logger = logger;

    public bool IsDarkTheme { get; private set; } = true;

    public event EventHandler? ThemeChanged;

    public void Apply(ThemePreference preference)
    {
        _preference = preference;
        ApplyEffective();

        if (_subscribed) return;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _subscribed = true;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
        if (_preference != ThemePreference.System) return;

        // El evento llega en un hilo del sistema.
        Application.Current?.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ApplyEffective));
    }

    private void ApplyEffective()
    {
        var dark = _preference switch
        {
            ThemePreference.Light => false,
            ThemePreference.Dark => true,
            _ => IsSystemUsingDarkTheme()
        };

        IsDarkTheme = dark;

        try
        {
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se ha podido aplicar el tema base");
        }

        SwapPalette(dark);
        ApplySystemAccent(dark);

        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void SwapPalette(bool dark)
    {
        var application = Application.Current;
        if (application is null) return;

        var source = new Uri(
            dark ? "pack://application:,,,/Resources/Palette.Dark.xaml"
                 : "pack://application:,,,/Resources/Palette.Light.xaml",
            UriKind.Absolute);

        var dictionaries = application.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d =>
            d.Source is not null && d.Source.OriginalString.Contains("Palette.", StringComparison.Ordinal));

        var replacement = new ResourceDictionary { Source = source };

        if (existing is null)
        {
            dictionaries.Insert(0, replacement);
            return;
        }

        // Se sustituye en el mismo índice para no alterar la precedencia.
        var index = dictionaries.IndexOf(existing);
        dictionaries[index] = replacement;
    }

    /// <summary>
    /// Toma el acento del sistema y lo corrige en luminosidad si hace falta: el
    /// usuario puede tener elegido un color que sobre negro (o sobre blanco) sea
    /// ilegible, y el contraste no es negociable.
    /// </summary>
    private void ApplySystemAccent(bool dark)
    {
        var accent = TryReadSystemAccent();
        if (accent is not { } color) return;

        try
        {
            var adjusted = EnsureReadable(color, dark);

            var resources = Application.Current?.Resources;
            if (resources is null) return;

            resources["ZenithAccentColor"] = adjusted;
            resources["ZenithAccentBrush"] = Frozen(adjusted);
            resources["ZenithAccentHoverBrush"] = Frozen(Shift(adjusted, dark ? 0.10 : -0.08));
            resources["ZenithAccentPressedBrush"] = Frozen(Shift(adjusted, dark ? -0.08 : -0.16));
            resources["ZenithAccentSubtleBrush"] = Frozen(Color.FromArgb(0x24, adjusted.R, adjusted.G, adjusted.B));
            resources["ZenithOnAccentBrush"] = Frozen(Luminance(adjusted) > 0.55 ? Colors.Black : Colors.White);
            resources["ZenithChartStrokeBrush"] = Frozen(adjusted);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se ha podido aplicar el color de acento del sistema");
        }
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private Color? TryReadSystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(DwmKey);
            if (key?.GetValue("AccentColor") is not int raw) return null;

            // El valor viene en ABGR.
            var bytes = BitConverter.GetBytes(raw);
            return Color.FromRgb(bytes[0], bytes[1], bytes[2]);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se ha podido leer el acento del sistema");
            return null;
        }
    }

    private static bool IsSystemUsingDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static double Luminance(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255;

    private static Color EnsureReadable(Color color, bool dark)
    {
        var luminance = Luminance(color);

        // En oscuro exigimos un acento suficientemente claro y viceversa.
        if (dark && luminance < 0.42) return Shift(color, 0.42 - luminance + 0.05);
        if (!dark && luminance > 0.55) return Shift(color, 0.55 - luminance - 0.05);
        return color;
    }

    private static Color Shift(Color color, double amount)
    {
        static byte Blend(byte channel, double amount) => amount >= 0
            ? (byte)Math.Clamp(channel + (255 - channel) * amount, 0, 255)
            : (byte)Math.Clamp(channel * (1 + amount), 0, 255);

        return Color.FromRgb(Blend(color.R, amount), Blend(color.G, amount), Blend(color.B, amount));
    }

    public void Dispose()
    {
        if (!_subscribed) return;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _subscribed = false;
    }
}
