// 定义运行分析物化、失效标记、租约和运维任务的数据库事务边界。
using Ingot.Contracts.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Npgsql;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

/// <summary>以显式租约和事务保存可重算的运行分析物化结果。</summary>
public interface IProcessExecutionAnalysisMaterializationStore : IProcessExecutionAnalysisOperationsStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<ProcessExecutionAnalysisSnapshot?> TryLoadAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        ProcessExecutionAnalysisSourceFingerprint source,
        CancellationToken ct = default);

    Task<ProcessExecutionAnalysisSnapshot?> TryLoadLatestAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        CancellationToken ct = default);

    Task<ProcessExecutionAnalysisSnapshot> SaveAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        ProcessExecutionAnalysisSourceFingerprint source,
        WholeProcessExecutionAnalysisResult analysis,
        CancellationToken ct = default);

    Task<ProcessExecutionAnalysisSnapshot?> TrySaveForUniqueSiteAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        string siteId,
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
        CancellationToken ct = default);

    Task<ProcessExecutionAnalysisBackfillLease?> ClaimBackfillJobAsync(
        TimeSpan leaseTimeout,
        CancellationToken ct = default);

    Task<bool> SaveClaimedBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        Guid leaseId,
        bool releaseLease,
        CancellationToken ct = default);

    Task<ProcessExecutionAnalysisRecomputeLease?> ClaimRecomputeAsync(
        TimeSpan leaseTimeout,
        CancellationToken ct = default);

    Task<bool> CompleteRecomputeAsync(
        string executionId,
        Guid leaseId,
        CancellationToken ct = default);

    Task<bool> RetryRecomputeAsync(
        string executionId,
        Guid leaseId,
        TimeSpan delay,
        string error,
        int maxAttempts,
        CancellationToken ct = default);

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
