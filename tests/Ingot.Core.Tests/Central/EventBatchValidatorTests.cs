using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Central;

public sealed class EventBatchValidatorTests
{
    [Fact]
    public void TryValidate_ShouldNormalizeEdgeIdAndOrderEvents()
    {
        var request = CreateRequest(CreateEvent(2), CreateEvent(1)) with
        {
            EdgeId = " EDGE-001 "
        };

        Assert.True(EventBatchValidator.TryValidate(request, out var normalized, out _));
        Assert.Equal("EDGE-001", normalized!.EdgeId);
        Assert.Equal([1L, 2L], normalized.Events.Select(static evt => evt.Seq));
    }

    [Fact]
    public void TryValidate_ShouldRejectDuplicateSequenceAndEventId()
    {
        var first = CreateEvent(1);

        Assert.False(EventBatchValidator.TryValidate(
            CreateRequest(first, CreateEvent(1)),
            out _,
            out var sequenceError));
        Assert.Contains("重复 Seq", sequenceError, StringComparison.Ordinal);

        Assert.False(EventBatchValidator.TryValidate(
            CreateRequest(first, CreateEvent(2) with { EventId = first.EventId }),
            out _,
            out var idError));
        Assert.Contains("重复 EventId", idError, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldRejectSourceOwnedByAnotherEdge()
    {
        var request = CreateRequest(CreateEvent(1) with
        {
            Source = "edge/EDGE-002/SOURCE-01/rule"
        });

        Assert.False(EventBatchValidator.TryValidate(request, out _, out var error));
        Assert.Contains("edge/EDGE-001/", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldRejectMalformedEnvelopeBeforeStorage()
    {
        var request = CreateRequest(CreateEvent(1) with { EventTypeVersion = 0 });

        Assert.False(EventBatchValidator.TryValidate(request, out _, out var error));
        Assert.Contains("EventTypeVersion", error, StringComparison.Ordinal);
    }

    private static EventBatchRequest CreateRequest(params ProductionEvent[] events) => new()
    {
        EdgeId = "EDGE-001",
        Events = events
    };

    private static ProductionEvent CreateEvent(long seq)
        => ProductionEvent.Create(
            "cycle.completed",
            DateTimeOffset.UtcNow,
            "edge/EDGE-001/SOURCE-01/rule",
            new ObjectRef("equipment", "EQ-01")) with
        {
            Seq = seq
        };
}
