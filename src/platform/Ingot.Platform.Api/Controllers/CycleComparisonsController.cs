using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Contracts.Events;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/cycle-comparisons")]
public sealed class CycleComparisonsController(
    ICycleComparisonService comparisons,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] CycleSelectionComparisonRequest request,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        var baselineCycleId = request.BaselineCycleId?.Trim();
        var cycleIds = request.CycleIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(baselineCycleId) || baselineCycleId.Length > 200 ||
            cycleIds.Length < 2 || cycleIds.Any(static id => id.Length > 200) ||
            !cycleIds.Contains(baselineCycleId, StringComparer.Ordinal))
        {
            return BadRequest(new { error = "请选择至少两个周期，并从中指定一个基准周期。" });
        }
        try
        {
            var result = await comparisons.CompareSelectedAsync(baselineCycleId, cycleIds, ct).ConfigureAwait(false);
            return result is null ? NotFound(new { error = "部分生产周期不存在。" }) : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

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
        if (limit < 1)
            return BadRequest(new { error = "Limit 必须大于 0。" });
        var result = await comparisons.CompareWithHistoryAsync(correlationId.Trim(), limit, ct).ConfigureAwait(false);
        return result is null ? NotFound(new { error = "未找到基准周期。" }) : Ok(result);
    }
}
