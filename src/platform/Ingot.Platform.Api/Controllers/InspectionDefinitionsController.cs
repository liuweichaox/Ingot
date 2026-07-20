using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Contracts.Inspections;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/inspection-definitions")]
public sealed class InspectionDefinitionsController(IInspectionMasterDataStore store) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(new { data = await store.ListInspectionDefinitionsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{code}/{version:int}")]
    public async Task<IActionResult> Get(string code, int version, CancellationToken ct)
    {
        var item = await store.GetInspectionDefinitionAsync(code.Trim().ToLowerInvariant(), version, ct)
            .ConfigureAwait(false);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] InspectionDefinition? request, CancellationToken ct)
    {
        if (!InspectionMasterDataValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        var item = await store.UpsertInspectionDefinitionAsync(normalized!, ct).ConfigureAwait(false);
        return Ok(item);
    }

    [HttpDelete("{code}/{version:int}")]
    public async Task<IActionResult> Delete(string code, int version, CancellationToken ct)
        => await store.DeleteInspectionDefinitionAsync(code.Trim().ToLowerInvariant(), version, ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
}

