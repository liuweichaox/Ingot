using System.Text.Json;
using Ingot.Central.Api.Events;
using Ingot.Central.Infrastructure.Events;
using Ingot.Contracts.Events;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
public sealed class EventsController(
    ICentralEventStore store,
    EdgeTokenValidator tokenValidator) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("/api/v1/events:batch")]
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

        return Ok(await store.IngestAsync(normalized, ct).ConfigureAwait(false));
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
            limit);
        if (!TryValidateQuery(query, out var error))
            return BadRequest(new { error });

        var events = await store.QueryAsync(query, ct).ConfigureAwait(false);
        return Ok(new
        {
            data = events,
            count = events.Count,
            nextIngestId = events.Count == 0
                ? afterIngestId
                : events.Max(static item => item.IngestId)
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

        var initialQuery = BuildQuery(
            edgeId,
            eventType,
            subjectType,
            subjectId,
            correlationId,
            from,
            to,
            cursor,
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
                BuildQuery(edgeId, eventType, subjectType, subjectId, correlationId, from, to, cursor, 100),
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
        var correlated = await store.QueryAsync(
            BuildQuery(null, null, null, null, correlationId, null, null, null, 500),
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
        var sameSubjectWindow = await store.QueryAsync(
                BuildQuery(
                    first.EdgeId,
                    null,
                    first.Event.Subject.Type,
                    first.Event.Subject.Id,
                    null,
                    startedAt,
                    windowEnd,
                    null,
                    500),
                ct)
            .ConfigureAwait(false);
        var ordered = pair
            .Concat(sameSubjectWindow)
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

    private CentralEventQuery BuildQuery(
        string? edgeId,
        string? eventType,
        string? subjectType,
        string? subjectId,
        string? correlationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        long? afterIngestId,
        int limit)
    {
        var context = Request.Query
            .Where(static pair => pair.Key.StartsWith("ctx.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static pair => pair.Key[4..],
                static pair => pair.Value.ToString(),
                StringComparer.Ordinal);
        return new CentralEventQuery
        {
            EdgeId = edgeId,
            EventType = eventType,
            SubjectType = subjectType,
            SubjectId = subjectId,
            CorrelationId = correlationId,
            From = from,
            To = to,
            AfterIngestId = afterIngestId,
            Limit = limit,
            Context = context
        };
    }

    private static bool TryValidateQuery(CentralEventQuery query, out string error)
        => EventQueryContractValidator.TryValidate(query, query.AfterIngestId, out error);
}
