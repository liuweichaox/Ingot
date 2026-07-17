using System.Net;
using System.Net.Http.Json;
using Ingot.Application.Abstractions;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Edge.Agent.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class HttpEventShipperTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ShouldAdvanceOutboxOnlyAfterCentralAck(
        bool eventMetricsThrow)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventLog = new FakeEventLog(
            [
                CreateEvent(1),
                CreateEvent(2)
            ],
            () => cancellation.Cancel());
        var handler = new RecordingHandler();
        var factory = new SingleClientFactory(new HttpClient(handler));
        var reportingOptions = Options.Create(new EdgeReportingOptions
        {
            EdgeId = "EDGE-001",
            CentralApiBaseUrl = "http://central/",
            EnableEventShipping = true,
            EventIngestToken = "secret",
            EventBatchSize = 100,
            EventIdleDelayMs = 100
        });
        var identity = new EdgeIdentityService(
            reportingOptions,
            NullLogger<EdgeIdentityService>.Instance);
        var shipper = new HttpEventShipper(
            eventLog,
            identity,
            factory,
            reportingOptions,
            new FakeMetrics { ThrowOnEventMetrics = eventMetricsThrow },
            NullLogger<HttpEventShipper>.Instance);

        await shipper.RunAsync(cancellation.Token);

        Assert.Equal(2, eventLog.AckSeq);
        Assert.Equal("Bearer secret", handler.Authorization);
        Assert.NotNull(handler.Request);
        Assert.Equal("EDGE-001", handler.Request!.EdgeId);
        Assert.Equal([1L, 2L], handler.Request.Events.Select(static evt => evt.Seq));
    }

    private static ProductionEvent CreateEvent(long seq) =>
        ProductionEvent.Create(
            "cycle.completed",
            DateTimeOffset.UtcNow,
            "edge/EDGE-001/PLC-01/cycle",
            new ObjectRef("equipment", "EQ-01"),
            Guid.NewGuid().ToString()) with
        {
            Seq = seq
        };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public EventBatchRequest? Request { get; private set; }
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            Request = await request.Content!.ReadFromJsonAsync<EventBatchRequest>(
                cancellationToken: cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new EventBatchResponse
                {
                    Accepted = 2,
                    AckSeq = 2
                })
            };
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeEventLog(
        IReadOnlyList<ProductionEvent> pending,
        Action onAck) : IEventLog
    {
        public long? AckSeq { get; private set; }

        public Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProductionEvent>> QueryAsync(
            EventQuery query,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProductionEvent>> ReadPendingAsync(
            int max,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProductionEvent>>(
                AckSeq.HasValue ? [] : pending.Take(max).ToArray());

        public Task MarkShippedAsync(long upToSeq, CancellationToken ct = default)
        {
            AckSeq = upToSeq;
            onAck();
            return Task.CompletedTask;
        }

        public Task IncrementShipAttemptsAsync(long fromSeq, long toSeq, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<long> CountPendingAsync(CancellationToken ct = default)
            => Task.FromResult(AckSeq.HasValue ? 0L : (long)pending.Count);
    }

    private sealed class FakeMetrics : IMetricsCollector
    {
        public bool ThrowOnEventMetrics { get; init; }

        public void RecordCollectionLatency(string sourceCode, string? channelCode, string measurement, double latencyMs) { }
        public void RecordCollectionRate(string sourceCode, string? channelCode, string measurement, double pointsPerSecond) { }
        public void RecordQueueDepth(int depth) { }
        public void RecordProcessingLatency(double latencyMs) { }
        public void RecordWriteLatency(string measurement, double latencyMs) { }
        public void RecordBatchWriteEfficiency(int batchSize, double latencyMs) { }
        public void RecordError(string sourceCode, string? channelCode = null, string? measurement = null) { }
        public void RecordConnectionStatus(string sourceCode, bool isConnected) { }
        public void RecordConnectionDuration(string sourceCode, double durationSeconds) { }
        public void RecordEventEmitted(string eventType, double latencyMs) { }
        public void RecordEventOutboxBacklog(long count)
        {
            if (ThrowOnEventMetrics)
                throw new InvalidOperationException("outbox metric failed");
        }
        public void RecordEventBacklogDropped(long count) { }
        public void RecordContextStateEntries(long count) { }
        public void RecordEventPersistenceFailure(string eventType) { }
        public void RecordEventShipFailure(string edgeId)
        {
            if (ThrowOnEventMetrics)
                throw new InvalidOperationException("ship failure metric failed");
        }

        public void RecordEventsShipped(string edgeId, int count, double latencyMs)
        {
            if (ThrowOnEventMetrics)
                throw new InvalidOperationException("shipped metric failed");
        }
    }
}
