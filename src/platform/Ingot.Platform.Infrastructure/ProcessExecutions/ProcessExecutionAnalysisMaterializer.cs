
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.TimeSeries;
using Ingot.Platform.Infrastructure.TimeSeries;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ProcessExecutionAnalysisMaterializer(
    IProcessExecutionAnalysisMaterializationStore store,
    ProcessExecutionAnalysisEngine engine,
    ILogger<ProcessExecutionAnalysisMaterializer> logger,
    PostgresProcessExecutionScientificComputeEngine? databaseEngine = null,
    IExecutionAnalysisLockProvider? distributedLocks = null)
{
    private readonly SemaphoreSlim[] _executionLocks = Enumerable.Range(0, 64)
        .Select(static _ => new SemaphoreSlim(1, 1))
        .ToArray();

    public async Task<MaterializedProcessExecutionAnalysis?> TryLoadLatestAsync(
        string executionId,
        ProcessDataModel? dataModel,
        ProcessAnalysisPlan? plan,
        CancellationToken ct = default)
    {
        var key = CreateKey(executionId, dataModel, plan);
        var snapshot = await store.TryLoadLatestAsync(key, ct).ConfigureAwait(false);
        return snapshot is null ? null : FromSnapshot(snapshot, "cached");
    }

    public async Task<IReadOnlyDictionary<string, MaterializedProcessExecutionAnalysis>> LoadLatestReadyForSiteAsync(
        IReadOnlyList<(string ExecutionId, ProcessDataModel? DataModel, ProcessAnalysisPlan? Plan)> executions,
        string siteId,
        CancellationToken ct = default)
    {
        var snapshots = await store.LoadLatestReadyForSiteAsync(
            executions.Select(static item => CreateKey(item.ExecutionId, item.DataModel, item.Plan)).ToArray(),
            siteId,
            ct).ConfigureAwait(false);
        return snapshots.ToDictionary(
            static pair => pair.Key,
            static pair => FromSnapshot(pair.Value, "cached"),
            StringComparer.Ordinal);
    }

    public async Task<MaterializedProcessExecutionAnalysis> GetOrComputeAsync(
        string executionId,
        IReadOnlyList<ProcessSampleFrame> samples,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        ProcessDataModel? dataModel,
        ProcessAnalysisPlan? plan,
        CancellationToken ct = default)
    {
        var source = CreateSourceFingerprint(samples);
        if (!completedAt.HasValue)
        {
            return QueryTime(
                engine.Analyze(samples, startedAt, completedAt, dataModel, plan),
                source);
        }

        var key = CreateKey(executionId, dataModel, plan);
        var gate = _executionLocks[
            (StringComparer.Ordinal.GetHashCode(executionId) & int.MaxValue) % _executionLocks.Length];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                await using var distributedLease = distributedLocks is null
                    ? null
                    : await distributedLocks.AcquireAsync(key, ct).ConfigureAwait(false);
                var cached = await store.TryLoadAsync(key, source, ct)
                    .ConfigureAwait(false);
                if (cached is not null)
                    return FromSnapshot(cached, "cached");

                var analysis = engine.Analyze(samples, startedAt, completedAt, dataModel, plan);
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
                    engine.Analyze(samples, startedAt, completedAt, dataModel, plan),
                    source);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MaterializedProcessExecutionAnalysis?> TryMaterializeForUniqueSiteAsync(
        string executionId,
        string siteId,
        IReadOnlyList<ProcessSampleFrame> samples,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        ProcessDataModel? dataModel,
        ProcessAnalysisPlan? plan,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            throw new ArgumentException("站点标识不能为空。", nameof(siteId));
        var source = CreateSourceFingerprint(samples);
        if (!completedAt.HasValue)
        {
            return QueryTime(
                engine.Analyze(samples, startedAt, completedAt, dataModel, plan),
                source);
        }

        var key = CreateKey(executionId, dataModel, plan);
        var gate = _executionLocks[
            (StringComparer.Ordinal.GetHashCode(executionId) & int.MaxValue) % _executionLocks.Length];
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            try
            {
                await using var distributedLease = distributedLocks is null
                    ? null
                    : await distributedLocks.AcquireAsync(key, ct).ConfigureAwait(false);
                // The database compute engine is execution-id scoped. Use the already site-scoped
                // frames here, then let the store re-check site uniqueness under the ingest lock.
                var analysis = engine.Analyze(samples, startedAt, completedAt, dataModel, plan);
                var saved = await store.TrySaveForUniqueSiteAsync(
                    key, siteId.Trim(), source, analysis, ct).ConfigureAwait(false);
                if (saved is null)
                {
                    logger.LogError(
                        "过程执行 {ExecutionId} 出现在多个站点或站点归属不稳定；拒绝写入全局物化键",
                        executionId);
                    return null;
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
                    "过程执行 {ExecutionId} 的站点安全物化失败，将由后台队列重试",
                    executionId);
                return QueryTime(
                    engine.Analyze(samples, startedAt, completedAt, dataModel, plan),
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

    private static ProcessExecutionAnalysisMaterializationKey CreateKey(
        string executionId,
        ProcessDataModel? dataModel,
        ProcessAnalysisPlan? plan)
        => new(
            executionId,
            ProcessExecutionAnalysisEngine.AlgorithmVersion,
            dataModel?.ModelId ?? string.Empty,
            dataModel?.Version ?? 0,
            plan?.PlanId ?? string.Empty,
            plan?.Version ?? 0);

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

    public static ProcessExecutionAnalysisSourceFingerprint CreateSourceFingerprint(IReadOnlyList<ProcessSampleFrame> rows)
    {
        if (rows.Count == 0)
            return new ProcessExecutionAnalysisSourceFingerprint(0, 0, 0, Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant());

        var ordered = rows
            .OrderBy(static row => row.IngestId)
            .ThenBy(static row => row.EventId)
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
    ProcessExecutionAnalysisMaterialization Materialization)
{
    public IReadOnlyList<ProcessSampleConfigurationEvidence> SampleConfigurationEvidence { get; init; } = [];
}

// Groups persisted frame evidence without inferring missing values or expected sampling coverage.
internal static class ProcessSampleConfigurationEvidenceProjector
{
    private const int MaximumDetailedConfigurationGroups = 20;

    // QueryAllAsync returns unique frames; keep twenty groups detailed and merge all later groups exactly.
    public static IReadOnlyList<ProcessSampleConfigurationEvidence> Project(
        IReadOnlyList<ProcessSampleFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var detailedGroups = new List<ConfigurationEvidenceAccumulator>(MaximumDetailedConfigurationGroups);
        var summarizedConfigurationGroups = new HashSet<(AppliedConfigurationRef? Configuration, string? EdgeId)>();
        ConfigurationEvidenceAccumulator? summarized = null;
        foreach (var frame in frames)
        {
            ConfigurationEvidenceAccumulator? detailed = null;
            for (var index = 0; index < detailedGroups.Count; index++)
            {
                if (detailedGroups[index].AppliedConfiguration == frame.AppliedConfiguration &&
                    string.Equals(detailedGroups[index].SourceEdgeId, frame.EdgeId, StringComparison.OrdinalIgnoreCase))
                {
                    detailed = detailedGroups[index];
                    break;
                }
            }

            if (detailed is not null)
            {
                detailed.Add(frame);
                continue;
            }

            if (detailedGroups.Count < MaximumDetailedConfigurationGroups)
            {
                detailed = new ConfigurationEvidenceAccumulator(frame.AppliedConfiguration, frame.EdgeId);
                detailedGroups.Add(detailed);
                detailed.Add(frame);
                continue;
            }

            summarized ??= new ConfigurationEvidenceAccumulator(null, null);
            if (summarizedConfigurationGroups.Add((frame.AppliedConfiguration, frame.EdgeId)))
                summarized.SummarizedConfigurationGroupCount++;
            summarized.Add(frame);
        }

        var evidence = detailedGroups.Select(static group => group.ToEvidence()).ToList();
        if (summarized is not null)
            evidence.Add(summarized.ToEvidence());
        return evidence;
    }

    private sealed class ConfigurationEvidenceAccumulator(
        AppliedConfigurationRef? appliedConfiguration,
        string? sourceEdgeId)
    {
        public AppliedConfigurationRef? AppliedConfiguration { get; } = appliedConfiguration;

        public string? SourceEdgeId { get; } = sourceEdgeId;

        public int SampleFrameCount { get; private set; }

        public DateTimeOffset? FirstOccurredAt { get; private set; }

        public DateTimeOffset? LastOccurredAt { get; private set; }

        public int ObservedValueCount { get; private set; }

        public int GoodValueCount { get; private set; }

        public int UncertainValueCount { get; private set; }

        public int BadValueCount { get; private set; }

        public int UnclassifiedQualityValueCount { get; private set; }

        private HashSet<string> ObservedDataItemCodes { get; } = new(StringComparer.Ordinal);

        public int SummarizedConfigurationGroupCount { get; set; }

        public void Add(ProcessSampleFrame frame)
        {
            SampleFrameCount++;
            foreach (var code in frame.NumericValues.Keys.Concat(frame.QualityCodes.Keys))
            {
                if (!string.IsNullOrWhiteSpace(code))
                    ObservedDataItemCodes.Add(code);
            }
            FirstOccurredAt = !FirstOccurredAt.HasValue || frame.OccurredAt < FirstOccurredAt.Value
                ? frame.OccurredAt
                : FirstOccurredAt;
            LastOccurredAt = !LastOccurredAt.HasValue || frame.OccurredAt > LastOccurredAt.Value
                ? frame.OccurredAt
                : LastOccurredAt;
            foreach (var qualityCode in frame.QualityCodes.Values)
            {
                ObservedValueCount++;
                switch (qualityCode)
                {
                    case SignalQualityCodes.Good:
                        GoodValueCount++;
                        break;
                    case SignalQualityCodes.Uncertain:
                        UncertainValueCount++;
                        break;
                    case SignalQualityCodes.Bad:
                        BadValueCount++;
                        break;
                    default:
                        UnclassifiedQualityValueCount++;
                        break;
                }
            }
        }

        public ProcessSampleConfigurationEvidence ToEvidence()
            => new()
            {
                AppliedConfigurationKind = AppliedConfiguration?.Kind,
                AppliedConfigurationId = AppliedConfiguration?.Id,
                AppliedConfigurationVersion = AppliedConfiguration?.Version,
                SourceEdgeId = SourceEdgeId,
                SampleFrameCount = SampleFrameCount,
                FirstOccurredAt = FirstOccurredAt,
                LastOccurredAt = LastOccurredAt,
                ObservedValueCount = ObservedValueCount,
                GoodValueCount = GoodValueCount,
                UncertainValueCount = UncertainValueCount,
                BadValueCount = BadValueCount,
                UnclassifiedQualityValueCount = UnclassifiedQualityValueCount,
                ObservedDataItemCodes = ObservedDataItemCodes.Order(StringComparer.Ordinal).ToArray(),
                SummarizedConfigurationGroupCount = SummarizedConfigurationGroupCount
            };
    }
}
