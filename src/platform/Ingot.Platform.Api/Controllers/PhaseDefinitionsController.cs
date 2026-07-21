using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/phase-definitions")]
public sealed class PhaseDefinitionsController(
    IInspectionMasterDataStore store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new { data = await store.ListPhaseDefinitionsAsync(ct).ConfigureAwait(false) });
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var item = await store.GetPhaseDefinitionAsync(code.Trim().ToLowerInvariant(), ct).ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] PhaseDefinition? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        return Ok(await store.UpsertPhaseDefinitionAsync(normalized!, ct).ConfigureAwait(false));
    }

    [HttpDelete("{code}")]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        return await store.DeletePhaseDefinitionAsync(code.Trim().ToLowerInvariant(), ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
    }
}
