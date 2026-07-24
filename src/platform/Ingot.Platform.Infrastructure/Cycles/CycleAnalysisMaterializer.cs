using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class CycleAnalysisMaterializer(
    ICycleAnalysisMaterializationStore store,
    WholeCycleAnalysisEngine engine,
    ILogger<CycleAnalysisMaterializer> logger,
    PostgresCycleScientificComputeEngine? databaseEngine = null)
{
    private readonly SemaphoreSlim[] _cycleLocks = Enumerable.Range(0, 64)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task<MaterializedCycleAnalysis> GetOrComputeAsync(
        string correlationId,
        IReadOnlyList<PlatformProductionEvent> rows,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        ProcessDataModel? dataModel,
        ProcessAnalysisPlan? plan,
        CancellationToken ct = default)
    {
        var sourceMaxIngestId = rows.Count == 0 ? 0 : rows.Max(static row => row.IngestId);
        var sourceEventCount = rows.Count;
        if (!completedAt.HasValue)
        {
            return QueryTime(
                engine.Analyze(rows, startedAt, completedAt, dataModel, plan),
                sourceMaxIngestId,
                sourceEventCount);
        }

        var key = new CycleAnalysisMaterializationKey(
            correlationId,
            WholeCycleAnalysisEngine.AlgorithmVersion,
            dataModel?.ModelId ?? string.Empty,
            dataModel?.Version ?? 0,
            plan?.PlanId ?? string.Empty,
            plan?.Version ?? 0);
        var gate = _cycleLocks[
            (StringComparer.Ordinal.GetHashCode(correlationId) & int.MaxValue) % _cycleLocks.Length];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                var cached = await store.TryLoadAsync(key, sourceMaxIngestId, sourceEventCount, ct)
                    .ConfigureAwait(false);
                if (cached is not null)
                    return FromSnapshot(cached, "cached");

                var analysis = engine.Analyze(rows, startedAt, completedAt, dataModel, plan);
                if (databaseEngine is not null &&
                    startedAt.HasValue &&
                    completedAt.HasValue &&
                    dataModel is not null &&
                    plan is not null)
                {
                    analysis = await databaseEngine.ComputeAndVerifyAsync(
                        correlationId,
                        startedAt.Value,
                        completedAt.Value,
                        analysis,
                        ct).ConfigureAwait(false);
                }
                var saved = await store.SaveAsync(
                    key, sourceMaxIngestId, sourceEventCount, analysis, ct).ConfigureAwait(false);
                var verified = await store.TryLoadAsync(key, sourceMaxIngestId, sourceEventCount, ct)
                    .ConfigureAwait(false);
                if (verified is null)
                {
                    logger.LogInformation(
                        "周期 {CorrelationId} 在物化期间收到更新，当前请求使用本次确定性计算结果并等待下次重算",
                        correlationId);
                    return QueryTime(analysis, sourceMaxIngestId, sourceEventCount);
                }
                return FromSnapshot(saved, "materialized");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "周期 {CorrelationId} 的分析物化不可用，降级为查询时确定性计算",
                    correlationId);
                return QueryTime(
                    engine.Analyze(rows, startedAt, completedAt, dataModel, plan),
                    sourceMaxIngestId,
                    sourceEventCount);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static MaterializedCycleAnalysis FromSnapshot(CycleAnalysisSnapshot snapshot, string status)
        => new(
            snapshot.Analysis,
            new CycleAnalysisMaterialization
            {
                Status = status,
                AlgorithmVersion = WholeCycleAnalysisEngine.AlgorithmVersion,
                ComputedAt = snapshot.ComputedAt,
                SourceMaxIngestId = snapshot.SourceMaxIngestId,
                SourceEventCount = snapshot.SourceEventCount
            });

    private static MaterializedCycleAnalysis QueryTime(
        WholeCycleAnalysisResult analysis,
        long sourceMaxIngestId,
        int sourceEventCount)
        => new(
            analysis,
            new CycleAnalysisMaterialization
            {
                Status = "query-time",
                AlgorithmVersion = WholeCycleAnalysisEngine.AlgorithmVersion,
                SourceMaxIngestId = sourceMaxIngestId,
                SourceEventCount = sourceEventCount
            });
}

public sealed record MaterializedCycleAnalysis(
    WholeCycleAnalysisResult Analysis,
    CycleAnalysisMaterialization Materialization);
