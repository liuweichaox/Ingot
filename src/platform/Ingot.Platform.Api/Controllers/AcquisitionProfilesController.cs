using System.Text.Json;
using System.Text.RegularExpressions;
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
[Route("api/v1/acquisition-profiles")]
public sealed partial class AcquisitionProfilesController(
    IAcquisitionProfileStore store,
    IProcessConfigurationStore processStore,
    PlatformUserResolver userResolver,
    EdgeTokenValidator edgeTokenValidator,
    AcquisitionProbeTaskCoordinator probeTasks) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListAsync(ct).ConfigureAwait(false) });

    [HttpGet("{profileId}/{version:int}")]
    public async Task<IActionResult> Get(string profileId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await store.GetAsync(NormalizeCode(profileId), version, ct).ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<IActionResult> Active([FromQuery] string edgeId, CancellationToken ct)
    {
        var normalizedEdgeId = edgeId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEdgeId))
            return BadRequest(new { error = "edgeId 不能为空。" });
        if (!edgeTokenValidator.IsAuthorized(normalizedEdgeId, Request.Headers.Authorization.ToString()))
            return Unauthorized(new { error = "边缘节点认证失败。" });

        var profiles = await store.ListPublishedForEdgeAsync(normalizedEdgeId, ct).ConfigureAwait(false);
        var deployments = new List<AcquisitionDeployment>();
        foreach (var profile in profiles)
        {
            var model = await processStore.GetDataModelAsync(profile.DataModelId, profile.DataModelVersion, ct)
                .ConfigureAwait(false);
            if (model is not null && model.Status == ConfigurationStatuses.Published)
                deployments.Add(new AcquisitionDeployment { Profile = profile, DataModel = model });
        }
        return Ok(new { data = deployments });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] AcquisitionProfile? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (!TryNormalize(request, out var normalized, out var error))
            return BadRequest(new { error });

        var model = await processStore.GetDataModelAsync(
            normalized!.DataModelId,
            normalized.DataModelVersion,
            ct).ConfigureAwait(false);
        if (model is null)
            return BadRequest(new { error = "引用的工艺数据模型版本不存在。" });
        if (!ValidateMappings(normalized, model, out error))
            return BadRequest(new { error });
        if (normalized.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            return BadRequest(new { error = "发布采集配置前，引用的工艺数据模型必须已经发布。" });
        if (normalized.Status == ConfigurationStatuses.Published)
        {
            var probe = await ProbeEdgeAsync(
                new AcquisitionDeployment { Profile = normalized, DataModel = model },
                ct).ConfigureAwait(false);
            if (!probe.Success)
                return StatusCode(probe.StatusCode, new { error = probe.Error, validation = probe.Result });
            if (probe.Result is not { Success: true, MappingsValidated: true })
                return BadRequest(new
                {
                    error = probe.Result?.Message ?? "设备连接与映射验证未通过，不能发布采集配置。",
                    validation = probe.Result
                });
        }

        var existing = await store.GetAsync(normalized.ProfileId, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && normalized.Status == ConfigurationStatuses.Retired)
                normalized = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            else if (SamePayload(existing with { UpdatedAt = default }, normalized with { UpdatedAt = default }))
                return Ok(existing);
            else
                return Conflict(new { error = "已发布或停用的采集配置不可修改，请创建新版本。", existing });
        }

        // 发布走单事务：退役旧 published 版本 + 写入新版本原子完成，
        // 消除读-改-写循环在并发发布下残留两个 published 版本的竞态。
        return normalized.Status == ConfigurationStatuses.Published
            ? Ok(await store.PublishExclusiveAsync(normalized, ct).ConfigureAwait(false))
            : Ok(await store.UpsertAsync(normalized, ct).ConfigureAwait(false));
    }

    [HttpPost("probe")]
    public async Task<IActionResult> Probe([FromBody] AcquisitionProfile? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null)
            return BadRequest(new { error = "采集配置不能为空。" });
        var model = await processStore.GetDataModelAsync(
            NormalizeCode(request.DataModelId),
            request.DataModelVersion,
            ct).ConfigureAwait(false);
        if (model is null)
            return BadRequest(new { error = "引用的工艺数据模型版本不存在。" });

        const string placeholderCode = "__probe_only__";
        var needsDiscoveryPlaceholder =
            request.Protocol is AcquisitionProtocols.HttpPolling or AcquisitionProtocols.Mqtt or AcquisitionProtocols.OpcUa &&
            request.ValueMappings.All(item => string.IsNullOrWhiteSpace(item.DataItemCode) ||
                                              string.IsNullOrWhiteSpace(item.SourcePath));
        var probeRequest = needsDiscoveryPlaceholder
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
        if (!TryNormalize(probeRequest, out var normalized, out var error))
            return BadRequest(new { error });

        if (!ValidateMappings(normalized!, model, out error))
            return BadRequest(new { error });

        var probe = await ProbeEdgeAsync(
            new AcquisitionDeployment { Profile = normalized!, DataModel = model },
            ct).ConfigureAwait(false);
        if (!probe.Success)
            return StatusCode(probe.StatusCode, new { error = probe.Error });
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
        CancellationToken cancellationToken)
    {
        try
        {
            var timeout = TimeSpan.FromMilliseconds(Math.Clamp(
                deployment.Profile.Execution.TimeoutMs + 15_000,
                15_000,
                120_000));
            var result = await probeTasks.QueueAndWaitAsync(
                deployment,
                timeout,
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
    public IActionResult NextProbeTask([FromQuery] string edgeId)
    {
        var normalizedEdgeId = edgeId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEdgeId))
            return BadRequest(new { error = "edgeId 不能为空。" });
        if (!edgeTokenValidator.IsAuthorized(normalizedEdgeId, Request.Headers.Authorization.ToString()))
            return Unauthorized(new { error = "边缘节点认证失败。" });
        var task = probeTasks.ClaimNext(normalizedEdgeId);
        return task is null ? NoContent() : Ok(task);
    }

    [HttpPost("probe-tasks/{taskId}/result")]
    [AllowAnonymous]
    public IActionResult CompleteProbeTask(
        string taskId,
        [FromBody] AcquisitionProbeTaskCompletion? completion)
    {
        if (completion is null ||
            !string.Equals(taskId, completion.TaskId, StringComparison.Ordinal))
            return BadRequest(new { error = "探查任务结果与路由不匹配。" });
        if (!edgeTokenValidator.IsAuthorized(completion.EdgeId, Request.Headers.Authorization.ToString()))
            return Unauthorized(new { error = "边缘节点认证失败。" });
        return probeTasks.Complete(completion)
            ? NoContent()
            : NotFound(new { error = "探查任务不存在、已过期或不属于当前 Edge。" });
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

    [HttpDelete("{profileId}/{version:int}")]
    public async Task<IActionResult> Delete(string profileId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var existing = await store.GetAsync(NormalizeCode(profileId), version, ct).ConfigureAwait(false);
        if (existing is null) return NotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return Conflict(new { error = "只有草稿采集配置可以删除。" });
        return await store.DeleteAsync(existing.ProfileId, version, ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
    }

    /// <summary>
    ///     校验与规范化已经迁移到 <see cref="AcquisitionProfileValidator"/>（Ingot.Contracts）。
    ///
    ///     原因：这套规则以前只住在本控制器内部，边缘节点从本地缓存恢复配置时会完全绕过它，
    ///     MELSEC 选择器语法也因此没有任何服务端校验。移到公共契约后，平台、边缘节点
    ///     和配置界面共用同一份判断，并且由协议能力矩阵裁决"这个字段对该协议是否真的生效"。
    /// </summary>
    private static bool TryNormalize(
        AcquisitionProfile? value,
        out AcquisitionProfile? normalized,
        out string error)
        => AcquisitionProfileValidator.TryValidate(value, out normalized, out error);

    private static bool ValidateMappings(AcquisitionProfile profile, ProcessDataModel model, out string error)
    {
        var dataItems = model.Acquisition.DataItems.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var unknown = profile.ValueMappings.FirstOrDefault(item => !dataItems.ContainsKey(item.DataItemCode));
        if (unknown is not null) return Fail($"数据项未在工艺数据模型中定义：{unknown.DataItemCode}。", out error);
        if (profile.Status == ConfigurationStatuses.Published)
        {
            var missing = dataItems.Values.FirstOrDefault(item => !item.Nullable &&
                profile.ValueMappings.All(mapping => mapping.DataItemCode != item.Code));
            if (missing is not null) return Fail($"缺少必填数据项映射：{missing.Code}。", out error);
        }
        if (profile.Recipe is not null)
        {
            if (string.IsNullOrWhiteSpace(profile.Recipe.IdPath) ||
                string.IsNullOrWhiteSpace(profile.Recipe.VersionPath) ||
                string.IsNullOrWhiteSpace(profile.Recipe.ParametersPath))
                return Fail("启用配方采集后，配方编号、版本和参数路径不能为空。", out error);
            if (profile.Recipe.ParameterMappings.Any(item =>
                    !CodePattern().IsMatch(item.DataItemCode) || string.IsNullOrWhiteSpace(item.SourcePath)) ||
                profile.Recipe.ParameterMappings.Select(item => item.DataItemCode)
                    .Distinct(StringComparer.Ordinal).Count() != profile.Recipe.ParameterMappings.Count)
                return Fail("配方参数映射无效或重复。", out error);
            var definitions = model.RecipeParameters.ToDictionary(item => item.Code, StringComparer.Ordinal);
            var unknownParameter = profile.Recipe.ParameterMappings.FirstOrDefault(item => !definitions.ContainsKey(item.DataItemCode));
            if (unknownParameter is not null)
                return Fail($"配方参数未在工艺数据模型中定义：{unknownParameter.DataItemCode}。", out error);
        }
        if (profile.Lifecycle is not null)
        {
            var lifecycle = profile.Lifecycle;
            if (lifecycle.Mode != "discrete-cycle" ||
                (!string.IsNullOrWhiteSpace(lifecycle.CorrelationIdContextKey) &&
                 !CodePattern().IsMatch(lifecycle.CorrelationIdContextKey)) ||
                !EventTypePattern().IsMatch(lifecycle.StartedEventType) ||
                !EventTypePattern().IsMatch(lifecycle.CompletedEventType) ||
                !EventTypePattern().IsMatch(lifecycle.StepChangedEventType))
            {
                return Fail("周期边界配置无效。", out error);
            }
            if (!string.IsNullOrWhiteSpace(lifecycle.CorrelationIdContextKey) &&
                profile.ContextMappings.All(item =>
                    item.ContextKey != lifecycle.CorrelationIdContextKey))
            {
                return Fail($"周期边界缺少关联号上下文映射：{lifecycle.CorrelationIdContextKey}。", out error);
            }
            if (string.IsNullOrWhiteSpace(lifecycle.CorrelationIdContextKey) &&
                string.IsNullOrWhiteSpace(lifecycle.ActiveContextKey))
            {
                return Fail("周期边界必须配置生产状态上下文映射，由 Edge 自动生成周期关联号。", out error);
            }
            if (!string.IsNullOrWhiteSpace(lifecycle.ActiveContextKey) &&
                (string.IsNullOrWhiteSpace(lifecycle.ActiveValue) ||
                 profile.ContextMappings.All(item =>
                     item.ContextKey != lifecycle.ActiveContextKey)))
            {
                return Fail(
                    $"周期边界缺少运行激活状态上下文映射：{lifecycle.ActiveContextKey}。",
                    out error);
            }
        }
        error = string.Empty;
        return true;
    }

    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static bool Fail(string message, out string error) { error = message; return false; }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EventTypePattern();
}
