using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Contracts.Events;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/execution-comparisons")]
public sealed class ExecutionComparisonsController(
    IExecutionComparisonService comparisons,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] ExecutionSelectionComparisonRequest request,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        var baselineProcessExecutionId = request.BaselineProcessExecutionId?.Trim();
        var executionIds = request.ProcessExecutionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(baselineProcessExecutionId) || baselineProcessExecutionId.Length > 200 ||
            executionIds.Length < 2 || executionIds.Any(static id => id.Length > 200) ||
            !executionIds.Contains(baselineProcessExecutionId, StringComparer.Ordinal))
        {
            return BadRequest(new { error = "请选择至少两个过程执行，并从中指定一个基准过程执行。" });
        }
        try
        {
            var result = await comparisons.CompareSelectedAsync(baselineProcessExecutionId, executionIds, ct).ConfigureAwait(false);
            return result is null ? NotFound(new { error = "部分生产过程执行不存在。" }) : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }

    [HttpGet("{executionId}")]
    public async Task<IActionResult> Get(
        string executionId,
        [FromQuery] int limit = 12,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        if (string.IsNullOrWhiteSpace(executionId) || executionId.Length > 200)
            return BadRequest(new { error = "ExecutionId 格式不正确。" });
        if (limit < 1)
            return BadRequest(new { error = "Limit 必须大于 0。" });
        var result = await comparisons.CompareWithHistoryAsync(executionId.Trim(), limit, ct).ConfigureAwait(false);
        return result is null ? NotFound(new { error = "未找到基准过程执行。" }) : Ok(result);
    }
}
