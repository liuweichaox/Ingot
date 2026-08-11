using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-executions")]
public sealed class ProcessExecutionsController(
    IProcessExecutionService executions,
    IExecutionComparisonService comparisons,
    PlatformUserResolver userResolver) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? productFamilyCode,
        [FromQuery] string? productCode,
        [FromQuery] string? processSpecificationId,
        [FromQuery] string? equipmentId,
        [FromQuery] string? edgeId,
        [FromQuery] string? outputItemId,
        [FromQuery] string? externalBatchRef,
        [FromQuery] string? executionId,
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int limit = 200,
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
        if (status is not (null or "" or "all" or "completed" or "active"))
            return BadRequest(new { error = "Status 仅支持 all、completed 或 active。" });
        if (limit is < 1 or > 1000)
            return BadRequest(new { error = "Limit 必须在 1 到 1000 之间。" });
        if (offset < 0)
            return BadRequest(new { error = "Offset 不能小于 0。" });
        if (search?.Length > 128)
            return BadRequest(new { error = "搜索词不能超过 128 个字符。" });

        var result = await executions.QueryAsync(
            from,
            to,
            productFamilyCode,
            productCode,
            processSpecificationId,
            equipmentId,
            outputItemId,
            executionId,
            status,
            limit,
            offset,
            search,
            ct,
            edgeId,
            externalBatchRef).ConfigureAwait(false);
        return Ok(result);
    }

    [HttpGet("{executionId}/analysis")]
    public async Task<IActionResult> GetAnalysis(
        string executionId,
        CancellationToken ct = default)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return Unauthorized(new { error = "需要平台统一认证。" });
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return Forbid();
        if (string.IsNullOrWhiteSpace(executionId) || executionId.Length > 200)
            return BadRequest(new { error = "运行编号格式不正确。" });

        var result = await comparisons.GetProcessExecutionAsync(executionId.Trim(), ct).ConfigureAwait(false);
        return result is null
            ? NotFound(new { error = "未找到对应运行的分析记录。" })
            : Ok(result);
    }
}
