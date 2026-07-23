using Ingot.Contracts.Analytics;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Analytics;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/quality-analysis")]
public sealed class QualityAnalysisController(
    IQualityAnalysisService analysis,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? productSeries,
        [FromQuery] string? subjectType,
        [FromQuery] string? subjectId,
        [FromQuery] string? outcome,
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
        if (limit is < 1 or > 1000)
            return BadRequest(new { error = "Limit 必须在 1 到 1000 之间。" });
        if (offset < 0)
            return BadRequest(new { error = "Offset 不能小于 0。" });
        var normalizedOutcome = Normalize(outcome)?.ToUpperInvariant();
        if (normalizedOutcome is not (null or "PASS" or "FAIL" or "INCONCLUSIVE"))
            return BadRequest(new { error = "Outcome 仅支持 PASS、FAIL 或 INCONCLUSIVE。" });

        return Ok(await analysis.QueryAsync(new QualityAnalysisQuery
        {
            ProductSeries = Normalize(productSeries),
            SubjectType = Normalize(subjectType)?.ToLowerInvariant(),
            SubjectId = Normalize(subjectId),
            Outcome = normalizedOutcome,
            From = from?.ToUniversalTime(),
            To = to?.ToUniversalTime(),
            Limit = limit,
            Offset = offset
        }, ct).ConfigureAwait(false));
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
