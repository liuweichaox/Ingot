using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.Acquisition;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-data-models")]
public sealed class ProcessDataModelsController(
    ProcessConfigurationApplication store,
    AcquisitionApplication ingestionTasks,
    AcquisitionApplication ingestionConfigurations,
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
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ProcessDataModel? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ProcessConfigurationValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        var existing = await store.GetDataModelAsync(normalized!.ModelId, normalized.Version, ct).ConfigureAwait(false);
        if (existing?.Status == ConfigurationStatuses.Published &&
            normalized.Status == ConfigurationStatuses.Retired)
        {
            var activeReferences = await ActiveIngestionReferences(existing, ct).ConfigureAwait(false);
            if (activeReferences.Count > 0)
                return StateConflict(
                    "工艺数据模型仍被活动的数据摄取任务或任务模板引用，不能退役。",
                    ("references", activeReferences));
        }
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
            return ResourceNotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return StateConflict("只有草稿工艺数据模型可以删除。");
        var processSpecifications = await store.ListProcessSpecificationsAsync(ct).ConfigureAwait(false);
        var plans = await store.ListAnalysisPlansAsync(ct).ConfigureAwait(false);
        var packages = await store.ListScenarioPackagesAsync(ct).ConfigureAwait(false);
        var ingestionTaskValues = await ingestionTasks.ListAsync(ct).ConfigureAwait(false);
        var ingestionTemplates = await ingestionConfigurations.ListTemplatesAsync(ct).ConfigureAwait(false);
        if (processSpecifications.Any(item => item.DataModelId == existing.ModelId && item.DataModelVersion == existing.Version) ||
            plans.Any(item => item.DataModelId == existing.ModelId && item.DataModelVersion == existing.Version) ||
            packages.Any(item => item.DataModelId == existing.ModelId && item.DataModelVersion == existing.Version) ||
            ingestionTaskValues.Any(item => References(item, existing)) ||
            ingestionTemplates.Any(item => References(item, existing)))
        {
            return StateConflict("工艺数据模型仍被工艺规范版本、分析方案、工艺配置或数据摄取配置引用，不能删除。");
        }
        return await store.DeleteDataModelAsync(existing.ModelId, version, ct).ConfigureAwait(false) ? NoContent() : ResourceNotFound();
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
        return (null, StateConflict("已发布或停用的工艺数据模型不可修改，请创建新版本。", ("existing", existing)));
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);

    private async Task<IReadOnlyList<string>> ActiveIngestionReferences(
        ProcessDataModel model,
        CancellationToken ct)
    {
        var tasks = await ingestionTasks.ListAsync(ct).ConfigureAwait(false);
        var templates = await ingestionConfigurations.ListTemplatesAsync(ct).ConfigureAwait(false);
        return tasks.Where(item => item.Status != ConfigurationStatuses.Retired && References(item, model))
            .Select(item => $"task:{item.TaskId}@{item.Version}")
            .Concat(templates.Where(item => item.Status != ConfigurationStatuses.Retired && References(item, model))
                .Select(item => $"template:{item.TemplateId}@{item.Version}"))
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool References(IngestionTask task, ProcessDataModel model)
        => task.DataModelId == model.ModelId && task.DataModelVersion == model.Version;

    private static bool References(IngestionTaskTemplate template, ProcessDataModel model)
        => template.DataModelId == model.ModelId && template.DataModelVersion == model.Version;
}

