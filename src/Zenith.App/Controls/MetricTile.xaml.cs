using System.Windows;
using System.Windows.Controls;

namespace Zenith.App.Controls;

/// <summary>
/// Tarjeta de métrica del panel. Un único componente para CPU, RAM, GPU y
/// temperatura: la coherencia visual sale gratis y los cambios se hacen en un
/// solo sitio.
/// </summary>
public partial class MetricTile : UserControl
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(MetricTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(MetricTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(MetricTile), new PropertyMetadata("—"));

    public static readonly DependencyProperty SecondaryProperty = DependencyProperty.Register(
        nameof(Secondary), typeof(string), typeof(MetricTile), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(MetricTile), new PropertyMetadata(0d));

    public static readonly DependencyProperty HistoryProperty = DependencyProperty.Register(
        nameof(History), typeof(double[]), typeof(MetricTile), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowChartProperty = DependencyProperty.Register(
        nameof(ShowChart), typeof(bool), typeof(MetricTile), new PropertyMetadata(true));

    public static readonly DependencyProperty ShowBarProperty = DependencyProperty.Register(
        nameof(ShowBar), typeof(bool), typeof(MetricTile), new PropertyMetadata(false));

    public MetricTile() => InitializeComponent();

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Secondary
    {
        get => (string)GetValue(SecondaryProperty);
        set => SetValue(SecondaryProperty, value);
    }

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public double[]? History
    {
        get => (double[]?)GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    public bool ShowChart
    {
        get => (bool)GetValue(ShowChartProperty);
        set => SetValue(ShowChartProperty, value);
    }

    public bool ShowBar
    {
        get => (bool)GetValue(ShowBarProperty);
        set => SetValue(ShowBarProperty, value);
    }

    /// <summary>Lo que anuncia un lector de pantalla: etiqueta, valor y contexto.</summary>
    public string AccessibleDescription => $"{Label}: {Value}. {Secondary}";
}
