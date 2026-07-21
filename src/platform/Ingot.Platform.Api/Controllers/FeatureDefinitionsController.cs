using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/feature-definitions")]
public sealed class FeatureDefinitionsController(
    IInspectionMasterDataStore store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        return denied ?? Ok(new { data = await store.ListFeatureDefinitionsAsync(ct).ConfigureAwait(false) });
    }

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var item = await store.GetFeatureDefinitionAsync(code.Trim().ToLowerInvariant(), ct).ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] FeatureDefinition? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        return Ok(await store.UpsertFeatureDefinitionAsync(normalized!, ct).ConfigureAwait(false));
    }

    [HttpDelete("{code}")]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        return await store.DeleteFeatureDefinitionAsync(code.Trim().ToLowerInvariant(), ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
    }
}
