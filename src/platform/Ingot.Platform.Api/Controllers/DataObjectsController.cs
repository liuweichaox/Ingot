using Ingot.Contracts.Analytics;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Events;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/data-objects")]
public sealed class DataObjectsController(
    IPlatformEventStore events,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        if (from > to)
            return BadRequest(new { error = "开始时间不能晚于结束时间。" });
        if (limit is < 1 or > 500)
            return BadRequest(new { error = "Limit 必须在 1 到 500 之间。" });
        if (offset < 0)
            return BadRequest(new { error = "Offset 不能小于 0。" });

        return Ok(await events.QueryDataObjectsAsync(new DataObjectQuery
        {
            SubjectType = Normalize(subjectType)?.ToLowerInvariant(),
            SubjectId = Normalize(subjectId),
            From = from?.ToUniversalTime(),
            To = to?.ToUniversalTime(),
            Limit = limit,
            Offset = offset
        }, ct).ConfigureAwait(false));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
