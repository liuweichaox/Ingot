
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessConfiguration;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/scenario-packages")]
public sealed class ScenarioPackagesController(
    ScenarioPackageService service,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await service.ListAsync(ct).ConfigureAwait(false) });

    [HttpGet("{packageId}/{version:int}")]
    public async Task<IActionResult> Get(string packageId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var value = await service.GetAsync(packageId, version, ct).ConfigureAwait(false);
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ScenarioPackage? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var result = await service.UpsertAsync(request, ct).ConfigureAwait(false);
        return result.Status switch
        {
            ScenarioPackageOperationStatus.Success => Ok(result.Value),
            ScenarioPackageOperationStatus.Invalid => InvalidRequest(result.Error),
            ScenarioPackageOperationStatus.Conflict => StateConflict(
                result.Error, ("existing", result.Existing)),
            _ => ServerFailure()
        };
    }

    [HttpDelete("{packageId}/{version:int}")]
    public async Task<IActionResult> Delete(string packageId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var result = await service.DeleteAsync(packageId, version, ct).ConfigureAwait(false);
        return result.Status switch
        {
            ScenarioPackageOperationStatus.Success => NoContent(),
            ScenarioPackageOperationStatus.Conflict => StateConflict(result.Error),
            ScenarioPackageOperationStatus.NotFound => ResourceNotFound(),
            _ => ServerFailure()
        };
    }
}
