using Ingot.Edge.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Edge.ConnectorHost.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class ConnectorEventsControllerTests
{
    [Fact]
    public async Task Ingest_NormalizesPersistenceFieldsBeforeValidation()
    {
        var sink = new CapturingEventSink();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
                ["ConnectorHost:MaxBatchSize"] = "1000"
            }).Build();
        var controller = new ConnectorEventsController(sink, new StubEdgeIdentityProvider(), configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers.Authorization = "Bearer connector-token-with-at-least-24-characters";
        var incoming = new ProductionEvent
        {
            EventId = Guid.CreateVersion7().ToString(),
            EventType = "cycle.started",
            OccurredAt = DateTimeOffset.UtcNow,
            RecordedAt = default,
            Source = "connector/SOURCE-01",
            Subject = new ObjectRef("asset", "FURNACE-01")
        };

        var result = await controller.Ingest([incoming], CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        Assert.NotNull(sink.Captured);
        Assert.NotEqual(default, sink.Captured.RecordedAt);
        Assert.Equal(0, sink.Captured.Seq);
        Assert.Equal("edge/EDGE-001/connector/SOURCE-01", sink.Captured.Source);
    }

    [Fact]
    public async Task GetCycle_ReadsEveryTransportPageAndExcludesAdjacentCycles()
    {
        var started = DateTimeOffset.Parse("2026-07-23T08:00:00Z");
        var events = Enumerable.Range(0, 608)
            .Select(index => Event(
                index + 1,
                index == 0 ? "cycle.started" :
                index == 607 ? "cycle.completed" :
                index is 1 or 92 or 243 or 364 or 485 ? "recipe.step_changed" :
                "process.sample",
                "CYCLE-001",
                started.AddSeconds(Math.Min(index, 600))))
            .Append(Event(609, "cycle.started", "CYCLE-002", started.AddSeconds(600)))
            .ToArray();
        var controller = new EventsController(new QueryingEventLog(events));

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetCycle("CYCLE-001", CancellationToken.None));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var returned = document.RootElement.GetProperty("events");

        Assert.Equal(608, returned.GetArrayLength());
        Assert.All(returned.EnumerateArray(), item =>
            Assert.Equal("CYCLE-001", item.GetProperty("CorrelationId").GetString()));
    }

    private static ProductionEvent Event(
        long seq,
        string eventType,
        string correlationId,
        DateTimeOffset occurredAt)
        => ProductionEvent.Create(
            eventType,
            occurredAt,
            "edge/EDGE-001/connector/test",
            new ObjectRef("equipment", "PRESS-01"),
            correlationId) with { Seq = seq };

    private sealed class CapturingEventSink : IEventSink
    {
        public ProductionEvent? Captured { get; private set; }

        public ValueTask<ProductionEvent> EmitAsync(ProductionEvent evt, CancellationToken ct = default)
        {
            Captured = evt;
            return ValueTask.FromResult(evt with { Seq = 1 });
        }
    }

    private sealed class StubEdgeIdentityProvider : IEdgeIdentityProvider
    {
        public string GetEdgeId() => "EDGE-001";
    }

    private sealed class QueryingEventLog(IReadOnlyList<ProductionEvent> events) : IEventLog
    {
        public Task<long> AppendAsync(ProductionEvent evt, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProductionEvent>> QueryAsync(
            EventQuery query,
            CancellationToken ct = default)
        {
            var result = events
                .Where(item => !query.AfterSeq.HasValue || item.Seq > query.AfterSeq)
                .Where(item => query.CorrelationId is null || item.CorrelationId == query.CorrelationId)
                .Where(item => query.SubjectType is null || item.Subject.Type == query.SubjectType)
                .Where(item => query.SubjectId is null || item.Subject.Id == query.SubjectId)
                .Where(item => !query.From.HasValue || item.OccurredAt >= query.From)
                .Where(item => !query.To.HasValue || item.OccurredAt <= query.To)
                .OrderBy(item => item.Seq)
                .Take(query.Limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<ProductionEvent>>(result);
        }

        public Task<IReadOnlyList<ProductionEvent>> ReadPendingAsync(int max, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task MarkShippedAsync(long upToSeq, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task IncrementShipAttemptsAsync(long fromSeq, long toSeq, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<long> CountPendingAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
