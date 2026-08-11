using Ingot.Contracts.Events;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/process-execution-analysis-backfills")]
public sealed class ProcessExecutionAnalysisBackfillsController(
    IProcessExecutionAnalysisMaterializationStore store,
    ProcessExecutionAnalysisBackfillService backfill,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await store.ListBackfillJobsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var job = await store.GetBackfillJobAsync(jobId, ct).ConfigureAwait(false);
        return job is null ? NotFound() : Ok(job);
    }

    [HttpPost]
    public async Task<IActionResult> Enqueue(
        [FromBody] ProcessExecutionAnalysisBackfillRequest request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Accepted(await backfill.EnqueueAsync(request, ResolveUserId()!, ct).ConfigureAwait(false));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}

[ApiController]
[Route("api/v1/process-feature-aggregates")]
public sealed class ProcessExecutionFeatureAggregatesController(
    IProcessExecutionAnalysisMaterializationStore store,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> Query(
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
        if (from > to)
            return BadRequest(new { error = "开始时间不能晚于结束时间。" });
        if (limit is < 1 or > 500)
            return BadRequest(new { error = "Limit 必须在 1 到 500 之间。" });
        var rows = await store.QueryFeatureAggregatesAsync(
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
