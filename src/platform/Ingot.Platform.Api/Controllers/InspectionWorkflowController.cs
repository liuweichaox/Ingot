
// 提供有扫描边界的站点待检任务与汇总接口。
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-tasks")]
public sealed class InspectionWorkflowController(
    IInspectionWorkflowService workflow,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        CancellationToken ct = default,
        [FromQuery] string? siteId = null)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, true, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("必须指定当前身份有权访问的 siteId。");
        try
        {
            return Ok(await workflow.GetSummaryAsync(ct, authorizedSiteId).ConfigureAwait(false));
        }
        catch (InspectionQueryLimitExceededException exception)
        {
            return UnprocessableRequest(exception.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100,
        [FromQuery] int offset = 0,
        CancellationToken ct = default,
        [FromQuery] string? siteId = null)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        if (!identity.HasAnyRole(PlatformRoles.QualityRead))
            return AuthorizationDenied();
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, true, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("必须指定当前身份有权访问的 siteId。");
        if (limit is < 1 or > 500)
            return InvalidRequest("Limit 必须在 1 到 500 之间。");
        if (offset < 0)
            return InvalidRequest("Offset 不能小于 0。");
        var normalizedStatus = status?.Trim().ToLowerInvariant();
        if (normalizedStatus is not (null or "all" or "pending" or "in_progress" or "review_pending" or "completed"))
            return InvalidRequest("Status 不在支持范围内。");
        InspectionTaskPage page;
        try
        {
            page = await workflow.QueryTaskPageAsync(
                normalizedStatus, offset, limit, ct, authorizedSiteId).ConfigureAwait(false);
        }
        catch (InspectionQueryLimitExceededException exception)
        {
            return UnprocessableRequest(exception.Message);
        }
        return Ok(new { page.Data, count = page.Data.Count, page.Total, page.Offset, page.Limit });
    }
}
