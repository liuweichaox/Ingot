using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionAnalysisMaterializer(
    IProcessExecutionAnalysisMaterializationStore store,
    ProcessExecutionAnalysisEngine engine,
    ILogger<ProcessExecutionAnalysisMaterializer> logger,
    PostgresProcessExecutionScientificComputeEngine? databaseEngine = null)
{
    private readonly SemaphoreSlim[] _executionLocks = Enumerable.Range(0, 64)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task<MaterializedProcessExecutionAnalysis> GetOrComputeAsync(
        string executionId,
        IReadOnlyList<PlatformProductionEvent> rows,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        ProcessDataModel? dataModel,
        ProcessAnalysisPlan? plan,
        CancellationToken ct = default)
    {
        var source = CreateSourceFingerprint(rows);
        if (!completedAt.HasValue)
        {
            return QueryTime(
                engine.Analyze(rows, startedAt, completedAt, dataModel, plan),
                source);
        }

        var key = new ProcessExecutionAnalysisMaterializationKey(
            executionId,
            ProcessExecutionAnalysisEngine.AlgorithmVersion,
            dataModel?.ModelId ?? string.Empty,
            dataModel?.Version ?? 0,
            plan?.PlanId ?? string.Empty,
            plan?.Version ?? 0);
        var gate = _executionLocks[
            (StringComparer.Ordinal.GetHashCode(executionId) & int.MaxValue) % _executionLocks.Length];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                var cached = await store.TryLoadAsync(key, source, ct)
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
                        executionId,
                        startedAt.Value,
                        completedAt.Value,
                        analysis,
                        ct).ConfigureAwait(false);
                }
                var saved = await store.SaveAsync(
                    key, source, analysis, ct).ConfigureAwait(false);
                var verified = await store.TryLoadAsync(key, source, ct)
                    .ConfigureAwait(false);
                if (verified is null)
                {
                    logger.LogInformation(
                        "过程执行 {ExecutionId} 在物化期间收到更新，当前请求使用本次确定性计算结果并等待下次重算",
                        executionId);
                    return QueryTime(analysis, source);
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
                    "过程执行 {ExecutionId} 的分析物化不可用，降级为查询时确定性计算",
                    executionId);
                return QueryTime(
                    engine.Analyze(rows, startedAt, completedAt, dataModel, plan),
                    source);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static MaterializedProcessExecutionAnalysis FromSnapshot(ProcessExecutionAnalysisSnapshot snapshot, string status)
        => new(
            snapshot.Analysis,
            new ProcessExecutionAnalysisMaterialization
            {
                Status = status,
                AlgorithmVersion = ProcessExecutionAnalysisEngine.AlgorithmVersion,
                ComputedAt = snapshot.ComputedAt,
                SourceMinIngestId = snapshot.Source.MinIngestId,
                SourceMaxIngestId = snapshot.Source.MaxIngestId,
                SourceEventCount = snapshot.Source.EventCount,
                SourceContentHash = snapshot.Source.ContentHash
            });

    private static MaterializedProcessExecutionAnalysis QueryTime(
        WholeProcessExecutionAnalysisResult analysis,
        ProcessExecutionAnalysisSourceFingerprint source)
        => new(
            analysis,
            new ProcessExecutionAnalysisMaterialization
            {
                Status = "query-time",
                AlgorithmVersion = ProcessExecutionAnalysisEngine.AlgorithmVersion,
                SourceMinIngestId = source.MinIngestId,
                SourceMaxIngestId = source.MaxIngestId,
                SourceEventCount = source.EventCount,
                SourceContentHash = source.ContentHash
            });

    public static ProcessExecutionAnalysisSourceFingerprint CreateSourceFingerprint(IReadOnlyList<PlatformProductionEvent> rows)
    {
        if (rows.Count == 0)
            return new ProcessExecutionAnalysisSourceFingerprint(0, 0, 0, Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant());

        var ordered = rows
            .OrderBy(static row => row.IngestId)
            .ThenBy(static row => row.Event.EventId)
            .ToArray();
        var canonical = JsonSerializer.Serialize(ordered, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new ProcessExecutionAnalysisSourceFingerprint(
            ordered[0].IngestId,
            ordered[^1].IngestId,
            ordered.Length,
            hash);
    }
}

public sealed record MaterializedProcessExecutionAnalysis(
    WholeProcessExecutionAnalysisResult Analysis,
    ProcessExecutionAnalysisMaterialization Materialization);
