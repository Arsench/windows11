using System.Windows;
using System.Windows.Media;

namespace Zenith.App.Controls;

/// <summary>Barra de ocupación fina y redondeada, dibujada directamente.</summary>
public sealed class UsageBar : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(UsageBar),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(UsageBar),
        new FrameworkPropertyMetadata(Brushes.Gainsboro, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(UsageBar),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public Brush? TrackBrush
    {
        get => (Brush?)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public Brush? ValueBrush
    {
        get => (Brush?)GetValue(ValueBrushProperty);
        set => SetValue(ValueBrushProperty, value);
    }

    public UsageBar()
    {
        Height = 6;
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var radius = height / 2;

        if (TrackBrush is { } track)
        {
            drawingContext.DrawRoundedRectangle(track, null, new Rect(0, 0, width, height), radius, radius);
        }

        var value = Math.Clamp(Value, 0, 100);
        if (value <= 0 || ValueBrush is null) return;

        // Mínimo del ancho de la propia altura: un 0,4 % debe seguir viéndose.
        var filled = Math.Max(height, width * value / 100);
        drawingContext.DrawRoundedRectangle(ValueBrush, null, new Rect(0, 0, filled, height), radius, radius);
    }
}
