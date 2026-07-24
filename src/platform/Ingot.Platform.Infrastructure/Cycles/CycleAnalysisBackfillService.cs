using System.Threading.Channels;
using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class CycleAnalysisBackfillService(
    ICycleAnalysisMaterializationStore store,
    ICycleRecordService cycles,
    ILogger<CycleAnalysisBackfillService> logger) : BackgroundService
{
    private readonly Channel<Guid> _queue = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public async Task<CycleAnalysisBackfillJob> EnqueueAsync(
        CycleAnalysisBackfillRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (request.From > request.To)
            throw new ArgumentException("回填开始时间不能晚于结束时间。", nameof(request));
        if (request.PageSize is < 10 or > 500)
            throw new ArgumentException("回填每批数量必须在 10 到 500 之间。", nameof(request));
        var normalized = request with
        {
            ProductSeries = Normalize(request.ProductSeries),
            ProductCode = Normalize(request.ProductCode),
            RecipeId = Normalize(request.RecipeId),
            MachineId = Normalize(request.MachineId)
        };
        var job = new CycleAnalysisBackfillJob
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
            var offset = job.ProcessedCycles;
            while (true)
            {
                var page = await cycles.QueryAsync(
                    job.Request.From,
                    job.Request.To,
                    job.Request.ProductSeries,
                    job.Request.ProductCode,
                    job.Request.RecipeId,
                    job.Request.MachineId,
                    null,
                    null,
                    "completed",
                    job.Request.PageSize,
                    offset,
                    ct).ConfigureAwait(false);
                if (page.Data.Count == 0)
                    break;
                var materialized = page.Data.Count(row =>
                    row.AnalysisMaterialization.Status is "materialized" or "cached");
                var failed = page.Data.Count - materialized;
                offset += page.Data.Count;
                job = job with
                {
                    TotalCycles = page.Total,
                    ProcessedCycles = offset,
                    MaterializedCycles = job.MaterializedCycles + materialized,
                    FailedCycles = job.FailedCycles + failed,
                    LastCorrelationId = page.Data[^1].CorrelationId
                };
                await store.SaveBackfillJobAsync(job, ct).ConfigureAwait(false);
                if (offset >= page.Total)
                    break;
            }
            job = job with
            {
                Status = job.FailedCycles == 0 ? "completed" : "completed_with_errors",
                CompletedAt = DateTimeOffset.UtcNow
            };
            await store.SaveBackfillJobAsync(job, ct).ConfigureAwait(false);
            logger.LogInformation(
                "周期分析回填 {JobId} 完成：{Materialized}/{Total} 个周期已物化",
                job.JobId,
                job.MaterializedCycles,
                job.TotalCycles);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await store.SaveBackfillJobAsync(job with { Status = "queued" }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "周期分析回填 {JobId} 失败", job.JobId);
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
