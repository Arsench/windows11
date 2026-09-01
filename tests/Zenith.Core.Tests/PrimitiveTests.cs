using System.Globalization;
using Zenith.Core.Monitoring;
using Zenith.Core.Primitives;

namespace Zenith.Core.Tests;

public sealed class PrimitiveTests
{
    [Fact]
    public void Una_metrica_no_disponible_no_expone_valor()
    {
        var metric = Metric<double>.NotSupported(MetricDetail.NotReportedByDevice);

        Assert.False(metric.HasValue);
        Assert.Null(metric.ValueOrNull);
        Assert.Equal(0, metric.ValueOr(0));
        Assert.Throws<InvalidOperationException>(() => metric.Value);
        Assert.Equal(MetricDetail.NotReportedByDevice, metric.Detail);
    }

    [Fact]
    public void Una_metrica_disponible_conserva_el_valor()
    {
        var metric = Metric<double>.Available(42.5);

        Assert.True(metric.HasValue);
        Assert.Equal(42.5, metric.Value);
        Assert.Equal(MetricStatus.Available, metric.Status);
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(512L, "512 B")]
    [InlineData(1024L, "1,0 KB")]
    [InlineData(1610612736L, "1,5 GB")]
    public void Los_tamanos_se_formatean_en_base_1024(long bytes, string expected)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("es-ES");
        try
        {
            Assert.Equal(expected, ByteSize.Format(bytes));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void El_historico_circular_conserva_las_ultimas_muestras()
    {
        var history = new MetricHistory(3);
        history.Add(1);
        history.Add(2);
        history.Add(3);
        history.Add(4);

        Assert.Equal([2d, 3d, 4d], history.ToArray());
        Assert.Equal(3, history.Count);
    }

    [Fact]
    public void El_historico_vacio_devuelve_una_serie_vacia()
    {
        var history = new MetricHistory(5);

        Assert.Empty(history.ToArray());
        Assert.Equal(0, history.Count);
    }
}
