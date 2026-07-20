using Ingot.Platform.Infrastructure.Events;
using Ingot.Domain.Events;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class SequenceGapDetectorTests
{
    [Fact]
    public void HasSequenceGap_ShouldDetectGapInsideBatch()
    {
        var events = new[]
        {
            CreateEvent(10),
            CreateEvent(12)
        };

        Assert.True(PostgresPlatformEventStore.HasSequenceGap(9, events));
    }

    [Fact]
    public void HasSequenceGap_ShouldAcceptContiguousReplayAndNextBatch()
    {
        Assert.False(PostgresPlatformEventStore.HasSequenceGap(
            2,
            [CreateEvent(1), CreateEvent(2)]));
        Assert.False(PostgresPlatformEventStore.HasSequenceGap(
            2,
            [CreateEvent(3), CreateEvent(4)]));
        Assert.False(PostgresPlatformEventStore.HasSequenceGap(
            2,
            [CreateEvent(1), CreateEvent(3)]));
    }

    [Fact]
    public void HasSequenceGap_ShouldDetectMissingInitialEvents()
    {
        Assert.True(PostgresPlatformEventStore.HasSequenceGap(null, [CreateEvent(3)]));
    }

    private static ProductionEvent CreateEvent(long seq)
        => ProductionEvent.Create(
            "cycle.completed",
            DateTimeOffset.UtcNow,
            "edge/EDGE-01/SOURCE-01/rule",
            new ObjectRef("equipment", "EQ-01")) with
        {
            Seq = seq
        };
}
