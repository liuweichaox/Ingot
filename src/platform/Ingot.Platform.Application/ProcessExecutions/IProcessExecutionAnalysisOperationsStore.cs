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
