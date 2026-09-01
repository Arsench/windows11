using System.Windows;
using System.Windows.Media;

namespace Zenith.App.Controls;

/// <summary>
/// Gráfico de área minimalista dibujado a mano. Sin librerías de terceros: es
/// una sola geometría por fotograma, así que actualizar cada segundo no se nota
/// en el consumo de CPU.
/// </summary>
public sealed class Sparkline : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(100d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(Sparkline),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Sparkline),
        new FrameworkPropertyMetadata(1.6d, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Número de muestras que caben en el eje X aunque aún no existan todas.</summary>
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(Sparkline),
        new FrameworkPropertyMetadata(90, FrameworkPropertyMetadataOptions.AffectsRender));

    public double[]? Values
    {
        get => (double[]?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public int Capacity
    {
        get => (int)GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    // El gráfico es decorativo: la cifra que lo acompaña es la que da el dato,
    // así que no participa en el hit-testing ni en el árbol de accesibilidad.
    public Sparkline() => IsHitTestVisible = false;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var values = Values;
        var width = ActualWidth;
        var height = ActualHeight;

        if (values is null || values.Length < 2 || width <= 1 || height <= 1) return;

        var maximum = Maximum > 0 ? Maximum : 1;
        var capacity = Math.Max(values.Length, Capacity);
        var step = width / (capacity - 1);

        // La serie se ancla a la derecha: lo más reciente siempre en el borde.
        var offset = width - (values.Length - 1) * step;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var first = new Point(offset, YFor(values[0], maximum, height));
            context.BeginFigure(first, isFilled: false, isClosed: false);

            for (var i = 1; i < values.Length; i++)
            {
                context.LineTo(new Point(offset + i * step, YFor(values[i], maximum, height)), true, false);
            }
        }
        geometry.Freeze();

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var context = area.Open())
            {
                context.BeginFigure(new Point(offset, height), isFilled: true, isClosed: true);
                for (var i = 0; i < values.Length; i++)
                {
                    context.LineTo(new Point(offset + i * step, YFor(values[i], maximum, height)), true, false);
                }
                context.LineTo(new Point(offset + (values.Length - 1) * step, height), true, false);
            }
            area.Freeze();

            drawingContext.DrawGeometry(fill, null, area);
        }

        if (Stroke is { } stroke)
        {
            var pen = new Pen(stroke, StrokeThickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            pen.Freeze();
            drawingContext.DrawGeometry(null, pen, geometry);
        }
    }

    private static double YFor(double value, double maximum, double height)
    {
        // Un margen superior de 2 px evita que el pico toque el borde del control.
        var normalized = Math.Clamp(value / maximum, 0, 1);
        return 2 + (height - 4) * (1 - normalized);
    }
}
