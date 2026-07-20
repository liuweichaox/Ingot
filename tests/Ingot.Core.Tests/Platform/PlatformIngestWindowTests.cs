using Ingot.Platform.Infrastructure.Events;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class PlatformIngestWindowTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 19, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryValidate_ShouldAcceptEventsInsideWindow()
    {
        var request = CreateRequest(CreateEvent(Now.AddDays(-1)));

        Assert.True(PlatformIngestWindow.TryValidate(request, new PlatformEventOptions(), Now, out _));
    }

    [Fact]
    public void TryValidate_ShouldRejectFarFutureOccurredAt()
    {
        var request = CreateRequest(CreateEvent(Now.AddHours(2)));

        Assert.False(PlatformIngestWindow.TryValidate(
            request,
            new PlatformEventOptions { MaxFutureSkewMinutes = 60 },
            Now,
            out var error));
        Assert.Contains("未来时间窗", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldRejectAncientOccurredAt()
    {
        var request = CreateRequest(CreateEvent(Now.AddDays(-4000)));

        Assert.False(PlatformIngestWindow.TryValidate(
            request,
            new PlatformEventOptions { MaxPastDays = 3650 },
            Now,
            out var error));
        Assert.Contains("最早时间窗", error, StringComparison.Ordinal);
    }

    private static EventBatchRequest CreateRequest(params ProductionEvent[] events) => new()
    {
        EdgeId = "EDGE-001",
        Events = events
    };

    private static ProductionEvent CreateEvent(DateTimeOffset occurredAt)
        => ProductionEvent.Create(
            "cycle.completed",
            occurredAt,
            "edge/EDGE-001/SOURCE-01/rule",
            new ObjectRef("equipment", "EQ-01")) with
        {
            Seq = 1
        };
}
