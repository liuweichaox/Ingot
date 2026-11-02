// Verify Edge associates a snapshot with an Event ID only when that sample enters the local outbox.
using Ingot.Contracts.Acquisition;
using Ingot.Domain.Events;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionLifecycleTrackerEventIdTests
{
    [Fact]
    public async Task ActiveSample_ShouldExposeItsEventIdAfterSuccessfulOutboxWrite()
    {
        var tracker = new AcquisitionLifecycleTracker();
        var sink = new RecordingEventSink();
        var mapped = Mapping(active: true);

        var events = await tracker.TrackAndEmitAsync(
            mapped,
            new AcquisitionLifecycleMapping { ActiveContextKey = "run_active" },
            1000,
            sink);

        Assert.Contains(events, item => item.EventId == mapped.Sample.EventId);
        Assert.Equal(mapped.Sample.EventId, tracker.EmittedSampleEventId);
        Assert.Contains(sink.Events, item => item.EventId == mapped.Sample.EventId);
    }

    [Fact]
    public async Task InactiveSnapshot_ShouldClearEventIdWhenNoSampleWasEmitted()
    {
        var tracker = new AcquisitionLifecycleTracker();
        var sink = new RecordingEventSink();

        var events = await tracker.TrackAndEmitAsync(
            Mapping(active: false),
            new AcquisitionLifecycleMapping { ActiveContextKey = "run_active" },
            1000,
            sink);

        Assert.Empty(events);
        Assert.Null(tracker.EmittedSampleEventId);
    }

    [Fact]
    public async Task FailedOutboxWrite_ShouldNotExposeAnEventId()
    {
        var tracker = new AcquisitionLifecycleTracker();

        await Assert.ThrowsAsync<IOException>(() => tracker.TrackAndEmitAsync(
            Mapping(active: true),
            (AcquisitionLifecycleMapping?)null,
            1000,
            new RecordingEventSink(throwOnWrite: true)));

        Assert.Null(tracker.EmittedSampleEventId);
    }

    private static AcquisitionMappingResult Mapping(bool active)
    {
        var sample = ProductionEvent.Create(
            "process.sample",
            DateTimeOffset.UtcNow,
            "edge/EDGE-001/connector/test",
            new ObjectRef("equipment", "PRESS-01"),
            context: new Dictionary<string, string> { ["run_active"] = active ? "true" : "false" },
            data: new Dictionary<string, object?>
            {
                ["values"] = new Dictionary<string, object?> { ["temperature"] = 25d }
            });
        return new AcquisitionMappingResult(sample, null, null);
    }

    private sealed class RecordingEventSink(bool throwOnWrite = false) : IEventSink
    {
        public List<ProductionEvent> Events { get; } = [];

        public ValueTask<ProductionEvent> EmitAsync(ProductionEvent evt, CancellationToken ct = default)
            => ValueTask.FromResult(evt);

        public ValueTask<IReadOnlyList<ProductionEvent>> EmitBatchAsync(
            IReadOnlyList<ProductionEvent> events,
            CancellationToken ct = default)
        {
            if (throwOnWrite)
                throw new IOException("Outbox is unavailable.");
            Events.AddRange(events);
            return ValueTask.FromResult<IReadOnlyList<ProductionEvent>>(events);
        }
    }
}
