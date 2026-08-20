using Ingot.Platform.Application.ProcessConfiguration;
using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Acquisition;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/ingestion-tasks")]
public sealed class IngestionTasksController(
    IIngestionTaskStore store,
    IProcessConfigurationStore processStore,
    PlatformUserResolver userResolver,
    EdgeTokenValidator edgeTokenValidator,
    AcquisitionProbeTaskCoordinator probeTasks) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListAsync(ct).ConfigureAwait(false) });

    [HttpGet("{taskId}/{version:int}")]
    public async Task<IActionResult> Get(string taskId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await store.GetAsync(NormalizeCode(taskId), version, ct).ConfigureAwait(false);
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<IActionResult> Active([FromQuery] string edgeId, CancellationToken ct)
    {
        var normalizedEdgeId = edgeId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEdgeId))
            return InvalidRequest("edgeId 不能为空。");
        if (!edgeTokenValidator.IsAuthorized(normalizedEdgeId, Request.Headers.Authorization.ToString()))
            return AuthenticationRequired("边缘节点认证失败。");

        var tasks = await store.ListPublishedForEdgeAsync(normalizedEdgeId, ct).ConfigureAwait(false);
        var deployments = new List<AcquisitionDeployment>();
        var invalidReferences = new List<string>();
        foreach (var task in tasks)
        {
            var model = await processStore.GetDataModelAsync(task.DataModelId, task.DataModelVersion, ct)
                .ConfigureAwait(false);
            if (model is not null && model.Status == ConfigurationStatuses.Published)
                deployments.Add(new AcquisitionDeployment { Task = task, DataModel = model });
            else
                invalidReferences.Add($"{task.TaskId}@{task.Version} → {task.DataModelId}@{task.DataModelVersion}");
        }
        if (invalidReferences.Count > 0)
            return StateConflict(
                "已发布数据摄取任务引用了不存在或未发布的数据模型；为避免误停采，本次不下发配置。",
                ("references", invalidReferences));
        return Ok(new { data = deployments });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] IngestionTask? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (!TryNormalize(request, out var normalized, out var error))
            return InvalidRequest(error);
        var protocolNormalized = normalized!;

        var model = await processStore.GetDataModelAsync(
            protocolNormalized.DataModelId,
            protocolNormalized.DataModelVersion,
            ct).ConfigureAwait(false);
        if (model is null)
            return InvalidRequest("引用的工艺数据模型版本不存在。");
        if (!TryNormalize(protocolNormalized, model, out var modelNormalized, out error))
            return InvalidRequest(error);
        normalized = modelNormalized!;
        if (normalized.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            return InvalidRequest("发布采集配置前，引用的工艺数据模型必须已经发布。");
        if (normalized.Status == ConfigurationStatuses.Published)
        {
            var probe = await ProbeEdgeAsync(
                new AcquisitionDeployment { Task = normalized, DataModel = model },
                null,
                ct).ConfigureAwait(false);
            if (!probe.Success)
                return ProblemResponse(probe.StatusCode, probe.Error, [("validation", probe.Result)]);
            if (probe.Result is not { Success: true, MappingsValidated: true })
                return InvalidRequest(
                    probe.Result?.Message ?? "设备连接与映射验证未通过，不能发布采集配置。",
                    ("validation", probe.Result));
        }

        var existing = await store.GetAsync(normalized.TaskId, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && normalized.Status == ConfigurationStatuses.Retired)
                normalized = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            else if (SamePayload(existing with { UpdatedAt = default }, normalized with { UpdatedAt = default }))
                return Ok(existing);
            else
                return StateConflict("已发布或停用的采集配置不可修改，请创建新版本。", ("existing", existing));
        }

        // 发布走单事务：退役旧 published 版本 + 写入新版本原子完成，
        // 消除读-改-写循环在并发发布下残留两个 published 版本的竞态。
        try
        {
            return normalized.Status == ConfigurationStatuses.Published
                ? Ok(await store.PublishExclusiveAsync(normalized, ct).ConfigureAwait(false))
                : Ok(await store.UpsertAsync(normalized, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("probe")]
    public async Task<IActionResult> Probe([FromBody] IngestionTaskProbeRequest? requestBody, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (requestBody?.Task is null)
            return InvalidRequest("采集配置不能为空。");
        var request = requestBody.Task;
        var model = await processStore.GetDataModelAsync(
            NormalizeCode(request.DataModelId),
            request.DataModelVersion,
            ct).ConfigureAwait(false);
        if (model is null)
            return InvalidRequest("引用的工艺数据模型版本不存在。");

        const string placeholderCode = "__probe_only__";
        var needsDiscoveryPlaceholder =
            request.Protocol is AcquisitionProtocols.HttpPolling or AcquisitionProtocols.Mqtt or AcquisitionProtocols.OpcUa &&
            request.ValueMappings.All(item => string.IsNullOrWhiteSpace(item.DataItemCode) ||
                                              string.IsNullOrWhiteSpace(item.SourcePath));
        var candidateRequest = needsDiscoveryPlaceholder
            ? request with
            {
                ValueMappings =
                [
                    new AcquisitionValueMapping
                    {
                        DataItemCode = model.Acquisition.DataItems.First().Code,
                        SourcePath = placeholderCode,
                        Required = false
                    }
                ]
            }
            : request;
        if (!TryNormalize(candidateRequest, out var normalized, out var error))
            return InvalidRequest(error);

        if (!TryNormalize(normalized!, model, out var modelNormalized, out error))
            return InvalidRequest(error);
        normalized = modelNormalized;

        var probe = await ProbeEdgeAsync(
            new AcquisitionDeployment { Task = normalized!, DataModel = model },
            requestBody.Discovery,
            ct).ConfigureAwait(false);
        if (!probe.Success)
            return ProblemResponse(probe.StatusCode, probe.Error, []);
        return needsDiscoveryPlaceholder && probe.Result is { } result
            ? Ok(result with
            {
                Message = $"连接成功，读取到 {result.Points.Count} 个设备点位；请选择点位并完成映射后再次验证。",
                MappingsValidated = false,
                Mappings = []
            })
            : Ok(probe.Result);
    }

    private async Task<EdgeProbeResponse> ProbeEdgeAsync(
        AcquisitionDeployment deployment,
        SourceDiscoveryQuery? discovery,
        CancellationToken cancellationToken)
    {
        try
        {
            var timeout = TimeSpan.FromMilliseconds(Math.Clamp(
                deployment.Task.Execution.TimeoutMs + 15_000,
                15_000,
                120_000));
            var result = await probeTasks.QueueAndWaitAsync(
                deployment,
                timeout,
                discovery ?? new SourceDiscoveryQuery(),
                cancellationToken).ConfigureAwait(false);
            return EdgeProbeResponse.Succeeded(result);
        }
        catch (TimeoutException)
        {
            return EdgeProbeResponse.Failure(StatusCodes.Status504GatewayTimeout, "现场节点设备验证超时。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EdgeProbeResponse.Failure(StatusCodes.Status504GatewayTimeout, "现场节点设备验证超时。");
        }
    }

    [HttpGet("probe-tasks/next")]
    [AllowAnonymous]
    public async Task<IActionResult> NextProbeTask([FromQuery] string edgeId, CancellationToken ct)
    {
        var normalizedEdgeId = edgeId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEdgeId))
            return InvalidRequest("edgeId 不能为空。");
        if (!edgeTokenValidator.IsAuthorized(normalizedEdgeId, Request.Headers.Authorization.ToString()))
            return AuthenticationRequired("边缘节点认证失败。");
        var task = await probeTasks.ClaimNextAsync(normalizedEdgeId, ct).ConfigureAwait(false);
        return task is null ? NoContent() : Ok(task);
    }

    [HttpPost("probe-tasks/{taskId}/result")]
    [AllowAnonymous]
    public async Task<IActionResult> CompleteProbeTask(
        string taskId,
        [FromBody] AcquisitionProbeTaskCompletion? completion,
        CancellationToken ct)
    {
        if (completion is null ||
            !string.Equals(taskId, completion.TaskId, StringComparison.Ordinal))
            return InvalidRequest("探查任务结果与路由不匹配。");
        if (!edgeTokenValidator.IsAuthorized(completion.EdgeId, Request.Headers.Authorization.ToString()))
            return AuthenticationRequired("边缘节点认证失败。");
        return await probeTasks.CompleteAsync(completion, ct).ConfigureAwait(false)
            ? NoContent()
            : ResourceNotFound("探查任务不存在、已过期或不属于当前 Edge。");
    }

    private sealed record EdgeProbeResponse(
        bool Success,
        int StatusCode,
        string? Error,
        AcquisitionProbeResult? Result)
    {
        public static EdgeProbeResponse Succeeded(AcquisitionProbeResult result)
            => new(true, StatusCodes.Status200OK, null, result);

        public static EdgeProbeResponse Failure(int statusCode, string error)
            => new(false, statusCode, error, null);
    }

    [HttpDelete("{taskId}/{version:int}")]
    public async Task<IActionResult> Delete(string taskId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var existing = await store.GetAsync(NormalizeCode(taskId), version, ct).ConfigureAwait(false);
        if (existing is null) return ResourceNotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return StateConflict("只有草稿采集配置可以删除。");
        return await store.DeleteAsync(existing.TaskId, version, ct).ConfigureAwait(false)
            ? NoContent()
            : ResourceNotFound();
    }

    /// <summary>
    ///     校验与规范化已经迁移到 <see cref="IngestionTaskValidator"/>（Ingot.Contracts）。
    ///
    ///     平台、边缘节点和配置界面共用同一份判断，并由协议能力矩阵裁决
    ///     每个字段是否适用于当前协议。
    /// </summary>
    private static bool TryNormalize(
        IngestionTask? value,
        out IngestionTask? normalized,
        out string error)
    {
        var valid = IngestionTaskValidator.TryValidate(value, null, out normalized, out var errors);
        error = valid ? string.Empty : string.Join("；", errors.Select(static item => item.ToString()));
        return valid;
    }

    private static bool TryNormalize(
        IngestionTask value,
        ProcessDataModel model,
        out IngestionTask? normalized,
        out string error)
    {
        var valid = IngestionTaskValidator.TryValidate(value, model, out normalized, out var errors);
        error = valid ? string.Empty : string.Join("；", errors.Select(static item => item.ToString()));
        return valid;
    }

    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}
