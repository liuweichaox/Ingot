
// 管理按站点隔离的质量分析范围。
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-scopes")]
public sealed class InspectionScopesController(
    InspectionQueries queries,
    InspectionCommands commands,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet]
    public async Task<IActionResult> List(
        CancellationToken ct = default,
        [FromQuery] string? siteId = null)
    {
        var denied = Denied(readOnly: true);
        if (denied is not null) return denied;
        var identity = userResolver.ResolveIdentity(User)!;
        var siteFailure = PlatformSiteScope.Resolve(identity, siteId, true, out var authorizedSiteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("必须指定当前身份有权访问的 siteId。");
        return Ok(new { data = await queries.ListScopesAsync(ct, authorizedSiteId).ConfigureAwait(false) });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] InspectionScope? request, CancellationToken ct = default)
    {
        var denied = Denied(readOnly: false);
        if (denied is not null) return denied;
        var identity = userResolver.ResolveIdentity(User)!;
        var siteFailure = PlatformSiteScope.Resolve(identity, request?.SiteId, false, out var siteId);
        if (siteFailure == SiteScopeFailure.Forbidden)
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", request?.SiteId));
        if (siteFailure == SiteScopeFailure.Missing)
            return InvalidRequest("创建质量范围必须指定当前身份有权访问的 siteId。");
        var result = await commands.UpsertScopeAsync(
            request is null ? null : request with { SiteId = siteId! }, identity.UserId, ct).ConfigureAwait(false);
        return result.Status switch
        {
            InspectionCommandStatus.Success => Ok(result.Value),
            InspectionCommandStatus.Invalid => InvalidRequest(result.Error),
            InspectionCommandStatus.Conflict => StateConflict(result.Error, ("existing", result.Existing)),
            _ => ServerFailure()
        };
    }

    [HttpDelete("{scopeId}")]
    public async Task<IActionResult> Delete(string scopeId, CancellationToken ct = default)
    {
        var denied = Denied(readOnly: false);
        if (denied is not null) return denied;
        var identity = userResolver.ResolveIdentity(User)!;
        var scope = await queries.GetScopeAsync(scopeId, ct).ConfigureAwait(false);
        if (scope is not null && !identity.CanAccessSite(scope.SiteId))
            return ResourceNotFound();
        var result = await commands.DeleteScopeAsync(scopeId, ct).ConfigureAwait(false);
        return result.Status switch
        {
            InspectionCommandStatus.Success => NoContent(),
            InspectionCommandStatus.Conflict => StateConflict(result.Error),
            InspectionCommandStatus.NotFound => ResourceNotFound(),
            _ => ServerFailure()
        };
    }

    private IActionResult? Denied(bool readOnly)
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null) return AuthenticationRequired("需要平台统一认证。");
        if (readOnly) return identity.HasAnyRole(PlatformRoles.QualityRead) ? null : AuthorizationDenied();
        return identity.HasAnyRole(
            PlatformRoles.QualityInspector,
            PlatformRoles.QualityReviewer,
            PlatformRoles.ProcessEngineer,
            PlatformRoles.PlatformAdministrator) ? null : AuthorizationDenied();
    }

}
