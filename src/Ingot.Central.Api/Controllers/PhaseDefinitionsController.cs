using Ingot.Central.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Central.Api.Controllers;

[ApiController]
[Route("api/v1/phase-definitions")]
public sealed class PhaseDefinitionsController(IInspectionMasterDataStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(new { data = await store.ListPhaseDefinitionsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken ct)
    {
        var item = await store.GetPhaseDefinitionAsync(code.Trim().ToLowerInvariant(), ct).ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] PhaseDefinition? request, CancellationToken ct)
    {
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        return Ok(await store.UpsertPhaseDefinitionAsync(normalized!, ct).ConfigureAwait(false));
    }

    [HttpDelete("{code}")]
    public async Task<IActionResult> Delete(string code, CancellationToken ct)
        => await store.DeletePhaseDefinitionAsync(code.Trim().ToLowerInvariant(), ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
}

