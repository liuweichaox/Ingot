using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>维护过程执行分析物化状态及其重算、失效和回填任务。</summary>
public interface IProcessExecutionAnalysisOperationsStore
{
    Task<ProcessExecutionAnalysisBackfillJob> AddBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        CancellationToken ct = default) =>
        throw new NotSupportedException("当前过程执行分析存储不支持回填任务。");

    Task<ProcessExecutionAnalysisBackfillJob?> GetBackfillJobAsync(
        Guid jobId,
        CancellationToken ct = default) =>
        Task.FromResult<ProcessExecutionAnalysisBackfillJob?>(null);

    Task<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>> ListBackfillJobsAsync(
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>>([]);

    Task<IReadOnlyList<ProcessExecutionFeatureAggregate>> QueryFeatureAggregatesAsync(
        string? signalCode,
        string? phaseCode,
        string? featureCode,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ProcessExecutionFeatureAggregate>>([]);

    Task<bool> ReplayFailedRecomputeAsync(
        string executionId,
        CancellationToken ct = default) => Task.FromResult(false);
}

public enum ProcessExecutionReplayResult
{
    Accepted,
    ExecutionNotFound,
    FailedJobNotFound
}

/// <summary>协调过程执行分析的重算、回填和物化状态管理。</summary>
public sealed class ProcessExecutionAnalysisOperationsService(
    IProcessExecutionAnalysisOperationsStore store,
    IExecutionBoundaryStore boundaries,
    IProcessExecutionService executions)
{
    public Task<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>> ListBackfillJobsAsync(
        CancellationToken ct = default) => store.ListBackfillJobsAsync(ct);

    public Task<ProcessExecutionAnalysisBackfillJob?> GetBackfillJobAsync(
        Guid jobId,
        CancellationToken ct = default) => store.GetBackfillJobAsync(jobId, ct);

    public async Task<ProcessExecutionAnalysisBackfillJob> EnqueueBackfillAsync(
        ProcessExecutionAnalysisBackfillRequest request,
        string userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
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
        return await store.AddBackfillJobAsync(job, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ProcessExecutionFeatureAggregate>> QueryFeatureAggregatesAsync(
        string? signalCode,
        string? phaseCode,
        string? featureCode,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken ct = default) => store.QueryFeatureAggregatesAsync(
        signalCode,
        phaseCode,
        featureCode,
        from,
        to,
        limit,
        ct);

    public async Task<ProcessExecutionReplayResult> ReplayBoundaryAsync(
        string siteId,
        string executionId,
        CancellationToken ct = default) =>
        await boundaries.ReplayFailedProjectionAsync(siteId, executionId, ct).ConfigureAwait(false)
            ? ProcessExecutionReplayResult.Accepted
            : ProcessExecutionReplayResult.FailedJobNotFound;

    public async Task<ProcessExecutionReplayResult> ReplayAnalysisAsync(
        string siteId,
        string executionId,
        CancellationToken ct = default)
    {
        var authorized = await executions.QueryAsync(
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            executionId,
            null,
            1,
            ct: ct,
            siteId: siteId).ConfigureAwait(false);
        if (authorized.Data.Count == 0)
            return ProcessExecutionReplayResult.ExecutionNotFound;
        return await store.ReplayFailedRecomputeAsync(executionId, ct).ConfigureAwait(false)
            ? ProcessExecutionReplayResult.Accepted
            : ProcessExecutionReplayResult.FailedJobNotFound;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
