using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionAnalysisBackfillService(
    IProcessExecutionAnalysisMaterializationStore store,
    IProcessExecutionService executions,
    ILogger<ProcessExecutionAnalysisBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            var lease = await store.ClaimBackfillJobAsync(TimeSpan.FromMinutes(15), stoppingToken)
                .ConfigureAwait(false);
            if (lease is not null)
                await ProcessAsync(lease, stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task ProcessAsync(ProcessExecutionAnalysisBackfillLease lease, CancellationToken ct)
    {
        var job = lease.Job with
        {
            Status = "running",
            StartedAt = lease.Job.StartedAt ?? DateTimeOffset.UtcNow,
            Error = null
        };
        if (!await store.SaveClaimedBackfillJobAsync(job, lease.LeaseId, false, ct).ConfigureAwait(false))
            return;
        try
        {
            var offset = job.ProcessedProcessExecutions;
            while (true)
            {
                var page = await executions.QueryAsync(
                    job.Request.From,
                    job.Request.To,
                    job.Request.ProductFamilyCode,
                    job.Request.ProductCode,
                    job.Request.ProcessSpecificationId,
                    job.Request.EquipmentId,
                    null,
                    null,
                    "completed",
                    job.Request.PageSize,
                    offset,
                    null,
                    ct).ConfigureAwait(false);
                if (page.Data.Count == 0)
                    break;
                var materialized = page.Data.Count(row =>
                    row.AnalysisMaterialization.Status is "materialized" or "cached");
                var failed = page.Data.Count - materialized;
                offset += page.Data.Count;
                job = job with
                {
                    TotalProcessExecutions = page.Total,
                    ProcessedProcessExecutions = offset,
                    MaterializedProcessExecutions = job.MaterializedProcessExecutions + materialized,
                    FailedProcessExecutions = job.FailedProcessExecutions + failed,
                    LastExecutionId = page.Data[^1].ExecutionId
                };
                if (!await store.SaveClaimedBackfillJobAsync(job, lease.LeaseId, false, ct)
                        .ConfigureAwait(false))
                    return;
                if (offset >= page.Total)
                    break;
            }
            job = job with
            {
                Status = job.FailedProcessExecutions == 0 ? "completed" : "completed_with_errors",
                CompletedAt = DateTimeOffset.UtcNow
            };
            await store.SaveClaimedBackfillJobAsync(job, lease.LeaseId, true, ct).ConfigureAwait(false);
            logger.LogInformation(
                "过程执行分析回填 {JobId} 完成：{Materialized}/{Total} 个过程执行已物化",
                job.JobId,
                job.MaterializedProcessExecutions,
                job.TotalProcessExecutions);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await store.SaveClaimedBackfillJobAsync(
                    job with { Status = "queued" }, lease.LeaseId, true, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "过程执行分析回填 {JobId} 失败", job.JobId);
            await store.SaveClaimedBackfillJobAsync(
                job with
                {
                    Status = "failed",
                    Error = exception.Message,
                    CompletedAt = DateTimeOffset.UtcNow
                },
                lease.LeaseId,
                true,
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
