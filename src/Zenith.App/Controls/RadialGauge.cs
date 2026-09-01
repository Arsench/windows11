using System.Windows;
using System.Windows.Media;

namespace Zenith.App.Controls;

/// <summary>
/// Anillo de progreso. Se usa para el llenado de las unidades: comunica
/// "cuánto queda" de un vistazo mejor que una barra.
/// </summary>
public sealed class RadialGauge : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RadialGauge),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(RadialGauge),
        new FrameworkPropertyMetadata(Brushes.Gainsboro, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValueBrushProperty = DependencyProperty.Register(
        nameof(ValueBrush), typeof(Brush), typeof(RadialGauge),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(RadialGauge),
        new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsRender));

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

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    public RadialGauge() => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= RingThickness * 2) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var radius = (size - RingThickness) / 2;

        if (TrackBrush is { } track)
        {
            var trackPen = new Pen(track, RingThickness);
            trackPen.Freeze();
            drawingContext.DrawEllipse(null, trackPen, center, radius, radius);
        }

        var value = Math.Clamp(Value, 0, 100);
        if (value <= 0 || ValueBrush is null) return;

        var pen = new Pen(ValueBrush, RingThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();

        // Un círculo completo no se puede dibujar con un solo ArcSegment.
        if (value >= 99.95)
        {
            drawingContext.DrawEllipse(null, pen, center, radius, radius);
            return;
        }

        var angle = value / 100 * 2 * Math.PI;
        var start = new Point(center.X, center.Y - radius);
        var end = new Point(
            center.X + radius * Math.Sin(angle),
            center.Y - radius * Math.Cos(angle));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, angle > Math.PI, SweepDirection.Clockwise, true, false);
        }
        geometry.Freeze();

        drawingContext.DrawGeometry(null, pen, geometry);
    }
}
