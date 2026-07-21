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

    private static PlatformProductionEvent Row(
        long ingestId,
        string eventType,
        DateTimeOffset occurredAt)
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
                CorrelationId = "cycle-1",
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

            var ordered = query.AfterIngestId.HasValue
                ? filtered.OrderBy(item => item.IngestId)
                : filtered.OrderByDescending(item => item.IngestId);
            return Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(
                ordered.Take(query.Limit).ToArray());
        }

        public Task<PlatformEventScopeStats> GetScopeStatsAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> CanConnectAsync(CancellationToken ct = default)
            => Task.FromResult(true);
    }
}
