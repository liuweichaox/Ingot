using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/phase-mappings")]
public sealed class PhaseMappingsController(IInspectionMasterDataStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(new { data = await store.ListPhaseMappingsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{mappingId}")]
    public async Task<IActionResult> Get(string mappingId, CancellationToken ct)
    {
        var item = await store.GetPhaseMappingAsync(mappingId.Trim().ToLowerInvariant(), ct).ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] PhaseMapping? request, CancellationToken ct)
    {
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        return Ok(await store.UpsertPhaseMappingAsync(normalized!, ct).ConfigureAwait(false));
    }

    [HttpDelete("{mappingId}")]
    public async Task<IActionResult> Delete(string mappingId, CancellationToken ct)
        => await store.DeletePhaseMappingAsync(mappingId.Trim().ToLowerInvariant(), ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
}

