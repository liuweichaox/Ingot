// 编排运行分析回填、聚合查询和失败任务重放，不直接访问数据库。
using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.ProcessExecutions;

/// <summary>持久化运行分析运维任务和特征聚合。</summary>
public interface IProcessExecutionAnalysisOperationsStore
{
    Task<ProcessExecutionAnalysisBackfillJob> AddBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        CancellationToken ct = default);

    Task<ProcessExecutionAnalysisBackfillJob?> GetBackfillJobAsync(
        Guid jobId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>> ListBackfillJobsAsync(
        CancellationToken ct = default);

    Task<IReadOnlyList<ProcessExecutionFeatureAggregate>> QueryFeatureAggregatesAsync(
        string siteId,
        string? signalCode,
        string? phaseCode,
        string? featureCode,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken ct = default);

    Task<bool> ReplayFailedRecomputeAsync(
        string executionId,
        CancellationToken ct = default);

    Task<IReadOnlyList<string>> ResolveExecutionSitesAsync(
        string executionId,
        CancellationToken ct = default);
}

public enum ProcessExecutionReplayResult
{
    Accepted,
    ExecutionNotFound,
    FailedJobNotFound
}

/// <summary>在授权运行范围内编排分析回填和可恢复重放。</summary>
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
        if (string.IsNullOrWhiteSpace(request.SiteId))
            throw new ArgumentException("回填任务必须指定站点。", nameof(request));
        if (request.From > request.To)
            throw new ArgumentException("回填开始时间不能晚于结束时间。", nameof(request));
        if (request.PageSize is < 10 or > 500)
            throw new ArgumentException("回填每批数量必须在 10 到 500 之间。", nameof(request));
        var normalized = request with
        {
            SiteId = request.SiteId.Trim(),
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
        string siteId,
        string? signalCode,
        string? phaseCode,
        string? featureCode,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken ct = default) => store.QueryFeatureAggregatesAsync(
        siteId,
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
        var observedSites = await store.ResolveExecutionSitesAsync(executionId, ct).ConfigureAwait(false);
        if (observedSites.Count != 1 ||
            !string.Equals(observedSites[0], siteId, StringComparison.OrdinalIgnoreCase))
            return ProcessExecutionReplayResult.ExecutionNotFound;
        return await store.ReplayFailedRecomputeAsync(executionId, ct).ConfigureAwait(false)
            ? ProcessExecutionReplayResult.Accepted
            : ProcessExecutionReplayResult.FailedJobNotFound;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
