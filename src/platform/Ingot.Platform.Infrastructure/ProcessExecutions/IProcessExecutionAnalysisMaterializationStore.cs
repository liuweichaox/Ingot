using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public interface IProcessExecutionAnalysisMaterializationStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<ProcessExecutionAnalysisSnapshot?> TryLoadAsync(
        ProcessExecutionAnalysisMaterializationKey key,
        ProcessExecutionAnalysisSourceFingerprint source,
        CancellationToken ct = default);

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

    Task<ProcessExecutionAnalysisBackfillJob> AddBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        CancellationToken ct = default)
        => throw new NotSupportedException("当前过程执行分析存储不支持回填任务。");

    Task<ProcessExecutionAnalysisBackfillJob> SaveBackfillJobAsync(
        ProcessExecutionAnalysisBackfillJob job,
        CancellationToken ct = default)
        => throw new NotSupportedException("当前过程执行分析存储不支持回填任务。");

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

    Task<IReadOnlyList<string>> ListDirtyExecutionIdsAsync(
        int limit,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}

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
