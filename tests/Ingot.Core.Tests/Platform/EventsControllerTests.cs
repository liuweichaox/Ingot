using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class EventsControllerTests
{
    [Fact]
    public async Task Query_UsesBeforeCursorForOlderPages()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var store = new StubPlatformEventStore(Enumerable.Range(1, 10)
            .Select(index => Row(index, "process.sample", startedAt.AddSeconds(index)))
            .ToArray());
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = new EventsController(store, new EdgeTokenValidator(options), options)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var action = await controller.Query(
            null, null, null, null, null, null, null, null, 8, 0, 3, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var json = JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal([7L, 6L, 5L], json.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("IngestId").GetInt64()).ToArray());
        Assert.Equal(5, json.GetProperty("previousIngestId").GetInt64());
    }

    [Fact]
    public async Task Query_ReturnsFilteredTotalAndUsesOffset()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var store = new StubPlatformEventStore(Enumerable.Range(1, 10)
            .Select(index => Row(index, "process.sample", startedAt.AddSeconds(index)))
            .ToArray());
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = new EventsController(store, new EdgeTokenValidator(options), options)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var action = await controller.Query(
            null, null, null, null, null, null, null, null, null, 3, 3, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var json = JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal([7L, 6L, 5L], json.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("IngestId").GetInt64()).ToArray());
        Assert.Equal(10, json.GetProperty("total").GetInt64());
    }

    [Fact]
    public async Task Query_RejectsConflictingCursors()
    {
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = new EventsController(new StubPlatformEventStore([]), new EdgeTokenValidator(options), options)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var action = await controller.Query(
            null, null, null, null, null, null, null, 1, 8, 0, 3, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action);
    }

    [Fact]
    public async Task GetCycle_PagesThroughMoreThanFiveHundredEvents()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var rows = Enumerable.Range(1, 602)
            .Select(index => Row(
                index,
                index == 1 ? "cycle.started" :
                index == 602 ? "cycle.completed" : "process.sample",
                startedAt.AddSeconds(index)))
            .ToArray();
        var store = new StubPlatformEventStore(rows);
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = new EventsController(
            store,
            new EdgeTokenValidator(options),
            options)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var action = await controller.GetCycle("cycle-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var json = JsonSerializer.SerializeToElement(ok.Value);
        Assert.Equal(602, json.GetProperty("events").GetArrayLength());
        Assert.Equal(602, json.GetProperty("events")
            .EnumerateArray()
            .Select(item => item.GetProperty("IngestId").GetInt64())
            .Distinct()
            .Count());
        Assert.True(store.QueryCalls >= 4);
    }

    [Fact]
    public async Task GetCycle_ExcludesEventsBelongingToAnAdjacentCycle()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var rows = new[]
        {
            Row(1, "cycle.started", startedAt, "cycle-1"),
            Row(2, "process.sample", startedAt.AddSeconds(1), "cycle-1"),
            Row(3, "alarm.raised", startedAt.AddSeconds(2), null),
            Row(4, "cycle.started", startedAt.AddSeconds(3), "cycle-2"),
            Row(5, "process.sample", startedAt.AddSeconds(4), "cycle-2"),
            Row(6, "cycle.completed", startedAt.AddSeconds(5), "cycle-1")
        };
        var store = new StubPlatformEventStore(rows);
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = new EventsController(
            store,
            new EdgeTokenValidator(options),
            options)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        var action = await controller.GetCycle("cycle-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var json = JsonSerializer.SerializeToElement(ok.Value);
        var events = json.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(4, events.Length);
        Assert.DoesNotContain(
            events,
            item => item.GetProperty("Event")
                .GetProperty("CorrelationId")
                .GetString() == "cycle-2");
    }

    private static PlatformProductionEvent Row(
        long ingestId,
        string eventType,
        DateTimeOffset occurredAt,
        string? correlationId = "cycle-1")
        => new()
        {
            IngestId = ingestId,
            EdgeId = "EDGE-001",
            IngestedAt = occurredAt.AddSeconds(1),
            Event = new ProductionEvent
            {
                EventId = $"event-{ingestId}",
                EventType = eventType,
                OccurredAt = occurredAt,
                RecordedAt = occurredAt,
                Source = "edge/EDGE-001/press/PRESS-01",
                Subject = new ObjectRef("asset", "PRESS-01"),
                Context = new Dictionary<string, string>(),
                Data = new Dictionary<string, object?>
                {
                    ["mold.temperature_c"] = 600d,
                    ["press.force_n"] = 1000d
                },
                CorrelationId = correlationId,
                Seq = ingestId
            }
        };

    private sealed class StubPlatformEventStore(
        IReadOnlyList<PlatformProductionEvent> rows) : IPlatformEventStore
    {
        public int QueryCalls { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<EventBatchResponse> IngestAsync(
            EventBatchRequest request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            QueryCalls++;
            IEnumerable<PlatformProductionEvent> filtered = rows;
            if (!string.IsNullOrWhiteSpace(query.EdgeId))
                filtered = filtered.Where(item => item.EdgeId == query.EdgeId);
            if (!string.IsNullOrWhiteSpace(query.CorrelationId))
                filtered = filtered.Where(item => item.Event.CorrelationId == query.CorrelationId);
            if (!string.IsNullOrWhiteSpace(query.SubjectType))
                filtered = filtered.Where(item => item.Event.Subject.Type == query.SubjectType);
            if (!string.IsNullOrWhiteSpace(query.SubjectId))
                filtered = filtered.Where(item => item.Event.Subject.Id == query.SubjectId);
            if (query.From.HasValue)
                filtered = filtered.Where(item => item.Event.OccurredAt >= query.From.Value);
            if (query.To.HasValue)
                filtered = filtered.Where(item => item.Event.OccurredAt <= query.To.Value);
            if (query.AfterIngestId.HasValue)
                filtered = filtered.Where(item => item.IngestId > query.AfterIngestId.Value);
            if (query.BeforeIngestId.HasValue)
                filtered = filtered.Where(item => item.IngestId < query.BeforeIngestId.Value);

            var ordered = query.AfterIngestId.HasValue
                ? filtered.OrderBy(item => item.IngestId)
                : filtered.OrderByDescending(item => item.IngestId);
            return Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(
                ordered.Skip(query.Offset).Take(query.Limit).ToArray());
        }

        public Task<PlatformEventScopeStats> GetScopeStatsAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            IEnumerable<PlatformProductionEvent> filtered = rows;
            if (!string.IsNullOrWhiteSpace(query.EdgeId))
                filtered = filtered.Where(item => item.EdgeId == query.EdgeId);
            if (!string.IsNullOrWhiteSpace(query.EventType))
                filtered = filtered.Where(item => item.Event.EventType == query.EventType);
            if (!string.IsNullOrWhiteSpace(query.CorrelationId))
                filtered = filtered.Where(item => item.Event.CorrelationId == query.CorrelationId);
            if (!string.IsNullOrWhiteSpace(query.SubjectType))
                filtered = filtered.Where(item => item.Event.Subject.Type == query.SubjectType);
            if (!string.IsNullOrWhiteSpace(query.SubjectId))
                filtered = filtered.Where(item => item.Event.Subject.Id == query.SubjectId);
            if (query.From.HasValue)
                filtered = filtered.Where(item => item.Event.OccurredAt >= query.From.Value);
            if (query.To.HasValue)
                filtered = filtered.Where(item => item.Event.OccurredAt <= query.To.Value);
            if (query.AfterIngestId.HasValue)
                filtered = filtered.Where(item => item.IngestId > query.AfterIngestId.Value);
            if (query.BeforeIngestId.HasValue)
                filtered = filtered.Where(item => item.IngestId < query.BeforeIngestId.Value);
            var matching = filtered.ToArray();
            return Task.FromResult(new PlatformEventScopeStats
            {
                Count = matching.LongLength,
                LatestOccurredAt = matching.Length == 0 ? null : matching.Max(item => item.Event.OccurredAt),
                EarliestOccurredAt = matching.Length == 0 ? null : matching.Min(item => item.Event.OccurredAt)
            });
        }

        public Task<bool> CanConnectAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
