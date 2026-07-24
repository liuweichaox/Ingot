using System.Text.Json;
using Ingot.Platform.Infrastructure.Services;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class PrometheusTextParserTests
{
    [Fact]
    public void Parse_KeepsHelpAndTypeWithTheirMetricFamily()
    {
        const string metrics = """
            # HELP event_outbox_backlog Production events waiting to upload
            # TYPE event_outbox_backlog gauge
            event_outbox_backlog 7
            # HELP event_shipped_total Production events acknowledged by platform
            # TYPE event_shipped_total counter
            event_shipped_total{edge_id="EDGE-01"} 42
            """;

        var result = PrometheusTextParser.Parse(metrics);
        var backlog = JsonSerializer.SerializeToElement(result["event_outbox_backlog"]);
        var shipped = JsonSerializer.SerializeToElement(result["event_shipped_total"]);

        Assert.Equal("Production events waiting to upload", backlog.GetProperty("help").GetString());
        Assert.Equal("gauge", backlog.GetProperty("type").GetString());
        Assert.Equal(7, backlog.GetProperty("data")[0].GetProperty("value").GetDouble());
        Assert.Equal("Production events acknowledged by platform", shipped.GetProperty("help").GetString());
        Assert.Equal("counter", shipped.GetProperty("type").GetString());
        Assert.Equal("EDGE-01", shipped.GetProperty("data")[0].GetProperty("labels").GetProperty("edge_id").GetString());
    }

    [Fact]
    public void Parse_GroupsHistogramSamplesUnderTheDeclaredFamily()
    {
        const string metrics = """
            # HELP event_ship_latency_ms Upload latency
            # TYPE event_ship_latency_ms histogram
            event_ship_latency_ms_sum 18.5
            event_ship_latency_ms_count 3
            event_ship_latency_ms_bucket{le="+Inf"} 3
            """;

        var result = PrometheusTextParser.Parse(metrics);
        var latency = JsonSerializer.SerializeToElement(result["event_ship_latency_ms"]);

        Assert.Equal("Upload latency", latency.GetProperty("help").GetString());
        Assert.Equal(3, latency.GetProperty("data").GetArrayLength());
        Assert.Equal(18.5, latency.GetProperty("data")[0].GetProperty("value").GetDouble());
    }
}
