using Ingot.Contracts.Events;
using Npgsql;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public interface IProcessExecutionAnalysisMaterializationStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<ProcessExecutionAnalysisSnapshot?> TryLoadAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        ProcessExecutionAnalysisSourceFingerprint source,
        CancellationToken ct = default);

    /// <summary>
    ///     Loads the current ready snapshot without re-reading raw events to rebuild a fingerprint.
    ///     Ingestion invalidates affected executions atomically, so only status=ready is reusable.
    /// </summary>
    Task<ProcessExecutionAnalysisSnapshot?> TryLoadLatestAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        CancellationToken ct = default)
        => Task.FromResult<ProcessExecutionAnalysisSnapshot?>(null);

    Task<ProcessExecutionAnalysisSnapshot> SaveAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        ProcessExecutionAnalysisSourceFingerprint source,
        WholeProcessExecutionAnalysisResult analysis,
        CancellationToken ct = default);

    Task MarkDirtyAsync(
        IReadOnlyCollection<string> executionIds,
        long invalidatedSourceMaxIngestId,
        string reason,
        CancellationToken ct = default);

    Task MarkDirtyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<string> executionIds,
        long invalidatedSourceMaxIngestId,
        string reason,
        CancellationToken ct = default)
        => MarkDirtyAsync(executionIds, invalidatedSourceMaxIngestId, reason, ct);

    Task<ProcessExecutionAnalysisBackfillJob> AddBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        CancellationToken ct = default)
        => throw new NotSupportedException("当前过程执行分析存储不支持回填任务。");

    Task<ProcessExecutionAnalysisBackfillLease?> ClaimBackfillJobAsync(
        TimeSpan leaseTimeout,
        CancellationToken ct = default)
        => Task.FromResult<ProcessExecutionAnalysisBackfillLease?>(null);

    Task<bool> SaveClaimedBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        Guid leaseId,
        bool releaseLease,
        CancellationToken ct = default)
        => Task.FromResult(false);

    Task<ProcessExecutionAnalysisBackfillJob?> GetBackfillJobAsync(
        Guid jobId,
        CancellationToken ct = default)
        => Task.FromResult<ProcessExecutionAnalysisBackfillJob?>(null);

    Task<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>> ListBackfillJobsAsync(
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProcessExecutionAnalysisBackfillJob>>([]);

    Task<IReadOnlyList<ProcessExecutionFeatureAggregate>> QueryFeatureAggregatesAsync(
        string? signalCode,
        string? phaseCode,
        string? featureCode,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ProcessExecutionFeatureAggregate>>([]);

    Task<ProcessExecutionAnalysisRecomputeLease?> ClaimRecomputeAsync(
        TimeSpan leaseTimeout,
        CancellationToken ct = default)
        => Task.FromResult<ProcessExecutionAnalysisRecomputeLease?>(null);

    Task<bool> CompleteRecomputeAsync(
        string executionId,
        Guid leaseId,
        CancellationToken ct = default)
        => Task.FromResult(false);

    Task<bool> RetryRecomputeAsync(
        string executionId,
        Guid leaseId,
        TimeSpan delay,
        CancellationToken ct = default)
        => Task.FromResult(false);
}

public sealed record ProcessExecutionAnalysisBackfillLease(
    ProcessExecutionAnalysisBackfillJob Job,
    Guid LeaseId,
    int AttemptCount);

public sealed record ProcessExecutionAnalysisRecomputeLease(
    string ExecutionId,
    Guid LeaseId,
    int AttemptCount);

public sealed record ProcessExecutionAnalysisMaterializationKey(
    string ExecutionId,
    string AlgorithmVersion,
    string DataModelId,
    int DataModelVersion,
    string AnalysisPlanId,
    int AnalysisPlanVersion);

public sealed record ProcessExecutionAnalysisSourceFingerprint(
    long MinIngestId,
    long MaxIngestId,
    int EventCount,
    string ContentHash);

public sealed record ProcessExecutionAnalysisSnapshot(
    WholeProcessExecutionAnalysisResult Analysis,
    DateTimeOffset ComputedAt,
    ProcessExecutionAnalysisSourceFingerprint Source);
