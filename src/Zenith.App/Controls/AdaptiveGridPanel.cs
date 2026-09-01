using System.Windows;
using System.Windows.Controls;

namespace Zenith.App.Controls;

/// <summary>
/// Rejilla que decide cuántas columnas caben según el ancho disponible y estira
/// los elementos para repartirlo. Es lo que hace que el panel funcione igual en
/// una ventana estrecha, en un portátil al 150 % y en un monitor de 32".
/// </summary>
public sealed class AdaptiveGridPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGridPanel),
        new FrameworkPropertyMetadata(240d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(AdaptiveGridPanel),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(AdaptiveGridPanel),
        new FrameworkPropertyMetadata(6, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    private int _columns = 1;
    private double _columnWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Children.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();
        if (visible.Count == 0) return new Size(0, 0);

        var available = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;

        _columns = Math.Max(1, (int)Math.Floor((available + Gap) / (MinItemWidth + Gap)));
        _columns = Math.Min(_columns, Math.Min(MaxColumns, visible.Count));
        _columnWidth = (available - Gap * (_columns - 1)) / _columns;

        var rowHeight = 0d;
        var totalHeight = 0d;
        var column = 0;

        foreach (var child in visible)
        {
            child.Measure(new Size(_columnWidth, double.PositiveInfinity));
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

            if (++column != _columns) continue;

            totalHeight += rowHeight + Gap;
            rowHeight = 0;
            column = 0;
        }

        if (column > 0) totalHeight += rowHeight + Gap;

        return new Size(available, Math.Max(0, totalHeight - Gap));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visible = Children.Cast<UIElement>().Where(c => c.Visibility != Visibility.Collapsed).ToList();

        var y = 0d;
        for (var start = 0; start < visible.Count; start += _columns)
        {
            var row = visible.Skip(start).Take(_columns).ToList();

            // Todas las tarjetas de una fila comparten altura: si no, la rejilla
            // parece rota en cuanto una tiene una línea de texto más.
            var rowHeight = row.Max(c => c.DesiredSize.Height);

            for (var i = 0; i < row.Count; i++)
            {
                row[i].Arrange(new Rect(i * (_columnWidth + Gap), y, _columnWidth, rowHeight));
            }

            y += rowHeight + Gap;
        }

        return finalSize;
    }
}
