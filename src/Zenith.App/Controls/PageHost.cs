using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Zenith.App.Controls;

/// <summary>
/// Contenedor de página con transición. Windows 11 no cambia de pantalla de
/// golpe: entra con un desplazamiento corto hacia arriba y una atenuación.
/// </summary>
public sealed class PageHost : ContentControl
{
    private readonly TranslateTransform _translate = new();

    public PageHost()
    {
        RenderTransform = _translate;
        RenderTransformOrigin = new Point(0.5, 0.5);
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);

        if (newContent is null) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        _translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation
        {
            From = 12,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(240),
            EasingFunction = ease
        });

        BeginAnimation(OpacityProperty, new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = ease
        });
    }
}
