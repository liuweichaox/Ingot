using Ingot.ImportCli;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ImportMappingEngineTests
{
    private static ImportMapping SampleMapping() => MappingEngine.LoadMapping(
        """
        {
          "edgeId": "IMPORT-01",
          "eventType": { "value": "process.sample" },
          "occurredAt": { "column": "time", "format": "yyyy-MM-dd HH:mm:ss", "utcOffset": "+08:00" },
          "subjectType": { "value": "asset" },
          "subjectId": { "column": "machine" },
          "executionId": { "column": "execution" },
          "context": { "product_code": { "column": "product" } },
          "values": {
            "temp": { "column": "t", "type": "number" },
            "step": { "column": "step", "type": "integer" }
          }
        }
        """);

    [Fact]
    public void ReadCsv_ParsesQuotedFieldsAndEmbeddedCommas()
    {
        using var reader = new StringReader(
            "time,machine,product,execution,t,step\n" +
            "2026-06-01 08:00:00,M-01,\"LENS,A\",CYC-1,512.5,4\n");
        var rows = MappingEngine.ReadCsv(reader).ToList();
        Assert.Single(rows);
        Assert.Equal("LENS,A", rows[0]["product"]);
        Assert.Equal("512.5", rows[0]["t"]);
    }

    [Fact]
    public void BuildEvent_MapsColumnsToContractShape()
    {
        using var reader = new StringReader(
            "time,machine,product,execution,t,step\n" +
            "2026-06-01 08:00:00,M-01,LENS-A,CYC-1,512.5,4\n");
        var row = MappingEngine.ReadCsv(reader).Single();
        var evt = MappingEngine.BuildEvent(row, SampleMapping(), seq: 7, sourceFileTag: "history");

        Assert.Equal("process.sample", evt.EventType);
        Assert.Equal("edge/IMPORT-01/import/history", evt.Source);
        Assert.Equal("asset", evt.Subject.Type);
        Assert.Equal("M-01", evt.Subject.Id);
        Assert.Equal("CYC-1", evt.ExecutionId);
        Assert.Equal(7, evt.Seq);
        Assert.Equal("LENS-A", evt.Context["product_code"]);
        // +08:00 本地时间 08:00 → UTC 00:00
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), evt.OccurredAt);
        var values = Assert.IsType<Dictionary<string, object?>>(evt.Data["values"]);
        Assert.Equal(512.5, Assert.IsType<double>(values["temp"]));
        Assert.Equal(4L, Assert.IsType<long>(values["step"]));
        Assert.True(Guid.TryParse(evt.EventId, out var id) && id.Version == 7);
    }

    [Fact]
    public void BuildEvent_SkipsEmptyCells_NeverGuesses()
    {
        using var reader = new StringReader(
            "time,machine,product,execution,t,step\n" +
            "2026-06-01 08:00:00,M-01,,CYC-1,,4\n");
        var row = MappingEngine.ReadCsv(reader).Single();
        var evt = MappingEngine.BuildEvent(row, SampleMapping(), 1, "history");
        Assert.False(evt.Context.ContainsKey("product_code"));
        var values = Assert.IsType<Dictionary<string, object?>>(evt.Data["values"]);
        Assert.False(values.ContainsKey("temp"));
        Assert.True(values.ContainsKey("step"));
    }

    [Fact]
    public void BuildEvent_InvalidNumber_Throws()
    {
        using var reader = new StringReader(
            "time,machine,product,execution,t,step\n" +
            "2026-06-01 08:00:00,M-01,LENS-A,CYC-1,not-a-number,4\n");
        var row = MappingEngine.ReadCsv(reader).Single();
        Assert.Throws<FormatException>(() => MappingEngine.BuildEvent(row, SampleMapping(), 1, "history"));
    }

    [Fact]
    public void ParseTimestamp_RespectsExplicitOffsetInText()
    {
        var parsed = MappingEngine.ParseTimestamp(
            "2026-06-01T08:00:00+08:00",
            new FieldSource { Column = "time" });
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), parsed);
    }
}
