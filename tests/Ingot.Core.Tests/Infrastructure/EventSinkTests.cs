using Ingot.Edge.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Edge.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class EventSinkTests
{
    [Fact]
    public async Task Emit_ShouldRejectMalformedEnvelopeBeforePersistence()
    {
        var eventLog = new StubEventLog();
        var sink = CreateSink(
            eventLog,
            new EventPersistenceHealth(),
            new RecordingMetrics());

        var error = await Assert.ThrowsAsync<ArgumentException>(async () =>
            await sink.EmitAsync(CreateEvent() with { EventTypeVersion = 0 }));

        Assert.Contains("EventTypeVersion", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, eventLog.AppendCalls);
    }

    [Fact]
    public async Task Emit_ShouldNotRevokePersistedFactWhenBacklogMetricReadFails()
    {
        var eventLog = new StubEventLog
        {
            AppendResult = 42,
            CountPendingException = new IOException("metric read failed")
        };
        var health = new EventPersistenceHealth();
        var sink = CreateSink(eventLog, health, new RecordingMetrics());

        var result = await sink.EmitAsync(CreateEvent());

        Assert.Equal(42, result.Seq);
        Assert.False(health.Snapshot.IsDegraded);
        Assert.Equal(result.RecordedAt, health.Snapshot.LastSuccessAt);
    }

    [Fact]
    public async Task Emit_ShouldDegradeHealthWhenAppendFails()
    {
        var eventLog = new StubEventLog
        {
            AppendException = new IOException("disk full")
        };
        var health = new EventPersistenceHealth();
        var metrics = new RecordingMetrics();
        var sink = CreateSink(eventLog, health, metrics);

        var exception = await Assert.ThrowsAsync<IOException>(
            async () => await sink.EmitAsync(CreateEvent()));

        Assert.Equal("disk full", exception.Message);
        Assert.True(health.Snapshot.IsDegraded);
        Assert.Equal("disk full", health.Snapshot.LastError);
        Assert.Equal(1, metrics.PersistenceFailures);
    }

    [Fact]
    public async Task Emit_ShouldNotRevokePersistedFactWhenEmittedMetricFails()
    {
        var eventLog = new StubEventLog { AppendResult = 9 };
        var health = new EventPersistenceHealth();
        var sink = CreateSink(
            eventLog,
            health,
            new RecordingMetrics { ThrowOnEmitted = true });

        var result = await sink.EmitAsync(CreateEvent());

        Assert.Equal(9, result.Seq);
        Assert.False(health.Snapshot.IsDegraded);
    }

    [Fact]
    public async Task Emit_ShouldPreserveAppendExceptionWhenFailureMetricAlsoFails()
    {
        var eventLog = new StubEventLog
        {
            AppendException = new IOException("disk full")
        };
        var health = new EventPersistenceHealth();
        var sink = CreateSink(
            eventLog,
            health,
            new RecordingMetrics { ThrowOnPersistenceFailure = true });

        var exception = await Assert.ThrowsAsync<IOException>(
            async () => await sink.EmitAsync(CreateEvent()));

        Assert.Equal("disk full", exception.Message);
        Assert.True(health.Snapshot.IsDegraded);
    }

    private static EventSink CreateSink(
        IEventLog eventLog,
        IEventPersistenceHealth health,
        IMetricsCollector metrics)
        => new(
            eventLog,
            NullLogger<EventSink>.Instance,
            health,
            metrics);

    private static ProductionEvent CreateEvent()
        => ProductionEvent.Create(
            "cycle.completed",
            DateTimeOffset.UtcNow,
            "edge/EDGE-01/SOURCE-01/cycle",
            new ObjectRef("equipment", "POL-03"));

    private sealed class StubEventLog : IEventLog
    {
        public int AppendCalls { get; private set; }

        public long AppendResult { get; init; }

        public Exception? AppendException { get; init; }

        public Exception? CountPendingException { get; init; }

        public Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default)
        {
            AppendCalls++;
            return AppendException is null
                ? Task.FromResult(AppendResult)
                : Task.FromException<long>(AppendException);
        }

        public Task<IReadOnlyList<ProductionEvent>> QueryAsync(
            EventQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProductionEvent>>([]);

        public Task<IReadOnlyList<ProductionEvent>> ReadPendingAsync(
            int max,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProductionEvent>>([]);

        public Task MarkShippedAsync(long upToSeq, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task IncrementShipAttemptsAsync(
            long fromSeq,
            long toSeq,
            CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<long> CountPendingAsync(CancellationToken ct = default)
            => CountPendingException is null
                ? Task.FromResult(0L)
                : Task.FromException<long>(CountPendingException);
    }

    private sealed class RecordingMetrics : IMetricsCollector
    {
        public int PersistenceFailures { get; private set; }

        public bool ThrowOnEmitted { get; init; }

        public bool ThrowOnPersistenceFailure { get; init; }

        public void RecordCollectionLatency(
            string sourceCode,
            string? channelCode,
            string measurement,
            double latencyMs)
        {
        }

        public void RecordCollectionRate(
            string sourceCode,
            string? channelCode,
            string measurement,
            double pointsPerSecond)
        {
        }

        public void RecordQueueDepth(int depth)
        {
        }

        public void RecordProcessingLatency(double latencyMs)
        {
        }

        public void RecordWriteLatency(string measurement, double latencyMs)
        {
        }

        public void RecordBatchWriteEfficiency(int batchSize, double latencyMs)
        {
        }

        public void RecordError(
            string sourceCode,
            string? channelCode = null,
            string? measurement = null)
        {
        }

        public void RecordConnectionStatus(string sourceCode, bool isConnected)
        {
        }

        public void RecordConnectionDuration(string sourceCode, double durationSeconds)
        {
        }

        public void RecordEventEmitted(string eventType, double latencyMs)
        {
            if (ThrowOnEmitted)
                throw new InvalidOperationException("emitted metric failed");
        }

        public void RecordEventOutboxBacklog(long count)
        {
        }

        public void RecordEventBacklogDropped(long count)
        {
        }

        public void RecordContextStateEntries(long count)
        {
        }

        public void RecordEventPersistenceFailure(string eventType)
        {
            if (ThrowOnPersistenceFailure)
                throw new InvalidOperationException("failure metric failed");
            PersistenceFailures++;
        }

        public void RecordEventShipFailure(string edgeId)
        {
        }

        public void RecordEventsShipped(string edgeId, int count, double latencyMs)
        {
        }
    }
}
