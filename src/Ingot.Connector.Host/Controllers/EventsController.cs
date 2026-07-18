using System.Text.Json;
using Ingot.Application.Abstractions;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Connector.Host.Controllers;

/// <summary>
///     边缘本地生产事件查询与 SSE 订阅。
/// </summary>
[ApiController]
[Route("api/v1/events")]
public sealed class EventsController(IEventLog eventLog) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery(Name = "type")] string? eventType,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] string? correlationId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] long? afterSeq,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var query = BuildQuery(
            eventType,
            subjectType,
            subjectId,
            correlationId,
            from,
            to,
            afterSeq,
            limit);
        if (!TryValidateQuery(query, out var error))
            return BadRequest(new { error });
        var events = await eventLog.QueryAsync(query, ct).ConfigureAwait(false);

        return Ok(new
        {
            data = events,
            count = events.Count,
            nextSeq = events.Count == 0 ? afterSeq : events.Max(static evt => evt.Seq)
        });
    }

    /// <summary>
    ///     按关联 ID 返回一个生产周期内已经落盘的全部事实。
    /// </summary>
    [HttpGet("/api/v1/cycles/{correlationId}")]
    public async Task<IActionResult> GetCycle(
        string correlationId,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return BadRequest(new { error = "correlationId 不能为空。" });

        var events = await eventLog.QueryAsync(
                new EventQuery
                {
                    CorrelationId = correlationId,
                    Limit = limit
                },
                ct)
            .ConfigureAwait(false);

        var pair = events.OrderBy(static evt => evt.Seq).ToArray();
        if (pair.Length == 0)
            return NotFound(new { correlationId, error = "未找到对应生产周期。" });

        var startedAt = pair.Min(static evt => evt.OccurredAt);
        var completedAt = pair
            .Where(static evt =>
                evt.EventType.EndsWith(".completed", StringComparison.Ordinal) ||
                evt.EventType.EndsWith(".cleared", StringComparison.Ordinal) ||
                evt.EventType.EndsWith(".exited", StringComparison.Ordinal))
            .Select(static evt => (DateTimeOffset?)evt.OccurredAt)
            .LastOrDefault();
        var windowEnd = completedAt ?? pair.Max(static evt => evt.OccurredAt);
        var subject = pair[0].Subject;
        var sameSubjectWindow = await eventLog.QueryAsync(
                new EventQuery
                {
                    SubjectType = subject.Type,
                    SubjectId = subject.Id,
                    From = startedAt,
                    To = windowEnd,
                    Limit = limit
                },
                ct)
            .ConfigureAwait(false);
        var ordered = pair
            .Concat(sameSubjectWindow)
            .DistinctBy(static evt => evt.EventId)
            .OrderBy(static evt => evt.OccurredAt)
            .ThenBy(static evt => evt.Seq)
            .ToArray();

        return Ok(new
        {
            correlationId,
            subject,
            startedAt,
            completedAt,
            durationMs = completedAt.HasValue
                ? (completedAt.Value - startedAt).TotalMilliseconds
                : (double?)null,
            events = ordered
        });
    }

    [HttpGet("stream")]
    public async Task Stream(
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
            var query = BuildQuery(
                eventType,
                subjectType,
                subjectId,
                correlationId,
                from,
                to,
                cursor,
                100);
            var events = await eventLog.QueryAsync(query, ct).ConfigureAwait(false);

            foreach (var evt in events.OrderBy(static evt => evt.Seq))
            {
                await Response.WriteAsync($"id: {evt.Seq}\n", ct).ConfigureAwait(false);
                await Response.WriteAsync($"event: {evt.EventType}\n", ct).ConfigureAwait(false);
                await Response.WriteAsync(
                    $"data: {JsonSerializer.Serialize(evt, JsonOptions)}\n\n",
                    ct).ConfigureAwait(false);
                cursor = evt.Seq;
            }

            if (events.Count == 0)
            {
                await Response.WriteAsync(": keep-alive\n\n", ct).ConfigureAwait(false);
            }

            await Response.Body.FlushAsync(ct).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
        }
    }

    private EventQuery BuildQuery(
        string? eventType,
        string? subjectType,
        string? subjectId,
        string? correlationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        long? afterSeq,
        int limit)
    {
        var context = Request.Query
            .Where(static pair => pair.Key.StartsWith("ctx.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                static pair => pair.Key[4..],
                static pair => pair.Value.ToString(),
                StringComparer.Ordinal);

        return new EventQuery
        {
            EventType = eventType,
            SubjectType = subjectType,
            SubjectId = subjectId,
            CorrelationId = correlationId,
            From = from,
            To = to,
            AfterSeq = afterSeq,
            Limit = limit,
            Context = context
        };
    }

    private static bool TryValidateQuery(EventQuery query, out string error)
        => EventQueryContractValidator.TryValidate(query, query.AfterSeq, out error);
}
