using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Domain;

public sealed class ProductionEventValidatorTests
{
    [Fact]
    public void TryValidate_ShouldAcceptCreatedEventBeforeAndAfterPersistence()
    {
        var evt = CreateEvent();

        Assert.True(ProductionEventValidator.TryValidate(evt, false, out _));
        Assert.True(ProductionEventValidator.TryValidate(evt with { Seq = 1 }, true, out _));
    }

    [Theory]
    [InlineData("not-a-guid", "cycle.completed", 1, "EventId")]
    [InlineData("019f6c00-0000-4000-8000-000000000001", "cycle.completed", 1, "EventId")]
    [InlineData("019f6c00-0000-7000-8000-000000000001", "CycleCompleted", 1, "EventType")]
    [InlineData("019f6c00-0000-7000-8000-000000000001", "cycle.completed", 0, "EventTypeVersion")]
    public void TryValidate_ShouldRejectMalformedEnvelope(
        string eventId,
        string eventType,
        int eventTypeVersion,
        string expectedError)
    {
        var evt = CreateEvent() with
        {
            EventId = eventId,
            EventType = eventType,
            EventTypeVersion = eventTypeVersion,
            Seq = 1
        };

        Assert.False(ProductionEventValidator.TryValidate(evt, true, out var error));
        Assert.Contains(expectedError, error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldRejectNonPositivePersistedSequence()
    {
        Assert.False(ProductionEventValidator.TryValidate(CreateEvent(), true, out var error));
        Assert.Contains("Seq", error, StringComparison.Ordinal);
    }

    private static ProductionEvent CreateEvent()
        => ProductionEvent.Create(
            "cycle.completed",
            DateTimeOffset.UtcNow,
            "edge/EDGE-001/SOURCE-01/rule",
            new ObjectRef("equipment", "EQ-01"));
}
