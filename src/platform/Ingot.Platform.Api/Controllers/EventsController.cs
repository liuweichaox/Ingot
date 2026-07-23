using System.Text.Json;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Contracts.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
public sealed class EventsController(
    IPlatformEventStore store,
    EdgeTokenValidator tokenValidator,
    IOptions<PlatformEventOptions> eventOptions) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PlatformEventOptions _eventOptions = eventOptions.Value;

    [HttpPost("/api/v1/events:batch")]
    [AllowAnonymous]
    public async Task<IActionResult> Ingest(
        [FromBody] EventBatchRequest? request,
        CancellationToken ct)
    {
        if (!EventBatchValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        if (!tokenValidator.IsAuthorized(
                normalized!.EdgeId,
                Request.Headers.Authorization.FirstOrDefault()))
        {
            return Unauthorized(new { error = "Edge token 无效。" });
        }

        if (!PlatformIngestWindow.TryValidate(normalized, _eventOptions, DateTimeOffset.UtcNow, out var windowError))
            return BadRequest(new { error = windowError });

        try
        {
            return Ok(await store.IngestAsync(normalized, ct).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? edgeId,
        [FromQuery(Name = "type")] string? eventType,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] string? correlationId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] long? afterIngestId,
        [FromQuery] long? beforeIngestId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var query = BuildQuery(
            edgeId,
            eventType,
            subjectType,
            subjectId,
            correlationId,
            from,
            to,
            afterIngestId,
            beforeIngestId,
            limit,
            offset);
        if (!TryValidateQuery(query, out var error))
            return BadRequest(new { error });

        var eventsTask = store.QueryAsync(query, ct);
        var statsTask = store.GetScopeStatsAsync(query with { Offset = 0 }, ct);
        await Task.WhenAll(eventsTask, statsTask).ConfigureAwait(false);
        var events = await eventsTask.ConfigureAwait(false);
        var stats = await statsTask.ConfigureAwait(false);
        return Ok(new
        {
            data = events,
            count = events.Count,
            total = stats.Count,
            nextIngestId = events.Count == 0
                ? afterIngestId
                : events.Max(static item => item.IngestId),
            previousIngestId = events.Count == 0
                ? beforeIngestId
                : events.Min(static item => item.IngestId)
        });
    }

    [HttpGet("stream")]
    public async Task Stream(
        [FromQuery] string? edgeId,
        [FromQuery(Name = "type")] string? eventType,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] string? correlationId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] long? afterIngestId,
        CancellationToken ct)
    {
        if (!EventQueryContractValidator.TryParseCursor(
                Request.Headers["Last-Event-ID"].FirstOrDefault(),
                out var cursor))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(
                new { error = "Last-Event-ID 必须是非负整数。" },
                ct).ConfigureAwait(false);
            return;
        }

        if (afterIngestId is < 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(new { error = "afterIngestId 不能小于 0。" }, ct)
                .ConfigureAwait(false);
            return;
        }
        cursor = Math.Max(cursor ?? 0, afterIngestId ?? 0);

        var initialQuery = BuildQuery(
            edgeId,
            eventType,
            subjectType,
            subjectId,
            correlationId,
            from,
            to,
            cursor,
            null,
            100);
        if (!TryValidateQuery(initialQuery, out var validationError))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            await Response.WriteAsJsonAsync(
                new { error = validationError },
                ct).ConfigureAwait(false);
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Connection = "keep-alive";

        while (!ct.IsCancellationRequested)
        {
            var events = await store.QueryAsync(
                BuildQuery(edgeId, eventType, subjectType, subjectId, correlationId, from, to, cursor, null, 100),
                ct).ConfigureAwait(false);
            foreach (var item in events.OrderBy(static item => item.IngestId))
            {
                await Response.WriteAsync($"id: {item.IngestId}\n", ct).ConfigureAwait(false);
                await Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(item, JsonOptions)}\n\n",
                    ct).ConfigureAwait(false);
                cursor = item.IngestId;
            }

            if (events.Count == 0)
                await Response.WriteAsync(": keep-alive\n\n", ct).ConfigureAwait(false);
            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    [HttpGet("/api/v1/cycles/{correlationId}")]
    public async Task<IActionResult> GetCycle(string correlationId, CancellationToken ct)
    {
        var correlated = await QueryAllAsync(
            BuildQuery(null, null, null, null, correlationId, null, null, null, null, 500),
            ct).ConfigureAwait(false);
        var pair = correlated
            .OrderBy(static item => item.Event.OccurredAt)
            .ThenBy(static item => item.IngestId)
            .ToArray();
        if (pair.Length == 0)
            return NotFound(new { correlationId, error = "未找到对应生产周期。" });

        var first = pair[0];
        var startedAt = pair.Min(static item => item.Event.OccurredAt);
        var completedAt = pair
            .Where(static item =>
                item.Event.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
                item.Event.EventType.EndsWith(".cleared", StringComparison.Ordinal) ||
                item.Event.EventType.EndsWith(".exited", StringComparison.Ordinal))
            .Select(static item => (DateTimeOffset?)item.Event.OccurredAt)
            .LastOrDefault();
        var windowEnd = completedAt ?? pair.Max(static item => item.Event.OccurredAt);
        var sameSubjectWindow = await QueryAllAsync(
                BuildQuery(
                    first.EdgeId,
                    null,
                    first.Event.Subject.Type,
                    first.Event.Subject.Id,
                    null,
                    startedAt,
                    windowEnd,
                    null,
                    null,
                    500),
                ct)
            .ConfigureAwait(false);
        var ordered = pair
            .Concat(sameSubjectWindow.Where(item =>
                string.IsNullOrWhiteSpace(item.Event.CorrelationId) ||
                string.Equals(
                    item.Event.CorrelationId,
                    correlationId,
                    StringComparison.Ordinal)))
            .DistinctBy(static item => item.Event.EventId)
            .OrderBy(static item => item.Event.OccurredAt)
            .ThenBy(static item => item.IngestId)
            .ToArray();

        return Ok(new
        {
            correlationId,
            edgeId = first.EdgeId,
            subject = first.Event.Subject,
            startedAt,
            completedAt,
            durationMs = completedAt.HasValue
                ? (completedAt.Value - startedAt).TotalMilliseconds
                : (double?)null,
            events = ordered
        });
    }

    private async Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
        PlatformEventQuery query,
        CancellationToken ct)
    {
        const int pageSize = 500;
        var cursor = query.AfterIngestId ?? 0;
        var result = new List<PlatformProductionEvent>();

        while (true)
        {
            var page = await store.QueryAsync(
                    query with { AfterIngestId = cursor, Limit = pageSize },
                    ct)
                .ConfigureAwait(false);
            if (page.Count == 0)
                break;

            var nextCursor = page.Max(static item => item.IngestId);
            if (nextCursor <= cursor)
                throw new InvalidOperationException("完整周期查询的摄入游标没有前进。");

            result.AddRange(page);
            cursor = nextCursor;
            if (page.Count < pageSize)
                break;
        }

        return result;
    }

    private PlatformEventQuery BuildQuery(
        string? edgeId,
        string? eventType,
        string? subjectType,
        string? subjectId,
        string? correlationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        long? afterIngestId,
        long? beforeIngestId,
        int limit,
        int offset = 0)
    {
        var context = Request.Query
            .Where(static pair => pair.Key.StartsWith("ctx.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static pair => pair.Key[4..],
                static pair => pair.Value.ToString(),
                StringComparer.Ordinal);
        return new PlatformEventQuery
        {
            EdgeId = edgeId,
            EventType = eventType,
            SubjectType = subjectType,
            SubjectId = subjectId,
            CorrelationId = correlationId,
            From = from,
            To = to,
            AfterIngestId = afterIngestId,
            BeforeIngestId = beforeIngestId,
            Offset = offset,
            Limit = limit,
            Context = context
        };
    }

    private static bool TryValidateQuery(PlatformEventQuery query, out string error)
    {
        if (query.Offset < 0)
        {
            error = "offset 不能小于 0。";
            return false;
        }
        if (query.BeforeIngestId is <= 0)
        {
            error = "beforeIngestId 必须大于 0。";
            return false;
        }
        if (query.AfterIngestId.HasValue && query.BeforeIngestId.HasValue)
        {
            error = "afterIngestId 和 beforeIngestId 不能同时使用。";
            return false;
        }
        return EventQueryContractValidator.TryValidate(query, query.AfterIngestId, out error);
    }
}
