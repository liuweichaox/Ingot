// 提供 InspectionScopesController 的 HTTP 传输、认证与响应映射；业务规则由应用层执行。

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
    public async Task<IActionResult> List(CancellationToken ct = default)
    {
        var denied = Denied(readOnly: true);
        return denied ?? Ok(new { data = await queries.ListScopesAsync(ct).ConfigureAwait(false) });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] InspectionScope? request, CancellationToken ct = default)
    {
        var denied = Denied(readOnly: false);
        if (denied is not null) return denied;
        var identity = userResolver.ResolveIdentity(User)!;
        var result = await commands.UpsertScopeAsync(request, identity.UserId, ct).ConfigureAwait(false);
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
