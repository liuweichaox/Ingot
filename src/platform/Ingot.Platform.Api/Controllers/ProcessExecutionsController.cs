using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-executions")]
public sealed class ProcessExecutionsController(
    IProcessExecutionService executions,
    IExecutionComparisonService comparisons,
    PlatformUserResolver userResolver) : PlatformApiController
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
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        if (from > to)
            return InvalidRequest("开始时间不能晚于结束时间。");
        if (status is not (null or "" or "all" or "completed" or "active"))
            return InvalidRequest("Status 仅支持 all、completed 或 active。");
        if (limit is < 1 or > 1000)
            return InvalidRequest("Limit 必须在 1 到 1000 之间。");
        if (offset < 0)
            return InvalidRequest("Offset 不能小于 0。");
        if (search?.Length > 128)
            return InvalidRequest("搜索词不能超过 128 个字符。");

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
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        if (string.IsNullOrWhiteSpace(executionId) || executionId.Length > 200)
            return InvalidRequest("运行编号格式不正确。");

        var result = await comparisons.GetProcessExecutionAsync(executionId.Trim(), ct).ConfigureAwait(false);
        return result is null
            ? ResourceNotFound("未找到对应运行的分析记录。")
            : Ok(result);
    }
}
