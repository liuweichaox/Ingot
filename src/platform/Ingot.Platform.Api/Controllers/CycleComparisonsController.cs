using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Cycles;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/cycle-comparisons")]
public sealed class CycleComparisonsController(
    ICycleComparisonService comparisons,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet("{correlationId}")]
    public async Task<IActionResult> Get(
        string correlationId,
        [FromQuery] int limit = 12,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 200)
            return BadRequest(new { error = "CorrelationId 格式不正确。" });
        if (limit is < 1 or > 50)
            return BadRequest(new { error = "Limit 必须在 1 到 50 之间。" });
        var result = await comparisons.CompareWithHistoryAsync(correlationId.Trim(), limit, ct).ConfigureAwait(false);
        return result is null ? NotFound(new { error = "未找到基准周期。" }) : Ok(result);
    }
}
