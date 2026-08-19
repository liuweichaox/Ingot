using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.Acquisition;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/ingestion-configuration")]
public sealed class IngestionConfigurationController(
    IIngestionConfigurationStore store,
    IIngestionTaskStore taskStore,
    IProcessConfigurationStore processStore,
    AcquisitionProbeTaskCoordinator probeTasks,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpPost("extract-reusable")]
    public async Task<IActionResult> ExtractReusable(
        [FromBody] ExtractReusableRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("提取请求不能为空。");
        var task = await taskStore.GetAsync(
            NormalizeCode(request.TaskId), request.Version, ct).ConfigureAwait(false);
        if (task is null) return ResourceNotFound("指定的数据摄取任务不存在。");
        var model = await processStore.GetDataModelAsync(
            task.DataModelId, task.DataModelVersion, ct).ConfigureAwait(false);
        if (model is null || model.Status != ConfigurationStatuses.Published)
            return InvalidRequest("任务引用的工艺数据模型必须已经发布。");
        if (!IngestionTaskDecomposer.TryCreate(
                task,
                model,
                request.TemplateId,
                request.TemplateVersion < 1 ? 1 : request.TemplateVersion,
                request.DataSourceId,
                request.DataSourceVersion < 1 ? 1 : request.DataSourceVersion,
                out var extracted,
                out var errors))
            return InvalidRequest(Join(errors), ("validation", errors));
        try
        {
            return Ok(new { data = await store.SaveExtractedAsync(extracted!, ct).ConfigureAwait(false) });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpGet("templates")]
    public async Task<IActionResult> ListTemplates(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListTemplatesAsync(ct).ConfigureAwait(false) });

    [HttpGet("templates/{templateId}/{version:int}")]
    public async Task<IActionResult> GetTemplate(string templateId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await store.GetTemplateAsync(NormalizeCode(templateId), version, ct).ConfigureAwait(false);
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpPost("templates")]
    public async Task<IActionResult> SaveTemplate([FromBody] IngestionTaskTemplate? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("任务模板不能为空。");
        var model = await processStore.GetDataModelAsync(
            NormalizeCode(request.DataModelId), request.DataModelVersion, ct).ConfigureAwait(false);
        if (model is null) return InvalidRequest("引用的标准数据模型版本不存在。");
        if (request.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            return InvalidRequest("发布任务模板前，引用的标准数据模型必须已经发布。");
        if (!IngestionTaskValidator.TryValidateTemplate(request, model, out var normalized, out var errors))
            return InvalidRequest(Join(errors), ("validation", errors));
        var conflict = await ImmutableConflictAsync(
            await store.GetTemplateAsync(normalized!.TemplateId, normalized.Version, ct).ConfigureAwait(false),
            normalized.Status);
        if (conflict is not null) return conflict;
        try
        {
            return normalized.Status == ConfigurationStatuses.Published
                ? Ok(await store.PublishTemplateExclusiveAsync(normalized, ct).ConfigureAwait(false))
                : Ok(await store.UpsertTemplateAsync(normalized, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpDelete("templates/{templateId}/{version:int}")]
    public async Task<IActionResult> DeleteTemplate(string templateId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var current = await store.GetTemplateAsync(NormalizeCode(templateId), version, ct).ConfigureAwait(false);
        if (current is null) return ResourceNotFound();
        if (current.Status != ConfigurationStatuses.Draft)
            return StateConflict("只有草稿任务模板可以删除。");
        return await store.DeleteTemplateAsync(current.TemplateId, version, ct).ConfigureAwait(false)
            ? NoContent()
            : ResourceNotFound();
    }

    [HttpGet("data-sources")]
    public async Task<IActionResult> ListDataSources(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListDataSourcesAsync(ct).ConfigureAwait(false) });

    [HttpGet("data-sources/{dataSourceId}/{version:int}")]
    public async Task<IActionResult> GetDataSource(string dataSourceId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await store.GetDataSourceAsync(NormalizeCode(dataSourceId), version, ct).ConfigureAwait(false);
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpGet("data-sources.csv")]
    public async Task<IActionResult> ExportDataSources(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var csv = IngestionConfigurationCsv.WriteDataSources(
            await store.ListDataSourcesAsync(ct).ConfigureAwait(false));
        return File(Utf8Bom(csv), "text/csv; charset=utf-8", "data-sources.csv");
    }

    [HttpPost("data-sources:import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> ImportDataSources(IFormFile? file, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (file is null || file.Length == 0) return InvalidRequest("请选择数据源 CSV 文件。");
        IReadOnlyList<DataSourceInstance> parsed;
        try
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            parsed = IngestionConfigurationCsv.ReadDataSources(await reader.ReadToEndAsync(ct).ConfigureAwait(false));
        }
        catch (InvalidDataException exception)
        {
            return InvalidRequest(exception.Message);
        }
        if (parsed.Count is 0 or > 500)
            return InvalidRequest("一次必须导入 1-500 个数据源。");
        var duplicate = parsed.GroupBy(static item => (item.DataSourceId.Trim().ToLowerInvariant(), item.Version))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            return InvalidRequest($"数据源 {duplicate.Key.Item1} v{duplicate.Key.Version} 在文件中重复。");
        var normalized = new List<DataSourceInstance>();
        var failures = new List<BatchValidationFailure>();
        foreach (var source in parsed)
        {
            if (!IngestionTaskValidator.TryValidateDataSource(source, out var valid, out var errors))
            {
                failures.Add(new BatchValidationFailure(source.DataSourceId, errors));
                continue;
            }
            var existing = await store.GetDataSourceAsync(
                valid!.DataSourceId, valid.Version, ct).ConfigureAwait(false);
            if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
            {
                failures.Add(new BatchValidationFailure(source.DataSourceId,
                    [new AcquisitionValidationError("status", "已发布或停用的数据源版本不可覆盖。") ]));
                continue;
            }
            normalized.Add(valid);
        }
        if (failures.Count > 0)
            return InvalidRequest("CSV 存在无效数据，未写入任何数据源。", ("failures", failures));
        try
        {
            var saved = await store.SaveDataSourcesAsync(normalized, ct).ConfigureAwait(false);
            return Ok(new { data = saved, count = saved.Count });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("data-sources")]
    public async Task<IActionResult> SaveDataSource([FromBody] DataSourceInstance? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (!IngestionTaskValidator.TryValidateDataSource(request, out var normalized, out var errors))
            return InvalidRequest(Join(errors), ("validation", errors));
        var conflict = await ImmutableConflictAsync(
            await store.GetDataSourceAsync(normalized!.DataSourceId, normalized.Version, ct).ConfigureAwait(false),
            normalized.Status);
        if (conflict is not null) return conflict;
        try
        {
            return normalized.Status == ConfigurationStatuses.Published
                ? Ok(await store.PublishDataSourceExclusiveAsync(normalized, ct).ConfigureAwait(false))
                : Ok(await store.UpsertDataSourceAsync(normalized, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpDelete("data-sources/{dataSourceId}/{version:int}")]
    public async Task<IActionResult> DeleteDataSource(string dataSourceId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var current = await store.GetDataSourceAsync(NormalizeCode(dataSourceId), version, ct).ConfigureAwait(false);
        if (current is null) return ResourceNotFound();
        if (current.Status != ConfigurationStatuses.Draft)
            return StateConflict("只有草稿数据源可以删除。");
        return await store.DeleteDataSourceAsync(current.DataSourceId, version, ct).ConfigureAwait(false)
            ? NoContent()
            : ResourceNotFound();
    }

    [HttpGet("bindings")]
    public async Task<IActionResult> ListBindings(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListBindingsAsync(ct).ConfigureAwait(false) });

    [HttpGet("bindings.csv")]
    public async Task<IActionResult> ExportBindings(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var csv = IngestionConfigurationCsv.WriteBindings(
            await store.ListBindingsAsync(ct).ConfigureAwait(false));
        return File(Utf8Bom(csv), "text/csv; charset=utf-8", "ingestion-task-bindings.csv");
    }

    [HttpPost("bindings/{taskId}/{version:int}:publish")]
    public async Task<IActionResult> PublishBinding(string taskId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var existing = await store.GetBindingAsync(NormalizeCode(taskId), version, ct).ConfigureAwait(false);
        if (existing is null) return ResourceNotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return StateConflict("只有草稿任务绑定可以执行验证并发布。");
        var binding = existing with { Status = ConfigurationStatuses.Published, UpdatedAt = DateTimeOffset.UtcNow };
        var template = await store.GetTemplateAsync(
            binding.TemplateId, binding.TemplateVersion, ct).ConfigureAwait(false);
        var source = await store.GetDataSourceAsync(
            binding.DataSourceId, binding.DataSourceVersion, ct).ConfigureAwait(false);
        if (template is null || source is null)
            return InvalidRequest(template is null ? "引用的任务模板不存在。" : "引用的数据源不存在。");
        var model = await processStore.GetDataModelAsync(
            template.DataModelId, template.DataModelVersion, ct).ConfigureAwait(false);
        if (model is null || model.Status != ConfigurationStatuses.Published)
            return InvalidRequest("引用的工艺数据模型必须已经发布。");
        if (!IngestionTaskMaterializer.TryCreate(
                template, source, binding, model, out var task, out var errors))
            return InvalidRequest(Join(errors), ("validation", errors));
        AcquisitionProbeResult result;
        try
        {
            result = await probeTasks.QueueAndWaitAsync(
                new AcquisitionDeployment { Task = task!, DataModel = model },
                TimeSpan.FromMilliseconds(Math.Clamp(task!.Execution.TimeoutMs + 15_000, 15_000, 120_000)),
                new SourceDiscoveryQuery(),
                ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return ProblemResponse(StatusCodes.Status504GatewayTimeout, "现场节点设备验证超时。", []);
        }
        if (!result.Success || !result.MappingsValidated)
            return InvalidRequest(result.Message, ("validation", result));
        try
        {
            var saved = await store.SaveMaterializedTasksAsync([(binding, task!)], ct).ConfigureAwait(false);
            return Ok(new { data = AssertSingle(saved), validation = result });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("bindings:import")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> ImportBindings(IFormFile? file, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (file is null || file.Length == 0) return InvalidRequest("请选择任务绑定 CSV 文件。");
        try
        {
            await using var stream = file.OpenReadStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var values = IngestionConfigurationCsv.ReadBindings(
                await reader.ReadToEndAsync(ct).ConfigureAwait(false));
            return await Materialize(new IngestionTaskBatchRequest(values), ct).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }

    [HttpPost("materialize")]
    public async Task<IActionResult> Materialize(
        [FromBody] IngestionTaskBatchRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null || request.Bindings.Count == 0)
            return InvalidRequest("至少需要一个任务绑定。");
        if (request.Bindings.Count > 500)
            return InvalidRequest("单次最多实例化 500 个任务。");
        if (request.Bindings.Any(static item => item.Status != ConfigurationStatuses.Draft))
            return InvalidRequest("批量实例化只创建草稿；发布前必须逐个完成真实数据验证。");
        var duplicate = request.Bindings.GroupBy(static item => (NormalizeCode(item.TaskId), item.Version))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            return InvalidRequest($"任务 {duplicate.Key.Item1} v{duplicate.Key.Version} 在请求中重复。");

        var materialized = new List<(IngestionTaskBinding Binding, IngestionTask Task)>();
        var failures = new List<BatchValidationFailure>();
        foreach (var raw in request.Bindings)
        {
            if (!IngestionTaskValidator.TryValidateBinding(raw, out var binding, out var bindingErrors))
            {
                failures.Add(new BatchValidationFailure(raw.TaskId, bindingErrors));
                continue;
            }
            var template = await store.GetTemplateAsync(
                binding!.TemplateId, binding.TemplateVersion, ct).ConfigureAwait(false);
            var source = await store.GetDataSourceAsync(
                binding.DataSourceId, binding.DataSourceVersion, ct).ConfigureAwait(false);
            if (template is null || source is null)
            {
                var missing = template is null ? "引用的任务模板不存在。" : "引用的数据源不存在。";
                failures.Add(new BatchValidationFailure(binding.TaskId,
                    [new AcquisitionValidationError(string.Empty, missing)]));
                continue;
            }
            var model = await processStore.GetDataModelAsync(
                template.DataModelId, template.DataModelVersion, ct).ConfigureAwait(false);
            if (model is null)
            {
                failures.Add(new BatchValidationFailure(binding.TaskId,
                    [new AcquisitionValidationError("dataModelId", "模板引用的数据模型不存在。")]));
                continue;
            }
            if (!IngestionTaskMaterializer.TryCreate(
                    template, source, binding, model, out var task, out var materializationErrors))
            {
                failures.Add(new BatchValidationFailure(binding.TaskId, materializationErrors));
                continue;
            }
            var existing = await store.GetBindingAsync(binding.TaskId, binding.Version, ct).ConfigureAwait(false);
            if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
            {
                failures.Add(new BatchValidationFailure(binding.TaskId,
                    [new AcquisitionValidationError("status", "已发布或停用的任务绑定不可修改。") ]));
                continue;
            }
            materialized.Add((binding, task!));
        }
        if (failures.Count > 0)
            return InvalidRequest("批量实例化存在无效项目，未写入任何任务。", ("failures", failures));
        try
        {
            var saved = await store.SaveMaterializedTasksAsync(materialized, ct).ConfigureAwait(false);
            return Ok(new { data = saved, count = saved.Count });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    private Task<IActionResult?> ImmutableConflictAsync(object? existing, string nextStatus)
    {
        if (existing is null) return Task.FromResult<IActionResult?>(null);
        var status = existing switch
        {
            IngestionTaskTemplate template => template.Status,
            DataSourceInstance source => source.Status,
            _ => ConfigurationStatuses.Retired
        };
        return Task.FromResult<IActionResult?>(status == ConfigurationStatuses.Draft
            ? null
            : StateConflict($"已发布或停用的配置不可修改，请创建新版本；请求状态为 {nextStatus}。"));
    }

    private static string Join(IEnumerable<AcquisitionValidationError> errors)
        => string.Join("；", errors.Select(static item => item.ToString()));

    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static byte[] Utf8Bom(string value)
        => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(value)).ToArray();

    private static IngestionTask AssertSingle(IReadOnlyList<IngestionTask> values)
        => values.Count == 1 ? values[0] : throw new InvalidOperationException("任务发布事务没有返回唯一结果。");

    public sealed record IngestionTaskBatchRequest(IReadOnlyList<IngestionTaskBinding> Bindings);
    public sealed record ExtractReusableRequest(
        string TaskId,
        int Version,
        string TemplateId,
        string DataSourceId,
        int TemplateVersion = 1,
        int DataSourceVersion = 1);
    public sealed record BatchValidationFailure(
        string TaskId,
        IReadOnlyList<AcquisitionValidationError> Errors);
}
