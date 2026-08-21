
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-definitions")]
public sealed class InspectionDefinitionsController(
    InspectionQueries queries,
    InspectionCommands commands,
    PlatformUserResolver userResolver) : PlatformApiController
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedRead();
        return denied ?? Ok(new { data = await queries.ListDefinitionsAsync(ct).ConfigureAwait(false) });
    }

    [HttpGet("{code}/{version:int}")]
    public async Task<IActionResult> Get(string code, int version, CancellationToken ct)
    {
        var denied = DeniedRead();
        if (denied is not null)
            return denied;
        var item = await queries.GetDefinitionAsync(code, version, ct)
            .ConfigureAwait(false);
        return item is null ? ResourceNotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] InspectionDefinition? request, CancellationToken ct)
    {
        var denied = DeniedWrite();
        if (denied is not null)
            return denied;
        var result = await commands.UpsertDefinitionAsync(request, ct).ConfigureAwait(false);
        return result.Status switch
        {
            InspectionCommandStatus.Success => Ok(result.Value),
            InspectionCommandStatus.Invalid => InvalidRequest(result.Error),
            InspectionCommandStatus.Conflict => StateConflict(result.Error, ("existing", result.Existing)),
            _ => ServerFailure()
        };
    }

    [HttpDelete("{code}/{version:int}")]
    public async Task<IActionResult> Delete(string code, int version, CancellationToken ct)
    {
        var denied = DeniedWrite();
        if (denied is not null)
            return denied;
        var result = await commands.DeleteDefinitionAsync(code, version, ct).ConfigureAwait(false);
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
