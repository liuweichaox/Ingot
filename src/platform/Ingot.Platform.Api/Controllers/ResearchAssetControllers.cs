// 管理项目研发资产，并在每个子资源入口复核成员关系和项目站点授权。
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ResearchAssets;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/training-datasets")]
public sealed class TrainingDatasetsController(
    ResearchAssetApplication store,
    ResearchAssetWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 200, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null) return denied;
        if (limit is < 1 or > 200) return InvalidRequest("limit 必须在 1 到 200 之间。");
        return Ok(await store.ListDatasetsPageAsync(limit, cursor, ct).ConfigureAwait(false));
    }

    [HttpGet("{datasetId}/{version:int}")]
    public async Task<IActionResult> Get(string datasetId, int version, CancellationToken ct)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null)
            return denied;
        var value = await store.GetDatasetAsync(datasetId.Trim().ToLowerInvariant(), version, ct)
            .ConfigureAwait(false);
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] TrainingDatasetVersion request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await workflow.RegisterDatasetAsync(
                request,
                ResolveUserId()!,
                ct).ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
    }
}
