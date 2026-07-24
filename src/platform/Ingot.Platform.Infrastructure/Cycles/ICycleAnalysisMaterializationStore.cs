using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.Cycles;

public interface ICycleAnalysisMaterializationStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<CycleAnalysisSnapshot?> TryLoadAsync(
        CycleAnalysisMaterializationKey key,
        long sourceMaxIngestId,
        int sourceEventCount,
        CancellationToken ct = default);

    Task<CycleAnalysisSnapshot> SaveAsync(
        CycleAnalysisMaterializationKey key,
        long sourceMaxIngestId,
        int sourceEventCount,
        WholeCycleAnalysisResult analysis,
        CancellationToken ct = default);

    Task MarkDirtyAsync(
        IReadOnlyCollection<string> correlationIds,
        long invalidatedSourceMaxIngestId,
        string reason,
        CancellationToken ct = default);

    Task<CycleAnalysisBackfillJob> AddBackfillJobAsync(
        CycleAnalysisBackfillJob job,
        CancellationToken ct = default)
        => throw new NotSupportedException("当前周期分析存储不支持回填任务。");

    Task<CycleAnalysisBackfillJob> SaveBackfillJobAsync(
        CycleAnalysisBackfillJob job,
        CancellationToken ct = default)
        => throw new NotSupportedException("当前周期分析存储不支持回填任务。");

    Task<CycleAnalysisBackfillJob?> GetBackfillJobAsync(
        Guid jobId,
        CancellationToken ct = default)
        => Task.FromResult<CycleAnalysisBackfillJob?>(null);

    Task<IReadOnlyList<CycleAnalysisBackfillJob>> ListBackfillJobsAsync(
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CycleAnalysisBackfillJob>>([]);

    Task<IReadOnlyList<CycleFeatureAggregate>> QueryFeatureAggregatesAsync(
        string? signalCode,
        string? phaseCode,
        string? featureCode,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int limit,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CycleFeatureAggregate>>([]);

    Task<IReadOnlyList<string>> ListDirtyCorrelationIdsAsync(
        int limit,
        CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);
}

public sealed record CycleAnalysisMaterializationKey(
    string CorrelationId,
    string AlgorithmVersion,
    string DataModelId,
    int DataModelVersion,
    string AnalysisPlanId,
    int AnalysisPlanVersion);

public sealed record CycleAnalysisSnapshot(
    WholeCycleAnalysisResult Analysis,
    DateTimeOffset ComputedAt,
    long SourceMaxIngestId,
    int SourceEventCount);
