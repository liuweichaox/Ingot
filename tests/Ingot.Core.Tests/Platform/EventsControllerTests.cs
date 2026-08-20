using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Api.Errors;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using System.Security.Claims;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class EventsControllerTests
{
    private static readonly PlatformEventMetrics Metrics = new();
    [Fact]
    public async Task Ingest_RejectsTokenWhenEdgeClaimsAnotherSite()
    {
        var store = new StubPlatformEventStore([]);
        var options = Options.Create(new PlatformEventOptions
        {
            RequireToken = true,
            EdgeTokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EDGE-001"] = "edge-secret"
            },
            EdgeSites = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["EDGE-001"] = "SITE-001"
            }
        });
        var controller = CreateController(store, options);
        controller.Request.Headers.Authorization = "Bearer edge-secret";
        var evt = ProductionEvent.Create(
            "equipment.heartbeat",
            DateTimeOffset.UtcNow,
            "edge/EDGE-001/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01")) with { Seq = 1 };

        var action = await controller.Ingest(new EventBatchRequest
        {
            SiteId = "SITE-002",
            EdgeId = "EDGE-001",
            Events = [evt]
        }, CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status401Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Ingest_ReturnsConflictForUnrecoverableEdgeIdentityCollision()
    {
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var store = new StubPlatformEventStore(
            [],
            new EventIngestConflictException("事件幂等键或载荷冲突"));
        var controller = CreateController(store, options);
        var evt = ProductionEvent.Create(
            "equipment.heartbeat",
            DateTimeOffset.UtcNow,
            "edge/EDGE-001/equipment/PRESS-01",
            new ObjectRef("equipment", "PRESS-01")) with { Seq = 1 };

        var action = await controller.Ingest(new EventBatchRequest
        {
            SiteId = "SITE-001",
            EdgeId = "EDGE-001",
            Events = [evt]
        }, CancellationToken.None);

        var conflict = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Contains("更换 EdgeId", Assert.IsType<ApiProblemDetails>(conflict.Value).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_UsesBeforeCursorForOlderPages()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var store = new StubPlatformEventStore(Enumerable.Range(1, 10)
            .Select(index => Row(index, "process.sample", startedAt.AddSeconds(index)))
            .ToArray());
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = CreateController(store, options);

        var action = await controller.Query(
            null, null, null, null, null, null, null, null, null, 8, 0, 3, CancellationToken.None);

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
        var controller = CreateController(store, options);

        var action = await controller.Query(
            null, null, null, null, null, null, null, null, null, null, 3, 3, CancellationToken.None);

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
        var controller = CreateController(new StubPlatformEventStore([]), options);

        var action = await controller.Query(
            null, null, null, null, null, null, null, null, 1, 8, 0, 3, CancellationToken.None);

        var invalid = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status400BadRequest, invalid.StatusCode);
        Assert.Equal("request.invalid", Assert.IsType<ApiProblemDetails>(invalid.Value).Code);
    }

    [Fact]
    public async Task Query_RejectsSiteOutsideAuthenticatedScope()
    {
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = CreateController(new StubPlatformEventStore([]), options);

        var action = await controller.Query(
            "SITE-002", null, null, null, null, null, null, null, null, null, 0, 100,
            CancellationToken.None);

        var denied = Assert.IsType<ObjectResult>(action);
        Assert.Equal(StatusCodes.Status403Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task GetProcessExecution_PagesThroughMoreThanFiveHundredEvents()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var rows = Enumerable.Range(1, 602)
            .Select(index => Row(
                index,
                index == 1 ? "process.execution.started" :
                index == 602 ? "process.execution.completed" : "process.sample",
                startedAt.AddSeconds(index)))
            .ToArray();
        var store = new StubPlatformEventStore(rows);
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = CreateController(store, options);

        var action = await controller.GetProcessExecution("execution-1", "SITE-001", CancellationToken.None);

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
    public async Task GetProcessExecution_ExcludesEventsBelongingToAnAdjacentProcessExecution()
    {
        var startedAt = DateTimeOffset.Parse("2026-07-18T10:00:00Z");
        var rows = new[]
        {
            Row(1, "process.execution.started", startedAt, "execution-1"),
            Row(2, "process.sample", startedAt.AddSeconds(1), "execution-1"),
            Row(3, "alarm.raised", startedAt.AddSeconds(2), null),
            Row(4, "process.execution.started", startedAt.AddSeconds(3), "execution-2"),
            Row(5, "process.sample", startedAt.AddSeconds(4), "execution-2"),
            Row(6, "process.execution.completed", startedAt.AddSeconds(5), "execution-1")
        };
        var store = new StubPlatformEventStore(rows);
        var options = Options.Create(new PlatformEventOptions { RequireToken = false });
        var controller = CreateController(store, options);

        var action = await controller.GetProcessExecution("execution-1", "SITE-001", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action);
        var json = JsonSerializer.SerializeToElement(ok.Value);
        var events = json.GetProperty("events").EnumerateArray().ToArray();
        Assert.Equal(4, events.Length);
        Assert.DoesNotContain(
            events,
            item => item.GetProperty("Event")
                .GetProperty("ExecutionId")
                .GetString() == "execution-2");
    }

    private static PlatformProductionEvent Row(
        long ingestId,
        string eventType,
        DateTimeOffset occurredAt,
        string? executionId = "execution-1")
        => new()
        {
            IngestId = ingestId,
            SiteId = "SITE-001",
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
                ExecutionId = executionId,
                Seq = ingestId
            }
        };

    private static EventsController CreateController(
        IPlatformEventStore store,
        IOptions<PlatformEventOptions> options)
    {
        var controller = new EventsController(
            store,
            new EdgeTokenValidator(options),
            options,
            new PlatformUserResolver(new TestHostEnvironment()),
            new MissingBoundaryStore(),
            Metrics,
            NullLogger<EventsController>.Instance);
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "engineer-1"),
            new Claim(ClaimTypes.Role, PlatformRoles.ProcessEngineer),
            new Claim(PlatformClaimTypes.SiteId, "SITE-001")
        ], "test");
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };
        return controller;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ingot.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class MissingBoundaryStore : IExecutionBoundaryStore
    {
        public Task<ExecutionBoundary?> GetBoundaryAsync(string siteId, string sourceExecutionId, CancellationToken ct)
            => Task.FromResult<ExecutionBoundary?>(null);
        public Task SaveBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct) => Task.CompletedTask;
        public Task UpdateBoundaryAsync(ExecutionBoundary boundary, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutionBoundary>> QueryBoundariesAsync(
            string siteId, DateTimeOffset? from, DateTimeOffset? to, int limit = 100, int offset = 0,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutionBoundary>>([]);
    }

    private sealed class StubPlatformEventStore(
        IReadOnlyList<PlatformProductionEvent> rows,
        Exception? ingestException = null) : IPlatformEventStore
    {
        public int QueryCalls { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<EventBatchResponse> IngestAsync(
            EventBatchRequest request,
            CancellationToken ct = default)
            => ingestException is null
                ? Task.FromResult(new EventBatchResponse
                {
                    Accepted = request.Events.Count,
                    AckSeq = request.Events.Count == 0 ? 0 : request.Events.Max(static item => item.Seq)
                })
                : Task.FromException<EventBatchResponse>(ingestException);

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            QueryCalls++;
            IEnumerable<PlatformProductionEvent> filtered = rows;
            if (!string.IsNullOrWhiteSpace(query.EdgeId))
                filtered = filtered.Where(item => item.EdgeId == query.EdgeId);
            if (!string.IsNullOrWhiteSpace(query.ExecutionId))
                filtered = filtered.Where(item => item.Event.ExecutionId == query.ExecutionId);
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
            if (!string.IsNullOrWhiteSpace(query.ExecutionId))
                filtered = filtered.Where(item => item.Event.ExecutionId == query.ExecutionId);
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
