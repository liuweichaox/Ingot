using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/phase-mappings")]
public sealed class PhaseMappingsController(
    IInspectionMasterDataStore store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new { data = await store.ListPhaseMappingsAsync(ct).ConfigureAwait(false) });
    }

    [HttpGet("{mappingId}")]
    public async Task<IActionResult> Get(string mappingId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var item = await store.GetPhaseMappingAsync(mappingId.Trim().ToLowerInvariant(), ct).ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] PhaseMapping? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        return Ok(await store.UpsertPhaseMappingAsync(normalized!, ct).ConfigureAwait(false));
    }

    [HttpDelete("{mappingId}")]
    public async Task<IActionResult> Delete(string mappingId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        return await store.DeletePhaseMappingAsync(mappingId.Trim().ToLowerInvariant(), ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
    }
}