[ApiController]
[Route("api/v1/process-specifications")]
public sealed class ProcessSpecificationsController(
    ProcessConfigurationApplication store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListProcessSpecificationsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{processSpecificationId}/{version:int}")]
    public async Task<IActionResult> Get(string processSpecificationId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var value = await store.GetProcessSpecificationAsync(Normalize(processSpecificationId), version, ct).ConfigureAwait(false);
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ProcessSpecification? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ProcessConfigurationValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        var model = await store.GetDataModelAsync(normalized!.DataModelId, normalized.DataModelVersion, ct)
            .ConfigureAwait(false);
        if (model is null)
            return InvalidRequest("引用的工艺数据模型版本不存在。");
        if (normalized.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            return InvalidRequest("发布工艺规范前，引用的工艺数据模型必须已经发布。");
        var definitions = model.ControlParameters.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var unknown = normalized.Values.FirstOrDefault(item => !definitions.ContainsKey(item.Code));
        if (unknown is not null)
            return InvalidRequest($"控制参数未在工艺数据模型中定义：{unknown.Code}。");
        var missing = definitions.Values.FirstOrDefault(item => !item.Nullable && normalized.Values.All(value => value.Code != item.Code));
        if (missing is not null)
            return InvalidRequest($"缺少必填控制参数：{missing.Code}。");
        var invalid = normalized.Values.FirstOrDefault(item => !MatchesDataType(item.Value, definitions[item.Code].DataType));
        if (invalid is not null)
            return InvalidRequest($"控制参数 {invalid.Code} 的值不符合 {definitions[invalid.Code].DataType} 类型。");
        var existing = await store.GetProcessSpecificationAsync(normalized.ProcessSpecificationId, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && normalized.Status == ConfigurationStatuses.Retired)
                normalized = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            else if (SamePayload(existing with { UpdatedAt = default }, normalized with { UpdatedAt = default }))
                return Ok(existing);
            else
                return StateConflict("已发布或停用的工艺规范版本不可修改，请创建新版本。", ("existing", existing));
        }
        return Ok(await store.UpsertProcessSpecificationAsync(normalized, ct).ConfigureAwait(false));
    }

    [HttpDelete("{processSpecificationId}/{version:int}")]
    public async Task<IActionResult> Delete(string processSpecificationId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        var existing = await store.GetProcessSpecificationAsync(Normalize(processSpecificationId), version, ct).ConfigureAwait(false);
        if (existing is null)
            return ResourceNotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return StateConflict("只有草稿工艺规范版本可以删除。");
        return await store.DeleteProcessSpecificationAsync(existing.ProcessSpecificationId, version, ct).ConfigureAwait(false) ? NoContent() : ResourceNotFound();
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
    ProcessConfigurationApplication store,
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
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] ProcessAnalysisPlan? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (!ProcessConfigurationValidator.TryValidate(request, out var normalized, out var error))
            return InvalidRequest(error);
        var model = await store.GetDataModelAsync(normalized!.DataModelId, normalized.DataModelVersion, ct)
            .ConfigureAwait(false);
        if (model is null)
            return InvalidRequest("引用的工艺数据模型版本不存在。");
        if (normalized.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            return InvalidRequest("发布分析方案前，引用的工艺数据模型必须已经发布。");
        var dataItemCodes = model.Acquisition.DataItems.Select(item => item.Code).ToHashSet(StringComparer.Ordinal);
        var unknown = normalized.Signals.FirstOrDefault(item => !dataItemCodes.Contains(item.DataItemCode));
        if (unknown is not null)
            return InvalidRequest($"分析数据项未在工艺数据模型中定义：{unknown.DataItemCode}。");
        var existing = await store.GetAnalysisPlanAsync(normalized.PlanId, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && normalized.Status == ConfigurationStatuses.Retired)
                normalized = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            else if (SamePayload(existing with { UpdatedAt = default }, normalized with { UpdatedAt = default }))
                return Ok(existing);
            else
                return StateConflict("已发布或停用的分析方案不可修改，请创建新版本。", ("existing", existing));
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
            return ResourceNotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return StateConflict("只有草稿分析方案可以删除。");
        var packages = await store.ListScenarioPackagesAsync(ct).ConfigureAwait(false);
        if (packages.Any(item => item.AnalysisPlanId == existing.PlanId && item.AnalysisPlanVersion == existing.Version))
            return StateConflict("分析方案仍被工艺配置引用，不能删除。");
        return await store.DeleteAnalysisPlanAsync(existing.PlanId, version, ct).ConfigureAwait(false) ? NoContent() : ResourceNotFound();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
}
