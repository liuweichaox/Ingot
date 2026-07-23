using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-data-models")]
public sealed class ProcessDataModelsController(
    IProcessConfigurationStore store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListDataModelsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{modelId}/{version:int}")]
    public async Task<IActionResult> Get(string modelId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var value = await store.GetDataModelAsync(Normalize(modelId), version, ct).ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ProcessDataModel? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ProcessConfigurationValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        var existing = await store.GetDataModelAsync(normalized!.ModelId, normalized.Version, ct).ConfigureAwait(false);
        var immutable = HandleImmutable(existing, normalized, value => value.Status, (value, status) => value with
        {
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        if (immutable.Result is not null)
            return immutable.Result;
        return Ok(await store.UpsertDataModelAsync(immutable.Value!, ct).ConfigureAwait(false));
    }

    [HttpDelete("{modelId}/{version:int}")]
    public async Task<IActionResult> Delete(string modelId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var existing = await store.GetDataModelAsync(Normalize(modelId), version, ct).ConfigureAwait(false);
        if (existing is null)
            return NotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return Conflict(new { error = "只有草稿工艺数据模型可以删除。" });
        var recipes = await store.ListRecipeVersionsAsync(ct).ConfigureAwait(false);
        var plans = await store.ListAnalysisPlansAsync(ct).ConfigureAwait(false);
        if (recipes.Any(item => item.DataModelId == existing.ModelId && item.DataModelVersion == existing.Version) ||
            plans.Any(item => item.DataModelId == existing.ModelId && item.DataModelVersion == existing.Version))
        {
            return Conflict(new { error = "工艺数据模型仍被配方版本或分析方案引用，不能删除。" });
        }
        return await store.DeleteDataModelAsync(existing.ModelId, version, ct).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    private (ProcessDataModel? Value, IActionResult? Result) HandleImmutable(
        ProcessDataModel? existing,
        ProcessDataModel requested,
        Func<ProcessDataModel, string> status,
        Func<ProcessDataModel, string, ProcessDataModel> transition)
    {
        if (existing is null || status(existing) == ConfigurationStatuses.Draft)
            return (requested, null);
        if (SamePayload(existing with { UpdatedAt = default }, requested with { UpdatedAt = default }))
            return (existing, Ok(existing));
        if (status(existing) == ConfigurationStatuses.Published && requested.Status == ConfigurationStatuses.Retired)
            return (transition(existing, ConfigurationStatuses.Retired), null);
        return (null, Conflict(new { error = "已发布或停用的工艺数据模型不可修改，请创建新版本。", existing }));
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
}

[ApiController]
[Route("api/v1/recipe-versions")]
public sealed class RecipeVersionsController(
    IProcessConfigurationStore store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListRecipeVersionsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{recipeId}/{version:int}")]
    public async Task<IActionResult> Get(string recipeId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var value = await store.GetRecipeVersionAsync(Normalize(recipeId), version, ct).ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] RecipeVersion? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ProcessConfigurationValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        var model = await store.GetDataModelAsync(normalized!.DataModelId, normalized.DataModelVersion, ct)
            .ConfigureAwait(false);
        if (model is null)
            return BadRequest(new { error = "引用的工艺数据模型版本不存在。" });
        if (normalized.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            return BadRequest(new { error = "发布配方前，引用的工艺数据模型必须已经发布。" });
        var definitions = model.RecipeParameters.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var unknown = normalized.Values.FirstOrDefault(item => !definitions.ContainsKey(item.Code));
        if (unknown is not null)
            return BadRequest(new { error = $"配方参数未在工艺数据模型中定义：{unknown.Code}。" });
        var missing = definitions.Values.FirstOrDefault(item => !item.Nullable && normalized.Values.All(value => value.Code != item.Code));
        if (missing is not null)
            return BadRequest(new { error = $"缺少必填配方参数：{missing.Code}。" });
        var invalid = normalized.Values.FirstOrDefault(item => !MatchesDataType(item.Value, definitions[item.Code].DataType));
        if (invalid is not null)
            return BadRequest(new { error = $"配方参数 {invalid.Code} 的值不符合 {definitions[invalid.Code].DataType} 类型。" });
        var existing = await store.GetRecipeVersionAsync(normalized.RecipeId, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && normalized.Status == ConfigurationStatuses.Retired)
                normalized = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            else if (SamePayload(existing with { UpdatedAt = default }, normalized with { UpdatedAt = default }))
                return Ok(existing);
            else
                return Conflict(new { error = "已发布或停用的配方版本不可修改，请创建新版本。", existing });
        }
        return Ok(await store.UpsertRecipeVersionAsync(normalized, ct).ConfigureAwait(false));
    }

    [HttpDelete("{recipeId}/{version:int}")]
    public async Task<IActionResult> Delete(string recipeId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var existing = await store.GetRecipeVersionAsync(Normalize(recipeId), version, ct).ConfigureAwait(false);
        if (existing is null)
            return NotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return Conflict(new { error = "只有草稿配方版本可以删除。" });
        return await store.DeleteRecipeVersionAsync(existing.RecipeId, version, ct).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static bool MatchesDataType(JsonElement value, string dataType)
        => value.ValueKind == JsonValueKind.Null || dataType switch
        {
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "string" => value.ValueKind == JsonValueKind.String,
            _ => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out _)
        };
}

[ApiController]
[Route("api/v1/process-analysis-plans")]
public sealed class ProcessAnalysisPlansController(
    IProcessConfigurationStore store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListAnalysisPlansAsync(ct).ConfigureAwait(false) });

    [HttpGet("{planId}/{version:int}")]
    public async Task<IActionResult> Get(string planId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var value = await store.GetAnalysisPlanAsync(Normalize(planId), version, ct).ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ProcessAnalysisPlan? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ProcessConfigurationValidator.TryValidate(request, out var normalized, out var error))
            return BadRequest(new { error });
        var model = await store.GetDataModelAsync(normalized!.DataModelId, normalized.DataModelVersion, ct)
            .ConfigureAwait(false);
        if (model is null)
            return BadRequest(new { error = "引用的工艺数据模型版本不存在。" });
        if (normalized.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            return BadRequest(new { error = "发布分析方案前，引用的工艺数据模型必须已经发布。" });
        var dataItemCodes = model.Acquisition.DataItems.Select(item => item.Code).ToHashSet(StringComparer.Ordinal);
        var unknown = normalized.Signals.FirstOrDefault(item => !dataItemCodes.Contains(item.DataItemCode));
        if (unknown is not null)
            return BadRequest(new { error = $"分析数据项未在工艺数据模型中定义：{unknown.DataItemCode}。" });
        var existing = await store.GetAnalysisPlanAsync(normalized.PlanId, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && normalized.Status == ConfigurationStatuses.Retired)
                normalized = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            else if (SamePayload(existing with { UpdatedAt = default }, normalized with { UpdatedAt = default }))
                return Ok(existing);
            else
                return Conflict(new { error = "已发布或停用的分析方案不可修改，请创建新版本。", existing });
        }
        return Ok(await store.UpsertAnalysisPlanAsync(normalized, ct).ConfigureAwait(false));
    }

    [HttpDelete("{planId}/{version:int}")]
    public async Task<IActionResult> Delete(string planId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var existing = await store.GetAnalysisPlanAsync(Normalize(planId), version, ct).ConfigureAwait(false);
        if (existing is null)
            return NotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return Conflict(new { error = "只有草稿分析方案可以删除。" });
        return await store.DeleteAnalysisPlanAsync(existing.PlanId, version, ct).ConfigureAwait(false) ? NoContent() : NotFound();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
}
