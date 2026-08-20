using System.Text.Json;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Api.Errors;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessExecutions;
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
    IOptions<PlatformEventOptions> eventOptions,
    PlatformUserResolver userResolver,
    IExecutionBoundaryStore executionBoundaries,
    PlatformEventMetrics metrics,
    ILogger<EventsController> logger) : PlatformApiController
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly PlatformEventOptions _eventOptions = eventOptions.Value;

    [HttpPost(PlatformEventRoutes.AbsoluteBatchIngest)]
    [AllowAnonymous]
    public async Task<IActionResult> Ingest(
        [FromBody] EventBatchRequest? request,
        CancellationToken ct)
    {
        if (!EventBatchValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        if (!tokenValidator.IsAuthorized(
                normalized!.SiteId,
                normalized!.EdgeId,
                Request.Headers.Authorization.FirstOrDefault()))
        {
            return AuthenticationRequired("Edge token 无效。");
        }

        if (!PlatformIngestWindow.TryValidate(normalized, _eventOptions, DateTimeOffset.UtcNow, out var windowError))
            return InvalidRequest(windowError);

        try
        {
            var response = await store.IngestAsync(normalized, ct).ConfigureAwait(false);
            return Ok(response);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
        catch (EventIngestConflictException exception)
        {
            metrics.RecordPayloadConflict(normalized.SiteId, normalized.EdgeId);
            logger.LogError(
                exception,
                "生产事件幂等键载荷冲突：Site={SiteId}, Edge={EdgeId}, FirstSeq={FirstSeq}, EventCount={EventCount}",
                normalized.SiteId,
                normalized.EdgeId,
                normalized.Events.Min(static item => item.Seq),
                normalized.Events.Count);
            return StateConflict(
                $"{exception.Message}。该冲突不会通过重试恢复；若本地 outbox 已重建，请更换 EdgeId。");
        }
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? siteId,
        [FromQuery] string? edgeId,
        [FromQuery(Name = "type")] string? eventType,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] string? executionId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] long? afterIngestId,
        [FromQuery] long? beforeIngestId,
        [FromQuery] int offset = 0,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var siteAccess = ResolveSiteAccess(siteId);
        if (siteAccess.Denied is not null)
            return siteAccess.Denied;
        var query = BuildQuery(
            siteAccess.SiteId,
            edgeId,
            eventType,
            subjectType,
            subjectId,
            executionId,
            from,
            to,
            afterIngestId,
            beforeIngestId,
            limit,
            offset);
        if (!TryValidateQuery(query, out var error))
            return InvalidRequest(error);

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
        [FromQuery] string? siteId,
        [FromQuery] string? edgeId,
        [FromQuery(Name = "type")] string? eventType,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] string? executionId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] long? afterIngestId,
        CancellationToken ct)
    {
        var siteAccess = ResolveSiteAccess(siteId);
        if (siteAccess.Denied is not null)
        {
            var denied = (ObjectResult)siteAccess.Denied;
            Response.StatusCode = denied.StatusCode ?? StatusCodes.Status403Forbidden;
            Response.ContentType = "application/problem+json";
            await Response.WriteAsJsonAsync(denied.Value, ct).ConfigureAwait(false);
            return;
        }
        siteId = siteAccess.SiteId;
        if (!EventQueryContractValidator.TryParseCursor(
                Request.Headers["Last-Event-ID"].FirstOrDefault(),
                out var cursor))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.ContentType = "application/problem+json";
            await Response.WriteAsJsonAsync(
                ApiProblemDetailsFactory.Create(
                    HttpContext,
                    StatusCodes.Status400BadRequest,
                    "Last-Event-ID 必须是非负整数。"),
                ct).ConfigureAwait(false);
            return;
        }

        if (afterIngestId is < 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.ContentType = "application/problem+json";
            await Response.WriteAsJsonAsync(
                    ApiProblemDetailsFactory.Create(
                        HttpContext,
                        StatusCodes.Status400BadRequest,
                        "afterIngestId 不能小于 0。"),
                    ct)
                .ConfigureAwait(false);
            return;
        }
        cursor = Math.Max(cursor ?? 0, afterIngestId ?? 0);

        var initialQuery = BuildQuery(
            siteId,
            edgeId,
            eventType,
            subjectType,
            subjectId,
            executionId,
            from,
            to,
            cursor,
            null,
            100);
        if (!TryValidateQuery(initialQuery, out var validationError))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            Response.ContentType = "application/problem+json";
            await Response.WriteAsJsonAsync(
                ApiProblemDetailsFactory.Create(
                    HttpContext,
                    StatusCodes.Status400BadRequest,
                    validationError),
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
                BuildQuery(
                    siteId, edgeId, eventType, subjectType, subjectId, executionId,
                    from, to, cursor, null, 100),
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

    [HttpGet("/api/v1/process-executions/{executionId}")]
    public async Task<IActionResult> GetProcessExecution(
        string executionId,
        [FromQuery] string? siteId,
        CancellationToken ct)
    {
        var siteAccess = ResolveSiteAccess(siteId, requireExplicitForAdministrator: true);
        if (siteAccess.Denied is not null)
            return siteAccess.Denied;
        var correlated = await QueryAllAsync(
            BuildQuery(siteAccess.SiteId, null, null, null, null, executionId, null, null, null, null, 500),
            ct).ConfigureAwait(false);
        var pair = correlated
            .OrderBy(static item => item.Event.OccurredAt)
            .ThenBy(static item => item.IngestId)
            .ToArray();
        if (pair.Length == 0)
            return ResourceNotFound("未找到对应生产过程执行。", ("executionId", executionId));

        var first = pair[0];
        var boundary = await executionBoundaries.GetBoundaryAsync(
            siteAccess.SiteId!, executionId, ct).ConfigureAwait(false);
        var startedAt = boundary?.StartedAt ?? pair.Min(static item => item.Event.OccurredAt);
        var completedAt = boundary?.EndedAt ?? pair
            .Where(static item => item.Event.EventType == "process.execution.completed")
            .Select(static item => (DateTimeOffset?)item.Event.OccurredAt)
            .LastOrDefault();
        var windowEnd = completedAt ?? pair.Max(static item => item.Event.OccurredAt);
        var sameSubjectWindow = await QueryAllAsync(
                BuildQuery(
                    first.SiteId,
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
                string.IsNullOrWhiteSpace(item.Event.ExecutionId) ||
                string.Equals(
                    item.Event.ExecutionId,
                    executionId,
                    StringComparison.Ordinal)))
            .DistinctBy(static item => item.Event.EventId)
            .OrderBy(static item => item.Event.OccurredAt)
            .ThenBy(static item => item.IngestId)
            .ToArray();

        return Ok(new
        {
            executionId,
            siteId = first.SiteId,
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

    private (string? SiteId, IActionResult? Denied) ResolveSiteAccess(
        string? requestedSiteId,
        bool requireExplicitForAdministrator = false)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return (null, AuthenticationRequired("需要平台统一认证。"));

        var normalized = requestedSiteId?.Trim();
        if (!string.IsNullOrWhiteSpace(normalized))
            return identity.CanAccessSite(normalized)
                ? (normalized, null)
                : (null, AuthorizationDenied("当前身份无权访问该站点。", ("siteId", normalized)));

        if (identity.Roles.Contains(PlatformRoles.PlatformAdministrator) && !requireExplicitForAdministrator)
            return (null, null);
        if (identity.SiteIds.Count == 1)
            return (identity.SiteIds.Single(), null);
        return (null, InvalidRequest("必须指定当前身份有权访问的 siteId。"));
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
                throw new InvalidOperationException("完整次执行查询的摄入游标没有前进。");

            result.AddRange(page);
            cursor = nextCursor;
            if (page.Count < pageSize)
                break;
        }

        return result;
    }

    private PlatformEventQuery BuildQuery(
        string? siteId,
        string? edgeId,
        string? eventType,
        string? subjectType,
        string? subjectId,
        string? executionId,
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
            SiteId = siteId,
            EdgeId = edgeId,
            EventType = eventType,
            SubjectType = subjectType,
            SubjectId = subjectId,
            ExecutionId = executionId,
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
