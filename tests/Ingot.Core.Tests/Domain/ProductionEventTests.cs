using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Domain;

public sealed class ProductionEventTests
{
    [Fact]
    public void Create_ShouldBuildImmutableFiveTupleEnvelopeWithUuidV7()
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var evt = ProductionEvent.Create(
            "cycle.completed",
            occurredAt,
            "edge/EDGE-01/PLC-01/cycle",
            new ObjectRef("equipment", "POL-03"),
            "cycle-001",
            new Dictionary<string, string> { ["material_lot"] = "LOT-01" },
            new Dictionary<string, object?> { ["good_count"] = 12 });

        Assert.Equal(7, Guid.Parse(evt.EventId).Version);
        Assert.Equal("cycle.completed", evt.EventType);
        Assert.Equal(occurredAt, evt.OccurredAt);
        Assert.Equal("equipment", evt.Subject.Type);
        Assert.Equal("POL-03", evt.Subject.Id);
        Assert.Equal("LOT-01", evt.Context["material_lot"]);
        Assert.Equal(12, evt.Data["good_count"]);
        Assert.Equal("cycle-001", evt.CorrelationId);
        Assert.Equal(0, evt.Seq);
    }
}
