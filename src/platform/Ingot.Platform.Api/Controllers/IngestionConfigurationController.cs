// 提供 IngestionConfigurationController 的 HTTP 传输、认证与响应映射；业务规则由应用层执行。

using Ingot.Platform.Application.Acquisition;
using Ingot.Contracts.Acquisition;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/ingestion-configuration")]
public sealed class IngestionConfigurationController(
    AcquisitionApplication store,
    IngestionConfigurationWorkflow workflow,
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
        if (request is null) return InvalidRequest("至少需要一个任务绑定。");
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
