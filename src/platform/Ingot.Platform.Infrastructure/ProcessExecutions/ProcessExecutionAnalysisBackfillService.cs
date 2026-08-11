using System.Threading.Channels;
using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionAnalysisBackfillService(
    IProcessExecutionAnalysisMaterializationStore store,
    IProcessExecutionService executions,
    ILogger<ProcessExecutionAnalysisBackfillService> logger) : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<ProcessExecutionAnalysisBackfillJob> EnqueueAsync(
        ProcessExecutionAnalysisBackfillRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (request.From > request.To)
            throw new ArgumentException("回填开始时间不能晚于结束时间。", nameof(request));
        if (request.PageSize is < 10 or > 500)
            throw new ArgumentException("回填每批数量必须在 10 到 500 之间。", nameof(request));
        var normalized = request with
        {
            ProductFamilyCode = Normalize(request.ProductFamilyCode),
            ProductCode = Normalize(request.ProductCode),
            ProcessSpecificationId = Normalize(request.ProcessSpecificationId),
            EquipmentId = Normalize(request.EquipmentId)
        };
        var job = new ProcessExecutionAnalysisBackfillJob
        {
            JobId = Guid.CreateVersion7(),
            Request = normalized,
            Status = "queued",
            CreatedBy = userId,
            CreatedAt = DateTimeOffset.UtcNow
        };
        await store.AddBackfillJobAsync(job, ct).ConfigureAwait(false);
        await _queue.Writer.WriteAsync(job.JobId, ct).ConfigureAwait(false);
        return job;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var incomplete = (await store.ListBackfillJobsAsync(stoppingToken).ConfigureAwait(false))
            .Where(static job => job.Status is "queued" or "running")
            .OrderBy(static job => job.CreatedAt)
            .ToArray();
        foreach (var job in incomplete)
            await _queue.Writer.WriteAsync(job.JobId, stoppingToken).ConfigureAwait(false);

        await foreach (var jobId in _queue.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            await ProcessAsync(jobId, stoppingToken).ConfigureAwait(false);
    }

    private async Task ProcessAsync(Guid jobId, CancellationToken ct)
    {
        var job = await store.GetBackfillJobAsync(jobId, ct).ConfigureAwait(false);
        if (job is null || job.Status is "completed" or "completed_with_errors" or "failed")
            return;
        job = job with
        {
            Status = "running",
            StartedAt = job.StartedAt ?? DateTimeOffset.UtcNow,
            Error = null
        };
        await store.SaveBackfillJobAsync(job, ct).ConfigureAwait(false);
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
                await store.SaveBackfillJobAsync(job, ct).ConfigureAwait(false);
                if (offset >= page.Total)
                    break;
            }
            job = job with
            {
                Status = job.FailedProcessExecutions == 0 ? "completed" : "completed_with_errors",
                CompletedAt = DateTimeOffset.UtcNow
            };
            await store.SaveBackfillJobAsync(job, ct).ConfigureAwait(false);
            logger.LogInformation(
                "过程执行分析回填 {JobId} 完成：{Materialized}/{Total} 个过程执行已物化",
                job.JobId,
                job.MaterializedProcessExecutions,
                job.TotalProcessExecutions);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await store.SaveBackfillJobAsync(job with { Status = "queued" }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "过程执行分析回填 {JobId} 失败", job.JobId);
            await store.SaveBackfillJobAsync(
                job with
                {
                    Status = "failed",
                    Error = exception.Message,
                    CompletedAt = DateTimeOffset.UtcNow
                },
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
