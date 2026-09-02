// 验证边缘组件 ConnectorEventsController 的协议、状态和失败边界。

using System.Text.Json;
using Ingot.Domain.Events;
using Ingot.Edge.Application.Abstractions;
using Ingot.Edge.ConnectorHost.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class ConnectorEventsControllerTests
{
    [Fact]
    public async Task Ingest_NormalizesPersistenceFieldsBeforeBatchValidation()
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
            EventType = "process.execution.started",
            OccurredAt = DateTimeOffset.UtcNow,
            RecordedAt = default,
            Source = "connector/SOURCE-01",
            Subject = new ObjectRef("asset", "FURNACE-01")
        };

        var result = await controller.Ingest([incoming], CancellationToken.None);

        Assert.IsType<AcceptedResult>(result);
        var captured = Assert.Single(sink.BatchCaptured);
        Assert.NotEqual(default, captured.RecordedAt);
        Assert.Equal(0, captured.Seq);
        Assert.Equal("edge/EDGE-001/connector/SOURCE-01", captured.Source);
    }

    [Fact]
    public async Task Ingest_InvalidEventLaterInBatch_DoesNotPersistEarlierEvents()
    {
        var sink = new CapturingEventSink();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters"
            }).Build();
        var controller = new ConnectorEventsController(sink, new StubEdgeIdentityProvider(), configuration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Headers.Authorization = "Bearer connector-token-with-at-least-24-characters";
        var valid = ProductionEvent.Create(
            "equipment.heartbeat",
            DateTimeOffset.UtcNow,
            "connector/SOURCE-01",
            new ObjectRef("equipment", "FURNACE-01"));
        var invalid = valid with { EventId = Guid.CreateVersion7().ToString(), Source = "" };

        var result = await controller.Ingest([valid, invalid], CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(sink.BatchCaptured);
        Assert.Equal(0, sink.BatchCalls);
    }

    [Fact]
    public async Task GetProcessExecution_ReadsEveryTransportPageAndExcludesAdjacentProcessExecutions()
    {
        var started = DateTimeOffset.Parse("2026-07-23T08:00:00Z");
        var events = Enumerable.Range(0, 608)
            .Select(index => Event(
                index + 1,
                index == 0 ? "process.execution.started" :
                index == 607 ? "process.execution.completed" :
                index is 1 or 92 or 243 or 364 or 485 ? "process.stage_changed" :
                "process.sample",
                "CYCLE-001",
                started.AddSeconds(Math.Min(index, 600))))
            .Append(Event(609, "process.execution.started", "CYCLE-002", started.AddSeconds(600)))
            .ToArray();
        var controller = new EventsController(new QueryingEventLog(events));

        var result = Assert.IsType<OkObjectResult>(
            await controller.GetProcessExecution("CYCLE-001", CancellationToken.None));
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
        var returned = document.RootElement.GetProperty("events");

        Assert.Equal(608, returned.GetArrayLength());
        Assert.All(returned.EnumerateArray(), item =>
            Assert.Equal("CYCLE-001", item.GetProperty("ExecutionId").GetString()));
    }

    private static ProductionEvent Event(
        long seq,
        string eventType,
        string executionId,
        DateTimeOffset occurredAt)
        => ProductionEvent.Create(
            eventType,
            occurredAt,
            "edge/EDGE-001/connector/test",
            new ObjectRef("equipment", "PRESS-01"),
            executionId) with
        { Seq = seq };

    private sealed class CapturingEventSink : IEventSink
    {
        public List<ProductionEvent> BatchCaptured { get; } = [];
        public int BatchCalls { get; private set; }

        public ValueTask<ProductionEvent> EmitAsync(ProductionEvent evt, CancellationToken ct = default)
            => ValueTask.FromResult(evt with { Seq = 1 });

        public ValueTask<IReadOnlyList<ProductionEvent>> EmitBatchAsync(
            IReadOnlyList<ProductionEvent> events,
            CancellationToken ct = default)
        {
            BatchCalls++;
            BatchCaptured.AddRange(events);
            return ValueTask.FromResult<IReadOnlyList<ProductionEvent>>(
                events.Select((item, index) => item with { Seq = index + 1 }).ToArray());
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

        public Task<IReadOnlyList<long>> AppendBatchAsync(
            IReadOnlyList<ProductionEvent> items,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ProductionEvent>> QueryAsync(
            EventQuery query,
            CancellationToken ct = default)
        {
            var result = events
                .Where(item => !query.AfterSeq.HasValue || item.Seq > query.AfterSeq)
                .Where(item => query.ExecutionId is null || item.ExecutionId == query.ExecutionId)
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
        public Task QuarantineAsync(long seq, string reason, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<long> CountPendingAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<EventLogPendingStatistics> GetPendingStatisticsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
