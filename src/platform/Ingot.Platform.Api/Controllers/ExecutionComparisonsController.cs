
using Ingot.Contracts.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessExecutions;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/execution-comparisons")]
public sealed class ExecutionComparisonsController(
    IExecutionComparisonService comparisons,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpPost]
    public async Task<IActionResult> Post(
        [FromBody] ExecutionSelectionComparisonRequest request,
        CancellationToken ct = default,
        [FromQuery] string? siteId = null)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, false, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("比较过程执行必须指定当前身份有权访问的 siteId。");
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
            return InvalidRequest("请选择至少两个过程执行，并从中指定一个基准过程执行。");
        }
        try
        {
            var result = await comparisons.CompareSelectedAsync(
                baselineProcessExecutionId,
                executionIds,
                ct,
                authorizedSiteId,
                request.AdditionalKnownUnmeasuredConfounders).ConfigureAwait(false);
            return result is null ? ResourceNotFound("部分生产过程执行不存在。") : Ok(result);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }

    [HttpGet("{executionId}")]
    public async Task<IActionResult> Get(
        string executionId,
        [FromQuery] int limit = 12,
        CancellationToken ct = default,
        [FromQuery] string? siteId = null,
        [FromQuery] string[]? knownUnmeasuredConfounder = null)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, false, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("比较过程执行必须指定当前身份有权访问的 siteId。");
        if (string.IsNullOrWhiteSpace(executionId) || executionId.Length > 200)
            return InvalidRequest("ExecutionId 格式不正确。");
        if (limit < 1)
            return InvalidRequest("Limit 必须大于 0。");
        var result = await comparisons.CompareWithHistoryAsync(
            executionId.Trim(),
            limit,
            ct,
            authorizedSiteId,
            knownUnmeasuredConfounder).ConfigureAwait(false);
        return result is null ? ResourceNotFound("未找到基准过程执行。") : Ok(result);
    }
}
