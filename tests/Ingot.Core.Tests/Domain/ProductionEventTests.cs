using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Domain;

public sealed class ProductionEventTests
{
    [Fact]
    public void Create_ShouldBuildSealedProductionEnvelopeWithUuidV7()
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var evt = ProductionEvent.Create(
            "process.execution.completed",
            occurredAt,
            "edge/EDGE-01/PLC-01/execution",
            new ObjectRef("equipment", "POL-03"),
            "execution-001",
            new Dictionary<string, string> { ["material_lot"] = "LOT-01" },
            new Dictionary<string, object?> { ["good_count"] = 12 },
            new AppliedConfigurationRef("ingestion-task", "TASK-01", 3),
            ["missing_value"]);

        Assert.Equal(7, Guid.Parse(evt.EventId).Version);
        Assert.Equal("process.execution.completed", evt.EventType);
        Assert.Equal(occurredAt, evt.OccurredAt);
        Assert.Equal("equipment", evt.Subject.Type);
        Assert.Equal("POL-03", evt.Subject.Id);
        Assert.Equal("LOT-01", evt.Context["material_lot"]);
        Assert.Equal(12, evt.Data["good_count"]);
        Assert.Equal("execution-001", evt.ExecutionId);
        Assert.Equal(1, evt.SchemaVersion);
        Assert.Equal(new AppliedConfigurationRef("ingestion-task", "TASK-01", 3), evt.AppliedConfiguration);
        Assert.Equal(["missing_value"], evt.QualityFlags);
        Assert.Equal(64, evt.PayloadHash.Length);
        Assert.True(ProductionEventIntegrity.HasValidPayloadHash(evt));
        Assert.Equal(0, evt.Seq);
    }

    [Fact]
    public void Seal_ShouldProduceSameHashForEquivalentDictionaryOrder()
    {
        var original = ProductionEvent.Create(
            "process.execution.completed",
            DateTimeOffset.Parse("2026-08-19T08:00:00Z"),
            "edge/EDGE-01/PLC-01/execution",
            new ObjectRef("equipment", "PRESS-01"),
            context: new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" },
            data: new Dictionary<string, object?>
            {
                ["outer"] = new Dictionary<string, object?> { ["z"] = 3, ["a"] = 1 }
            });
        var reordered = ProductionEventIntegrity.Seal(original with
        {
            Context = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" },
            Data = new Dictionary<string, object?>
            {
                ["outer"] = new Dictionary<string, object?> { ["a"] = 1, ["z"] = 3 }
            }
        });

        Assert.Equal(original.PayloadHash, reordered.PayloadHash);
    }
}
