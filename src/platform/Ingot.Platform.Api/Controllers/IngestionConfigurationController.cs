// 管理采集数据源、绑定和发布流程，并按 Edge 所属站点授权资源访问。
using System.Text;
using Ingot.Contracts.Acquisition;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Application.Acquisition;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/ingestion-configuration")]
public sealed class IngestionConfigurationController(
    AcquisitionApplication store,
    IngestionConfigurationWorkflow workflow,
    PlatformUserResolver userResolver,
    EdgeTokenValidator edgeTokenValidator) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpPost("extract-reusable")]
    public async Task<IActionResult> ExtractReusable(
        [FromBody] ExtractReusableRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("提取请求不能为空。");
        var sourceTask = await store.GetTaskAsync(
            NormalizeCode(request.TaskId), request.Version, ct).ConfigureAwait(false);
        if (sourceTask is null || !CanAccessEdge(ResolveIdentity()!, sourceTask.EdgeId))
            return ResourceNotFound("指定的数据摄取任务不存在。");
        var relatedDenied = await DeniedLogicalResourceAccessAsync(
            [sourceTask.TaskId], [request.DataSourceId], ct).ConfigureAwait(false);
        if (relatedDenied is not null) return relatedDenied;
        try
        {
            var data = await workflow.ExtractReusableAsync(
                request.TaskId,
                request.Version,
                request.TemplateId,
                request.TemplateVersion,
                request.DataSourceId,
                request.DataSourceVersion,
                ct).ConfigureAwait(false);
            return Ok(new { data });
        }
        catch (AcquisitionWorkflowException exception)
        {
            return WorkflowFailure(exception);
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
        try
        {
            return Ok(await workflow.SaveTemplateAsync(request, ct).ConfigureAwait(false));
        }
        catch (AcquisitionWorkflowException exception)
        {
            return WorkflowFailure(exception);
        }
    }

    [HttpDelete("templates/{templateId}/{version:int}")]
    public async Task<IActionResult> DeleteTemplate(string templateId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        try
        {
            await workflow.DeleteTemplateAsync(templateId, version, ct).ConfigureAwait(false);
            return NoContent();
        }
        catch (AcquisitionWorkflowException exception)
        {
            return WorkflowFailure(exception);
        }
    }

    [HttpGet("data-sources")]
    public async Task<IActionResult> ListDataSources(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var identity = ResolveIdentity()!;
        var sources = await store.ListDataSourcesAsync(ct).ConfigureAwait(false);
        return Ok(new
        {
            data = sources.Where(source => CanAccessEdge(identity, source.EdgeId)).ToArray()
        });
    }

    [HttpGet("data-sources/{dataSourceId}/{version:int}")]
    public async Task<IActionResult> GetDataSource(string dataSourceId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await store.GetDataSourceAsync(NormalizeCode(dataSourceId), version, ct).ConfigureAwait(false);
        return value is null || !CanAccessEdge(ResolveIdentity()!, value.EdgeId)
            ? ResourceNotFound()
            : Ok(value);
    }

    [HttpGet("data-sources.csv")]
    public async Task<IActionResult> ExportDataSources(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var identity = ResolveIdentity()!;
        var csv = IngestionConfigurationCsv.WriteDataSources(
            (await store.ListDataSourcesAsync(ct).ConfigureAwait(false))
            .Where(source => CanAccessEdge(identity, source.EdgeId))
            .ToArray());
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
        var edgeDenied = await DeniedDataSourceMutationAsync(parsed, ct).ConfigureAwait(false);
        if (edgeDenied is not null) return edgeDenied;
        try
        {
            var saved = await workflow.ImportDataSourcesAsync(parsed, ct).ConfigureAwait(false);
            return Ok(new { data = saved, count = saved.Count });
        }
        catch (AcquisitionWorkflowException exception)
        {
            return WorkflowFailure(exception);
        }
    }

    [HttpPost("data-sources")]
    public async Task<IActionResult> SaveDataSource([FromBody] DataSourceInstance? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is not null)
        {
            var edgeDenied = await DeniedDataSourceMutationAsync([request], ct).ConfigureAwait(false);
            if (edgeDenied is not null) return edgeDenied;
        }
        try
        {
            return Ok(await workflow.SaveDataSourceAsync(request, ct).ConfigureAwait(false));
        }
        catch (AcquisitionWorkflowException exception)
        {
            return WorkflowFailure(exception);
        }
    }

    [HttpDelete("data-sources/{dataSourceId}/{version:int}")]
    public async Task<IActionResult> DeleteDataSource(string dataSourceId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var existing = await store.GetDataSourceAsync(
            NormalizeCode(dataSourceId), version, ct).ConfigureAwait(false);
        if (existing is null || !CanAccessEdge(ResolveIdentity()!, existing.EdgeId))
            return ResourceNotFound();
        try
        {
            await workflow.DeleteDataSourceAsync(dataSourceId, version, ct).ConfigureAwait(false);
            return NoContent();
        }
        catch (AcquisitionWorkflowException exception)
        {
            return WorkflowFailure(exception);
        }
    }

    [HttpGet("bindings")]
    public async Task<IActionResult> ListBindings(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        return Ok(new { data = await ListAccessibleBindingsAsync(ct).ConfigureAwait(false) });
    }

    [HttpGet("bindings.csv")]
    public async Task<IActionResult> ExportBindings(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var csv = IngestionConfigurationCsv.WriteBindings(
            await ListAccessibleBindingsAsync(ct).ConfigureAwait(false));
        return File(Utf8Bom(csv), "text/csv; charset=utf-8", "ingestion-task-bindings.csv");
    }

    [HttpPost("bindings/{taskId}/{version:int}:publish")]
    public async Task<IActionResult> PublishBinding(string taskId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var binding = await store.GetBindingAsync(NormalizeCode(taskId), version, ct).ConfigureAwait(false);
        if (binding is null) return ResourceNotFound("任务绑定不存在。");
        var source = await store.GetDataSourceAsync(
            binding.DataSourceId,
            binding.DataSourceVersion,
            ct).ConfigureAwait(false);
        if (source is null) return ResourceNotFound("任务绑定不存在或当前身份无权访问。");
        var edgeDenied = DeniedEdgeAccess(source.EdgeId);
        if (edgeDenied is not null) return edgeDenied;
        var relatedDenied = await DeniedLogicalResourceAccessAsync(
            [binding.TaskId], [], ct).ConfigureAwait(false);
        if (relatedDenied is not null) return relatedDenied;
        try
        {
            var published = await workflow.PublishBindingAsync(taskId, version, ct).ConfigureAwait(false);
            return Ok(new { data = published.Task, validation = published.Validation });
        }
        catch (AcquisitionWorkflowException exception)
        {
            return WorkflowFailure(exception);
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
        if (request?.Bindings is null) return InvalidRequest("至少需要一个任务绑定。");
        var edgeDenied = await DeniedBindingMutationAsync(request.Bindings, ct).ConfigureAwait(false);
        if (edgeDenied is not null) return edgeDenied;
        try
        {
            var saved = await workflow.MaterializeAsync(request.Bindings, ct).ConfigureAwait(false);
            return Ok(new { data = saved, count = saved.Count });
        }
        catch (AcquisitionWorkflowException exception)
        {
            return WorkflowFailure(exception);
        }
    }

    private IActionResult WorkflowFailure(AcquisitionWorkflowException exception)
    {
        if (exception.Kind == AcquisitionWorkflowFailureKind.NotFound)
            return ResourceNotFound(exception.Message);
        if (exception.Kind == AcquisitionWorkflowFailureKind.Conflict)
            return StateConflict(exception.Message);
        if (exception.Kind == AcquisitionWorkflowFailureKind.Timeout)
            return ProblemResponse(StatusCodes.Status504GatewayTimeout, exception.Message, []);
        if (exception.Failures.Count > 0)
            return InvalidRequest(exception.Message, ("failures", exception.Failures));
        if (exception.Validation.Count > 0)
            return InvalidRequest(exception.Message, ("validation", exception.Validation));
        return InvalidRequest(exception.Message);
    }

    private static string NormalizeCode(string? value)
        => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private IActionResult? DeniedEdgeAccess(string? edgeId)
    {
        var identity = ResolveIdentity();
        return identity is not null && CanAccessEdge(identity, edgeId)
            ? null
            : ResourceNotFound("采集节点不存在或当前身份无权访问。");
    }

    private bool CanAccessEdge(PlatformIdentity identity, string? edgeId)
        => edgeTokenValidator.TryGetSiteId(edgeId?.Trim() ?? string.Empty, out var siteId) &&
           identity.CanAccessSite(siteId);

    private async Task<IActionResult?> DeniedDataSourceMutationAsync(
        IReadOnlyList<DataSourceInstance> requests,
        CancellationToken ct)
    {
        var identity = ResolveIdentity();
        if (identity is null || requests.Any(source => !CanAccessEdge(identity, source.EdgeId)))
            return ResourceNotFound("数据源不存在或当前身份无权访问。");

        return await DeniedLogicalResourceAccessAsync(
            [], requests.Select(static source => source.DataSourceId), ct).ConfigureAwait(false);
    }

    private async Task<IActionResult?> DeniedBindingMutationAsync(
        IReadOnlyList<IngestionTaskBinding> bindings,
        CancellationToken ct)
    {
        var identity = ResolveIdentity();
        if (identity is null)
            return ResourceNotFound("任务绑定不存在或当前身份无权访问。");

        foreach (var binding in bindings)
        {
            var source = await store.GetDataSourceAsync(
                NormalizeCode(binding.DataSourceId), binding.DataSourceVersion, ct).ConfigureAwait(false);
            if (source is null || !CanAccessEdge(identity, source.EdgeId))
                return ResourceNotFound("任务绑定不存在或当前身份无权访问。");
        }

        return await DeniedLogicalResourceAccessAsync(
            bindings.Select(static binding => binding.TaskId), [], ct).ConfigureAwait(false);
    }

    private async Task<IActionResult?> DeniedLogicalResourceAccessAsync(
        IEnumerable<string> taskIds,
        IEnumerable<string> dataSourceIds,
        CancellationToken ct)
    {
        var identity = ResolveIdentity();
        if (identity is null)
            return ResourceNotFound("采集资源不存在或当前身份无权访问。");

        var normalizedTaskIds = taskIds.Select(NormalizeCode)
            .Where(static id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedTaskIds.Count > 0)
        {
            var tasks = await store.ListTasksAsync(ct).ConfigureAwait(false);
            if (tasks.Any(task =>
                    normalizedTaskIds.Contains(task.TaskId) &&
                    !CanAccessEdge(identity, task.EdgeId)))
                return ResourceNotFound("采集配置不存在或当前身份无权访问。");
        }

        var normalizedSourceIds = dataSourceIds.Select(NormalizeCode)
            .Where(static id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedSourceIds.Count > 0)
        {
            var sources = await store.ListDataSourcesAsync(ct).ConfigureAwait(false);
            if (sources.Any(source =>
                    normalizedSourceIds.Contains(source.DataSourceId) &&
                    !CanAccessEdge(identity, source.EdgeId)))
                return ResourceNotFound("数据源不存在或当前身份无权访问。");
        }

        return null;
    }

    private async Task<IReadOnlyList<IngestionTaskBinding>> ListAccessibleBindingsAsync(CancellationToken ct)
    {
        var identity = ResolveIdentity()!;
        var sourceKeys = (await store.ListDataSourcesAsync(ct).ConfigureAwait(false))
            .Where(source => CanAccessEdge(identity, source.EdgeId))
            .Select(static source => $"{NormalizeCode(source.DataSourceId)}\n{source.Version}")
            .ToHashSet(StringComparer.Ordinal);
        return (await store.ListBindingsAsync(ct).ConfigureAwait(false))
            .Where(binding => sourceKeys.Contains(
                $"{NormalizeCode(binding.DataSourceId)}\n{binding.DataSourceVersion}"))
            .ToArray();
    }

    private static byte[] Utf8Bom(string value)
        => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(value)).ToArray();

    public sealed record IngestionTaskBatchRequest(IReadOnlyList<IngestionTaskBinding> Bindings);
    public sealed record ExtractReusableRequest(
        string TaskId,
        int Version,
        string TemplateId,
        string DataSourceId,
        int TemplateVersion = 1,
        int DataSourceVersion = 1);
}
