
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ResearchAssets;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/mechanism-models")]
public sealed class MechanismModelsController(
    ResearchAssetApplication store,
    MechanismModelService service,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 200, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null) return denied;
        if (limit is < 1 or > 200) return InvalidRequest("limit 必须在 1 到 200 之间。");
        return Ok(await store.ListMechanismModelsPageAsync(limit, cursor, ct).ConfigureAwait(false));
    }

    [HttpGet("{modelId}/{version:int}")]
    public async Task<IActionResult> Get(string modelId, int version, CancellationToken ct)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null)
            return denied;
        var normalizedId = modelId.Trim().ToLowerInvariant();
        var model = await store.GetMechanismModelAsync(normalizedId, version, ct).ConfigureAwait(false);
        if (model is null)
            return ResourceNotFound();
        var audit = await store.ListAuditEntriesAsync(
            "mechanism-model",
            $"{normalizedId}:{version}",
            ct).ConfigureAwait(false);
        return Ok(new { model, audit });
    }

    [HttpPost]
    public Task<IActionResult> SaveDraft(
        [FromBody] MechanismModelVersion request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => service.SaveModelDraftAsync(request, ResolveUserId()!, ct));

    [HttpPost("{modelId}/{version:int}/status")]
    public Task<IActionResult> ChangeStatus(
        string modelId,
        int version,
        [FromBody] StatusChangeRequest request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => service.ChangeModelStatusAsync(
                modelId,
                version,
                request.TargetStatus,
                ResolveUserId()!,
                ct));

    private async Task<IActionResult> ExecuteWriteAsync<T>(Func<Task<T>> operation)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await operation().ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
    }
}

[ApiController]
[Route("api/v1/mechanism-fusions")]
public sealed class MechanismFusionsController(
    ResearchAssetApplication store,
    MechanismModelService service,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 200, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null) return denied;
        if (limit is < 1 or > 200) return InvalidRequest("limit 必须在 1 到 200 之间。");
        return Ok(await store.ListMechanismFusionsPageAsync(limit, cursor, ct).ConfigureAwait(false));
    }

    [HttpGet("{fusionId}/{version:int}")]
    public async Task<IActionResult> Get(string fusionId, int version, CancellationToken ct)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null)
            return denied;
        var normalizedId = fusionId.Trim().ToLowerInvariant();
        var fusion = await store.GetMechanismFusionAsync(normalizedId, version, ct).ConfigureAwait(false);
        if (fusion is null)
            return ResourceNotFound();
        var audit = await store.ListAuditEntriesAsync(
            "mechanism-fusion",
            $"{normalizedId}:{version}",
            ct).ConfigureAwait(false);
        return Ok(new { fusion, audit });
    }

    [HttpPost]
    public Task<IActionResult> SaveDraft(
        [FromBody] MechanismFusionDefinition request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => service.SaveFusionDraftAsync(request, ResolveUserId()!, ct));

    [HttpPost("{fusionId}/{version:int}/status")]
    public Task<IActionResult> ChangeStatus(
        string fusionId,
        int version,
        [FromBody] StatusChangeRequest request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => service.ChangeFusionStatusAsync(
                fusionId,
                version,
                request.TargetStatus,
                ResolveUserId()!,
                ct));

    [HttpPost("execute")]
    public async Task<IActionResult> Execute(
        [FromBody] MechanismFusionExecutionRequest request,
        CancellationToken ct)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await service.ExecuteAsync(request, ct).ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    private async Task<IActionResult> ExecuteWriteAsync<T>(Func<Task<T>> operation)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await operation().ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
    }
}
