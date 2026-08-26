// 提供站点隔离的分析回填、特征聚合和失败任务重放运维接口。
using Ingot.Contracts.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ProcessExecutions;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-execution-analysis-backfills")]
public sealed class ProcessExecutionAnalysisBackfillsController(
    ProcessExecutionAnalysisOperationsService operations,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var identity = ResolveIdentity()!;
        var jobs = await operations.ListBackfillJobsAsync(ct).ConfigureAwait(false);
        return Ok(new
        {
            data = jobs.Where(job => identity.CanAccessSite(job.Request.SiteId)).ToArray()
        });
    }

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var job = await operations.GetBackfillJobAsync(jobId, ct).ConfigureAwait(false);
        return job is null || !ResolveIdentity()!.CanAccessSite(job.Request.SiteId)
            ? ResourceNotFound()
            : Ok(job);
    }

    [HttpPost]
    public async Task<IActionResult> Enqueue(
        [FromBody] ProcessExecutionAnalysisBackfillRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (request is null)
            return InvalidRequest("请求体不能为空。");
        if (string.IsNullOrWhiteSpace(request.SiteId))
            return InvalidRequest("siteId 不能为空。");
        if (!ResolveIdentity()!.CanAccessSite(request.SiteId))
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", request.SiteId));
        try
        {
            return Accepted(await operations.EnqueueBackfillAsync(request, ResolveUserId()!, ct)
                .ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }
}

[ApiController]
[Route("api/v1/process-feature-aggregates")]
public sealed class ProcessExecutionFeatureAggregatesController(
    ProcessExecutionAnalysisOperationsService operations,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] string? siteId,
        [FromQuery] string? signalCode,
        [FromQuery] string? phaseCode,
        [FromQuery] string? featureCode,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        if (string.IsNullOrWhiteSpace(siteId))
            return InvalidRequest("siteId 不能为空。");
        if (!ResolveIdentity()!.CanAccessSite(siteId))
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        if (from > to)
            return InvalidRequest("开始时间不能晚于结束时间。");
        if (limit is < 1 or > 500)
            return InvalidRequest("Limit 必须在 1 到 500 之间。");
        var rows = await operations.QueryFeatureAggregatesAsync(
            siteId.Trim(),
            signalCode,
            phaseCode,
            featureCode,
            from,
            to,
            limit,
            ct).ConfigureAwait(false);
        return Ok(new
        {
            data = rows,
            aggregation = "database",
            onlyReadyMaterializations = true
        });
    }
}

[ApiController]
[Route("api/v1/process-execution-maintenance")]
public sealed class ProcessExecutionMaintenanceController(
    ProcessExecutionAnalysisOperationsService operations,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpPost("boundary-jobs/{siteId}/{executionId}:replay")]
    public async Task<IActionResult> ReplayBoundary(
        string siteId,
        string executionId,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var identity = ResolveIdentity()!;
        if (!identity.CanAccessSite(siteId))
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        var result = await operations.ReplayBoundaryAsync(siteId.Trim(), executionId.Trim(), ct)
            .ConfigureAwait(false);
        return result == ProcessExecutionReplayResult.Accepted
            ? Accepted()
            : ResourceNotFound("未找到对应的失败边界重算任务。");
    }

    [HttpPost("analysis-jobs/{siteId}/{executionId}:replay")]
    public async Task<IActionResult> ReplayAnalysis(
        string siteId,
        string executionId,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var identity = ResolveIdentity()!;
        if (!identity.CanAccessSite(siteId))
            return AuthorizationDenied("当前身份无权访问该站点。", ("siteId", siteId));
        var result = await operations.ReplayAnalysisAsync(siteId.Trim(), executionId.Trim(), ct)
            .ConfigureAwait(false);
        return result switch
        {
            ProcessExecutionReplayResult.Accepted => Accepted(),
            ProcessExecutionReplayResult.ExecutionNotFound =>
                ResourceNotFound("未找到对应站点的过程执行。"),
            _ => ResourceNotFound("未找到对应的失败分析重算任务。")
        };
    }
}
