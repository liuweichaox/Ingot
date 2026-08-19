using System.Net;
using System.Net.Http.Json;
using Ingot.Edge.Application.Abstractions;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Edge.Application.Options;
using Ingot.Edge.ConnectorHost.Services;
using Ingot.Edge.Infrastructure.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class HttpEventShipperTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RunAsync_ShouldAdvanceOutboxOnlyAfterPlatformAck(
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
            SiteId = "SITE-001",
            EdgeId = "EDGE-001",
            PlatformApiBaseUrl = "http://platform/",
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
            new EdgeDeliveryStatus(),
            NullLogger<HttpEventShipper>.Instance);

        await shipper.RunAsync(cancellation.Token);

        Assert.Equal(2, eventLog.AckSeq);
        Assert.Equal("Bearer secret", handler.Authorization);
        Assert.NotNull(handler.Request);
        Assert.Equal("SITE-001", handler.Request!.SiteId);
        Assert.Equal("EDGE-001", handler.Request!.EdgeId);
        Assert.Equal([1L, 2L], handler.Request.Events.Select(static evt => evt.Seq));
    }

    [Fact]
    public async Task RunAsync_ShouldKeepAndReplayBatchAfterPlatformRecovers()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var pending = new[] { CreateEvent(7), CreateEvent(8) };
        var eventLog = new FakeEventLog(pending, () => cancellation.Cancel());
        var handler = new RecoveringHandler();
        var options = Options.Create(new EdgeReportingOptions
        {
            SiteId = "SITE-001",
            EdgeId = "EDGE-001",
            PlatformApiBaseUrl = "http://platform/",
            EnableEventShipping = true,
            EventIngestToken = "secret",
            EventBatchSize = 100,
            EventIdleDelayMs = 100,
            EventRetryMaxSeconds = 1
        });
        var identity = new EdgeIdentityService(options, NullLogger<EdgeIdentityService>.Instance);
        var delivery = new EdgeDeliveryStatus();
        var shipper = new HttpEventShipper(
            eventLog,
            identity,
            new SingleClientFactory(new HttpClient(handler)),
            options,
            new FakeMetrics(),
            delivery,
            NullLogger<HttpEventShipper>.Instance);

        await shipper.RunAsync(cancellation.Token);

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal([7L, 8L], request.Events.Select(evt => evt.Seq)));
        Assert.Equal(1, eventLog.ShipAttempts);
        Assert.Equal(8, eventLog.AckSeq);
        var runtime = delivery.Get();
        Assert.Equal("synchronized", runtime.State);
        Assert.Equal(1, runtime.RecoveryCount);
        Assert.Equal(8, runtime.LastAcknowledgedSequence);
        Assert.Equal(2, runtime.EventsShipped);
        Assert.NotNull(runtime.LastRecoveryDurationMs);
    }

    [Fact]
    public async Task RunAsync_QuarantinesDeterministicallyRejectedEventAndContinues()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventLog = new PoisonEventLog([CreateEvent(1), CreateEvent(2), CreateEvent(3)], () => cancellation.Cancel());
        var handler = new PoisonHandler();
        var options = Options.Create(new EdgeReportingOptions
        {
            SiteId = "SITE-001",
            EdgeId = "EDGE-001",
            PlatformApiBaseUrl = "http://platform/",
            EnableEventShipping = true,
            EventIngestToken = "secret",
            EventBatchSize = 100,
            EventIdleDelayMs = 100
        });
        var shipper = new HttpEventShipper(
            eventLog,
            new EdgeIdentityService(options, NullLogger<EdgeIdentityService>.Instance),
            new SingleClientFactory(new HttpClient(handler)),
            options,
            new FakeMetrics(),
            new EdgeDeliveryStatus(),
            NullLogger<HttpEventShipper>.Instance);

        await shipper.RunAsync(cancellation.Token);

        Assert.Equal([2L], eventLog.Quarantined);
        Assert.Equal(3, eventLog.AckSeq);
        Assert.Equal(4, handler.Requests.Count);
    }

    private static ProductionEvent CreateEvent(long seq) =>
        ProductionEvent.Create(
            "process.execution.completed",
            DateTimeOffset.UtcNow,
            "edge/EDGE-001/PLC-01/execution",
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

    private sealed class RecoveringHandler : HttpMessageHandler
    {
        public List<EventBatchRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add((await request.Content!.ReadFromJsonAsync<EventBatchRequest>(
                cancellationToken: cancellationToken))!);
            if (Requests.Count == 1)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = JsonContent.Create(new { error = "platform unavailable" })
                };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new EventBatchResponse
                {
                    Accepted = 2,
                    AckSeq = 8
                })
            };
        }
    }

    private sealed class PoisonHandler : HttpMessageHandler
    {
        public List<EventBatchRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var batch = (await request.Content!.ReadFromJsonAsync<EventBatchRequest>(cancellationToken: cancellationToken))!;
            Requests.Add(batch);
            if (batch.Events.Count > 1 || batch.Events[0].Seq == 2)
                return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { error = "invalid event" }) };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new EventBatchResponse { Accepted = 1, AckSeq = batch.Events[0].Seq })
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
        public int ShipAttempts { get; private set; }

        public Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<long>> AppendBatchAsync(
            IReadOnlyList<ProductionEvent> events,
            CancellationToken ct = default)
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
        {
            ShipAttempts++;
            return Task.CompletedTask;
        }

        public Task QuarantineAsync(long seq, string reason, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<long> CountPendingAsync(CancellationToken ct = default)
            => Task.FromResult(AckSeq.HasValue ? 0L : (long)pending.Count);

        public async Task<EventLogPendingStatistics> GetPendingStatisticsAsync(CancellationToken ct = default)
            => new(await CountPendingAsync(ct), null, null, null);
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

    private sealed class PoisonEventLog(IReadOnlyList<ProductionEvent> events, Action onFinished) : IEventLog
    {
        private readonly HashSet<long> _quarantined = [];
        public IReadOnlyList<long> Quarantined => _quarantined.Order().ToArray();
        public long? AckSeq { get; private set; }
        public Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<long>> AppendBatchAsync(IReadOnlyList<ProductionEvent> items, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductionEvent>> QueryAsync(EventQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductionEvent>> ReadPendingAsync(int max, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ProductionEvent>>(events.Where(value => value.Seq > (AckSeq ?? 0) && !_quarantined.Contains(value.Seq)).Take(max).ToArray());
        public Task MarkShippedAsync(long upToSeq, CancellationToken ct = default)
        {
            AckSeq = upToSeq;
            if (upToSeq == events[^1].Seq) onFinished();
            return Task.CompletedTask;
        }
        public Task IncrementShipAttemptsAsync(long fromSeq, long toSeq, CancellationToken ct = default) => Task.CompletedTask;
        public Task QuarantineAsync(long seq, string reason, CancellationToken ct = default)
        {
            _quarantined.Add(seq);
            return Task.CompletedTask;
        }
        public Task<long> CountPendingAsync(CancellationToken ct = default)
            => Task.FromResult((long)events.Count(value => value.Seq > (AckSeq ?? 0) && !_quarantined.Contains(value.Seq)));
        public async Task<EventLogPendingStatistics> GetPendingStatisticsAsync(CancellationToken ct = default)
            => new(await CountPendingAsync(ct), null, null, null);
    }
}
