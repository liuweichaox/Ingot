// 提供 InspectionPlansController 的 HTTP 传输、认证与响应映射；业务规则由应用层执行。

using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-plans")]
public sealed class InspectionPlansController(
    InspectionQueries queries,
    InspectionCommands commands,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        return Ok(new { data = await queries.ListPlansAsync(ct).ConfigureAwait(false) });
    }

    [HttpGet("{planId}/{version:int}")]
    public async Task<IActionResult> Get(string planId, int version, CancellationToken ct)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        var item = await queries.GetPlanAsync(planId, version, ct)
            .ConfigureAwait(false);
        return item is null ? ResourceNotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] InspectionPlan? request, CancellationToken ct)
    {
        var denied = DeniedWrite();
        if (denied is not null)
            return denied;
        var result = await commands.UpsertPlanAsync(request, ct).ConfigureAwait(false);
        return result.Status switch
        {
            InspectionCommandStatus.Success => Ok(result.Value),
            InspectionCommandStatus.Invalid => InvalidRequest(result.Error),
            InspectionCommandStatus.Conflict => StateConflict(result.Error, ("existing", result.Existing)),
            _ => ServerFailure()
        };
    }

    [HttpDelete("{planId}/{version:int}")]
    public async Task<IActionResult> Delete(string planId, int version, CancellationToken ct)
    {
        var denied = DeniedWrite();
        if (denied is not null)
            return denied;
        var result = await commands.DeletePlanAsync(planId, version, ct).ConfigureAwait(false);
        return result.Status switch
        {
            InspectionCommandStatus.Success => NoContent(),
            InspectionCommandStatus.Conflict => StateConflict(result.Error),
            InspectionCommandStatus.NotFound => ResourceNotFound(),
            _ => ServerFailure()
        };
    }

    private IActionResult? DeniedRead()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        return identity.HasAnyRole(PlatformRoles.QualityRead) ? null : AuthorizationDenied();
    }

    private IActionResult? DeniedWrite()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        return identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator)
            ? null
            : AuthorizationDenied();
    }
}
