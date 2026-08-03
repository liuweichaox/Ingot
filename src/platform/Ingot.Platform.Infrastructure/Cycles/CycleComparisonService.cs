using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using System.Globalization;
using System.Text.Json;

namespace Ingot.Platform.Infrastructure.Cycles;

public sealed class CycleComparisonService(
    IPlatformEventStore events,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    ProcessAnalysisResolver analysisResolver,
    WholeCycleAnalysisEngine? wholeCycleAnalysis = null,
    CycleAnalysisMaterializer? materializer = null,
    IProcessOptimizerClient? optimizerClient = null) : ICycleComparisonService
{
    private readonly WholeCycleAnalysisEngine _wholeCycleAnalysis = wholeCycleAnalysis ?? new();
    private readonly CycleAnalysisMaterializer? _materializer = materializer;
    private readonly CycleDiagnosisEngine _diagnosisEngine = new();

    public async Task<CycleComparisonRow?> GetCycleAsync(
        string correlationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("周期标识不能为空。", nameof(correlationId));
        correlationId = correlationId.Trim();
        var rows = await GetCyclesAsync([correlationId], ct).ConfigureAwait(false);
        return rows.GetValueOrDefault(correlationId);
    }

    public async Task<IReadOnlyDictionary<string, CycleComparisonRow>> GetCyclesAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(correlationIds);
        var ids = correlationIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return new Dictionary<string, CycleComparisonRow>(StringComparer.Ordinal);

        var cycleEvents = await LoadCyclesAsync(ids, ct).ConfigureAwait(false);
        var allInspections = InspectionRecordSet.Effective(
            await inspections.QueryAllByOperationRunIdsAsync(ids, ct).ConfigureAwait(false));
        var inspectionsByCycle = allInspections
            .GroupBy(static item => item.OperationRunId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            allInspections.Select(static value => value.RecordId).ToArray(), ct).ConfigureAwait(false);
        var contexts = ids
            .Select(id => ResolveContext(cycleEvents.GetValueOrDefault(id, [])))
            .ToArray();
        var analyses = await analysisResolver.ResolveManyAsync(contexts, "production-cycle", ct)
            .ConfigureAwait(false);
        var result = new Dictionary<string, CycleComparisonRow>(StringComparer.Ordinal);
        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index];
            if (!cycleEvents.TryGetValue(id, out var rows) || rows.Count == 0)
                continue;
            var analysis = analyses[index];
            var materialized = await AnalyzeAsync(id, rows, analysis, ct).ConfigureAwait(false);
            result[id] = BuildRow(
                id,
                rows,
                inspectionsByCycle.GetValueOrDefault(id, []),
                latestReviews,
                analysis,
                materialized);
        }
        return result;
    }

    public async Task<CycleComparisonResult?> CompareWithHistoryAsync(
        string correlationId,
        int limit,
        CancellationToken ct = default)
    {
        var baselineEvents = await QueryAllAsync(
            new PlatformEventQuery { CorrelationId = correlationId }, ct).ConfigureAwait(false);
        if (baselineEvents.Count == 0)
            return null;

        var baselineContext = ResolveContext(baselineEvents);
        var analysis = await analysisResolver.ResolveAsync(baselineContext, "production-cycle", ct)
            .ConfigureAwait(false);
        EnsureComparisonKeysPresent(analysis?.Plan, baselineContext, "基准周期");
        var comparisonContext = BuildComparisonContext(analysis?.Plan, baselineContext);
        var completed = await QueryAllAsync(
            new PlatformEventQuery { EventType = "cycle.completed", Context = comparisonContext }, ct)
            .ConfigureAwait(false);
        var candidateIds = completed
            .Where(item => !string.IsNullOrWhiteSpace(item.Event.CorrelationId) &&
                           !string.Equals(item.Event.CorrelationId, correlationId, StringComparison.Ordinal))
            .GroupBy(static item => item.Event.CorrelationId!, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static item => item.Event.OccurredAt).First())
            .OrderByDescending(static item => item.Event.OccurredAt)
            .Take(limit)
            .Select(static item => item.Event.CorrelationId!)
            .ToArray();
        var allIds = new[] { correlationId }.Concat(candidateIds).ToArray();
        var cycleEvents = await LoadCyclesAsync(allIds, ct).ConfigureAwait(false);
        return await BuildComparisonAsync(correlationId, allIds, cycleEvents, analysis, ct).ConfigureAwait(false);
    }

    public async Task<CycleComparisonResult?> CompareSelectedAsync(
        string baselineCycleId,
        IReadOnlyList<string> cycleIds,
        CancellationToken ct = default)
    {
        var allIds = new[] { baselineCycleId }
            .Concat(cycleIds.Where(id => !string.Equals(id, baselineCycleId, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (allIds.Length < 2)
            throw new ArgumentException("请选择至少两个不同的生产周期。", nameof(cycleIds));

        var cycleEvents = await LoadCyclesAsync(allIds, ct).ConfigureAwait(false);
        if (allIds.Any(id => !cycleEvents.TryGetValue(id, out var rows) || rows.Count == 0))
            return null;

        var baselineContext = ResolveContext(cycleEvents[baselineCycleId]);
        var analysis = await analysisResolver.ResolveAsync(baselineContext, "production-cycle", ct)
            .ConfigureAwait(false);
        var comparisonKeys = ResolveComparisonKeys(analysis?.Plan);
        EnsureComparisonKeysPresent(analysis?.Plan, baselineContext, "基准周期");
        var incompatible = allIds.Skip(1).FirstOrDefault(id =>
        {
            var candidateContext = ResolveContext(cycleEvents[id]);
            EnsureComparisonKeysPresent(analysis?.Plan, candidateContext, $"周期 {id}");
            return !ContextsMatch(baselineContext, candidateContext, comparisonKeys);
        });
        if (incompatible is not null)
        {
            throw new ArgumentException(
                $"周期 {incompatible} 与基准周期的同类比较键不一致：{string.Join("、", comparisonKeys)}。",
                nameof(cycleIds));
        }

        return await BuildComparisonAsync(baselineCycleId, allIds, cycleEvents, analysis, ct)
            .ConfigureAwait(false);
    }

    private async Task<CycleComparisonResult> BuildComparisonAsync(
        string baselineCycleId,
        IReadOnlyList<string> allIds,
        IReadOnlyDictionary<string, IReadOnlyList<PlatformProductionEvent>> cycleEvents,
        ResolvedProcessAnalysis? analysis,
        CancellationToken ct)
    {
        var allInspections = InspectionRecordSet.Effective(
            await inspections.QueryAllByOperationRunIdsAsync(allIds, ct).ConfigureAwait(false));
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            allInspections.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var inspectionsByCycle = allInspections.GroupBy(static record => record.OperationRunId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var materializedByCycle = new Dictionary<string, MaterializedCycleAnalysis>(StringComparer.Ordinal);
        foreach (var id in allIds)
        {
            materializedByCycle[id] = await AnalyzeAsync(id, cycleEvents[id], analysis, ct).ConfigureAwait(false);
        }
        var rows = allIds.Select(id => BuildRow(
                id,
                cycleEvents[id],
                inspectionsByCycle.GetValueOrDefault(id, []),
                latestReviews,
                analysis,
                materializedByCycle[id]))
            .ToArray();
        var acceptance = new CycleComparisonAcceptance
        {
            CycleCount = rows.Length,
            CompleteCycleCount = rows.Count(static row => row.LifecycleComplete),
            QualityLinkedCycleCount = rows.Count(static row => row.InspectionOutcomes.Count > 0),
            VisualReviewCompletedCycleCount = rows.Count(static row => !string.IsNullOrWhiteSpace(row.VisualReviewDecision)),
            AvailableCycleCount = rows.Count(static row =>
                row.ProcessDataQuality.Status == ProcessDataStatuses.Available),
            DegradedCycleCount = rows.Count(static row =>
                row.ProcessDataQuality.Status == ProcessDataStatuses.Degraded),
            UnavailableCycleCount = rows.Count(static row =>
                row.ProcessDataQuality.Status == ProcessDataStatuses.Unavailable),
            EffectiveCycleWeight = rows.Skip(1).Sum(static row => row.EvidenceWeight)
        };
        var effectiveWeight = acceptance.EffectiveCycleWeight;
        var diagnosis = await EnrichDiagnosisAsync(
            rows,
            _diagnosisEngine.Analyze(rows),
            optimizerClient,
            ct).ConfigureAwait(false);
        return new CycleComparisonResult
        {
            BaselineCycleId = baselineCycleId,
            ProductSeries = rows[0].ProductSeries,
            AnalysisPlanId = analysis?.Plan.PlanId,
            AnalysisPlanVersion = analysis?.Plan.Version,
            DataModelId = analysis?.DataModel.ModelId,
            DataModelVersion = analysis?.DataModel.Version,
            AnalysisScope = analysis?.Plan.AnalysisScope ?? "production-cycle",
            AlignmentMode = analysis?.Plan.AlignmentMode,
            FeatureAlgorithmVersion = WholeCycleAnalysisEngine.AlgorithmVersion,
            EvidenceLevel = rows[0].ProcessDataQuality.Status == ProcessDataStatuses.Unavailable ||
                            effectiveWeight < 5
                ? "insufficient"
                : effectiveWeight < 20 ? "exploratory" : "stable",
            Baseline = rows[0],
            HistoricalCycles = rows.Skip(1).ToArray(),
            SignalComparisons = BuildSignalComparisons(rows[0], rows.Skip(1).ToArray()),
            QualityAssociations = BuildQualityAssociations(rows),
            Diagnosis = diagnosis,
            Acceptance = acceptance
        };
    }

    private static async Task<CycleDiagnosisSummary> EnrichDiagnosisAsync(
        IReadOnlyList<CycleComparisonRow> rows,
        CycleDiagnosisSummary robust,
        IProcessOptimizerClient? optimizerClient,
        CancellationToken ct)
    {
        if (optimizerClient is null || robust.Candidates.Count == 0)
            return robust;
        var observations = rows
            .Where(static row =>
                row.EvidenceWeight > 0 &&
                (row.InspectionOutcomes.Contains("FAIL", StringComparer.Ordinal) ||
                 row.InspectionOutcomes.Contains("PASS", StringComparer.Ordinal)))
            .Select(row => new ProcessDiagnosticObservationInput
            {
                RunKey = row.CorrelationId,
                Outcome = row.InspectionOutcomes.Contains("FAIL", StringComparer.Ordinal) ? 1 : 0,
                Weight = Math.Clamp(row.EvidenceWeight, 0.01, 1),
                OccurredAt = row.StartedAt.ToUnixTimeSeconds(),
                Context = BuildDiagnosticContext(row),
                Values = robust.Candidates
                    .Select(candidate => (candidate.DataSource, Value: ReadCandidateValue(row, candidate)))
                    .Where(static pair => pair.Value.HasValue)
                    .ToDictionary(
                        static pair => pair.DataSource,
                        static pair => pair.Value!.Value,
                        StringComparer.Ordinal)
            })
            .ToArray();
        if (observations.Length < 4)
            return robust;
        try
        {
            var advanced = await optimizerClient.DiagnoseAsync(
                new ProcessDiagnosisCall
                {
                    Features = robust.Candidates.Select(candidate =>
                        new ProcessDiagnosticFeatureInput
                        {
                            DataSource = candidate.DataSource,
                            SourceKind = candidate.SourceKind,
                            Actionability = candidate.Actionability
                        }).ToArray(),
                    Observations = observations,
                    Seed = 17
                },
                ct).ConfigureAwait(false);
            var adjusted = advanced.Candidates.ToDictionary(
                static candidate => candidate.DataSource,
                StringComparer.Ordinal);
            var candidates = robust.Candidates.Select(candidate =>
                adjusted.TryGetValue(candidate.DataSource, out var model)
                    ? candidate with
                    {
                        AdjustedEffect = model.AdjustedEffect,
                        ModelImportance = model.ModelImportance,
                        StabilitySelectionRate = model.StabilitySelectionRate,
                        SignStability = model.SignStability,
                        CandidateScore = 0.4 * candidate.CandidateScore + 0.6 * model.RankScore,
                        EvidenceLevel = advanced.CrossValidationScore <= 0
                            ? "exploratory"
                            : model.StabilitySelectionRate >= 0.6
                                ? "stable"
                                : model.StabilitySelectionRate >= 0.25
                                    ? "exploratory"
                                    : "screening"
                    }
                    : advanced.CrossValidationScore > 0 &&
                      advanced.ModelFamily != "robust-screening-only"
                        ? candidate with { EvidenceLevel = "screening" }
                        : candidate)
                .OrderByDescending(static candidate => candidate.CandidateScore)
                .ThenBy(static candidate => candidate.CandidateId, StringComparer.Ordinal)
                .ToArray();
            return robust with
            {
                AlgorithmVersion = $"{CycleDiagnosisEngine.AlgorithmVersion}+{advanced.AlgorithmVersion}",
                ModelFamily = advanced.ModelFamily,
                AdjustmentMethod = advanced.AdjustmentMethod,
                CrossValidationScore = advanced.CrossValidationScore,
                FoldCount = advanced.FoldCount,
                StabilityRuns = advanced.StabilityRuns,
                ContextVariables = advanced.ContextVariables,
                Candidates = candidates,
                Interactions = advanced.Interactions.Select(static interaction =>
                    new CycleCauseInteraction
                    {
                        LeftDataSource = interaction.LeftDataSource,
                        RightDataSource = interaction.RightDataSource,
                        AdjustedEffect = interaction.AdjustedEffect,
                        StabilitySelectionRate = interaction.StabilitySelectionRate,
                        RankScore = interaction.RankScore
                    }).ToArray(),
                Limitations = robust.Limitations.Concat(advanced.Limitations)
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
        }
        catch (Exception exception) when (
            exception is ProcessOptimizerUnavailableException or
                ProcessResearchRuleException or
                NotSupportedException)
        {
            return robust with
            {
                Limitations = robust.Limitations
                    .Append("多变量数值分析当前不可用，本次仍保留稳健筛选结果。")
                    .Distinct(StringComparer.Ordinal).ToArray()
            };
        }
    }

    private static IReadOnlyDictionary<string, string> BuildDiagnosticContext(
        CycleComparisonRow row)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["product_series"] = row.ProductSeries,
            ["machine_id"] = row.MachineId
        };
        foreach (var key in new[]
                 {
                     "product_code", "material", "material_code", "material_lot",
                     "material_batch", "equipment_id", "mold_id", "tooling_id",
                     "batch_id", "lot_id", "recipe_id", "recipe_version"
                 })
        {
            var source = row.Context.FirstOrDefault(pair =>
                string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(source.Value))
                context[key] = source.Value;
        }
        Add("product_code", row.ProductCode);
        Add("recipe", row.RecipeId is null ? null : $"{row.RecipeId}@{row.RecipeVersion}");
        Add("mold_id", row.MoldId ?? row.ToolingId);
        Add("tooling_installation_id", row.ToolingInstallationId);
        return context;

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                context[key] = value;
        }
    }

    private static double? ReadCandidateValue(
        CycleComparisonRow row,
        CycleCauseCandidate candidate)
    {
        if (candidate.SourceKind == CycleCauseSourceKinds.RecipeParameter)
        {
            var parameter = row.RecipeParameters.FirstOrDefault(value =>
                string.Equals(value.Code, candidate.VariableCode, StringComparison.Ordinal));
            return parameter is not null && TryReadDouble(parameter.Value, out var value)
                ? value
                : null;
        }
        var signal = row.Signals.FirstOrDefault(value =>
            string.Equals(value.Code, candidate.SignalCode, StringComparison.Ordinal));
        return signal?.Features.FirstOrDefault(value =>
            string.Equals(value.Code, candidate.FeatureCode, StringComparison.Ordinal) &&
            string.Equals(value.PhaseCode, candidate.PhaseCode, StringComparison.Ordinal) &&
            value.PhaseOrder == candidate.PhaseOrder)?.Value;
    }

    private static IReadOnlyList<CycleQualityAssociation> BuildQualityAssociations(
        IReadOnlyList<CycleComparisonRow> rows)
    {
        var passRows = rows.Where(static row =>
                row.EvidenceWeight > 0 &&
                row.InspectionOutcomes.Contains("PASS", StringComparer.Ordinal) &&
                !row.InspectionOutcomes.Contains("FAIL", StringComparer.Ordinal))
            .ToArray();
        var failRows = rows.Where(static row =>
                row.EvidenceWeight > 0 &&
                row.InspectionOutcomes.Contains("FAIL", StringComparer.Ordinal))
            .ToArray();
        if (passRows.Length == 0 || failRows.Length == 0)
            return [];

        var confounders = FindPossibleConfounders(passRows, failRows);
        var keys = rows.SelectMany(row => row.Signals.SelectMany(signal =>
                signal.Features.Where(static feature => feature.Value.HasValue)
                    .Select(feature => new FeatureKey(
                        signal.Code,
                        feature.Code,
                        feature.PhaseCode,
                        feature.PhaseName,
                        feature.PhaseOrder))))
            .Distinct()
            .ToArray();
        return keys.Select(key =>
            {
                var pass = FeatureValues(passRows, key);
                var fail = FeatureValues(failRows, key);
                var passMedian = WeightedPercentile(pass, 0.5);
                var failMedian = WeightedPercentile(fail, 0.5);
                var combined = pass.Concat(fail).ToArray();
                var combinedMedian = WeightedPercentile(combined, 0.5);
                var mad = combinedMedian.HasValue
                    ? WeightedPercentile(
                        combined.Select(item =>
                            new WeightedValue(Math.Abs(item.Value - combinedMedian.Value), item.Weight)).ToArray(),
                        0.5)
                    : null;
                var robustEffect = passMedian.HasValue && failMedian.HasValue && mad is > 0
                    ? (double?)((failMedian.Value - passMedian.Value) / (1.4826d * mad.Value))
                    : (double?)null;
                var relativeDifference = passMedian.HasValue && failMedian.HasValue
                    ? (double?)((failMedian.Value - passMedian.Value) /
                      Math.Max(Math.Max(Math.Abs(passMedian.Value), Math.Abs(failMedian.Value)), 1e-9d))
                    : (double?)null;
                var passWeight = pass.Sum(static item => item.Weight);
                var failWeight = fail.Sum(static item => item.Weight);
                var support = Math.Min(1d, Math.Min(passWeight, failWeight) / 5d);
                return new CycleQualityAssociation
                {
                    SignalCode = key.SignalCode,
                    FeatureCode = key.FeatureCode,
                    PhaseCode = key.PhaseCode,
                    PhaseName = key.PhaseName,
                    PhaseOrder = key.PhaseOrder,
                    PassCycleCount = pass.Length,
                    FailCycleCount = fail.Length,
                    PassEffectiveWeight = passWeight,
                    FailEffectiveWeight = failWeight,
                    PassMedian = passMedian,
                    FailMedian = failMedian,
                    MedianDifference = passMedian.HasValue && failMedian.HasValue
                        ? failMedian.Value - passMedian.Value
                        : null,
                    RobustEffect = robustEffect,
                    CandidateScore = Math.Abs(robustEffect ?? relativeDifference ?? 0) * support,
                    EvidenceLevel = passWeight >= 5 && failWeight >= 5
                        ? "stable"
                        : passWeight >= 2 && failWeight >= 2 ? "exploratory" : "insufficient",
                    PossibleConfounders = confounders
                };
            })
            .Where(static item => item.PassCycleCount > 0 && item.FailCycleCount > 0)
            .OrderByDescending(static item => item.CandidateScore)
            .ThenBy(static item => item.SignalCode, StringComparer.Ordinal)
            .ThenBy(static item => item.PhaseOrder)
            .ThenBy(static item => item.FeatureCode, StringComparer.Ordinal)
            .Take(100)
            .ToArray();
    }

    private static WeightedValue[] FeatureValues(
        IReadOnlyList<CycleComparisonRow> rows,
        FeatureKey key)
        => rows.Select(row =>
            {
                var feature = row.Signals.FirstOrDefault(signal => signal.Code == key.SignalCode)?
                    .Features.FirstOrDefault(item =>
                        item.Code == key.FeatureCode &&
                        item.PhaseCode == key.PhaseCode &&
                        item.PhaseOrder == key.PhaseOrder);
                return feature?.Value is { } value
                    ? new WeightedValue(value, row.EvidenceWeight)
                    : null;
            })
            .Where(static item => item is not null)
            .Cast<WeightedValue>()
            .ToArray();

    private static IReadOnlyList<string> FindPossibleConfounders(
        IReadOnlyList<CycleComparisonRow> passRows,
        IReadOnlyList<CycleComparisonRow> failRows)
    {
        var result = new List<string>();
        AddIfDifferent("product_code", static row => row.ProductCode);
        AddIfDifferent("machine_id", static row => row.MachineId);
        AddIfDifferent("recipe", static row => $"{row.RecipeId}@{row.RecipeVersion}");
        AddIfDifferent("mold_id", static row => row.MoldId ?? row.ToolingId);
        return result;

        void AddIfDifferent(string name, Func<CycleComparisonRow, string?> selector)
        {
            var pass = passRows.Select(selector).Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>().ToHashSet(StringComparer.Ordinal);
            var fail = failRows.Select(selector).Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>().ToHashSet(StringComparer.Ordinal);
            if (pass.Count > 0 && fail.Count > 0 && !pass.SetEquals(fail))
                result.Add(name);
        }
    }

    private static IReadOnlyList<CycleSignalComparison> BuildSignalComparisons(
        CycleComparisonRow baseline,
        IReadOnlyList<CycleComparisonRow> historical)
    {
        var eligible = historical.Where(static row => row.EvidenceWeight > 0).ToArray();
        return baseline.Signals.SelectMany(signal => signal.Features.Select(feature =>
        {
            var weighted = eligible.Select(row =>
            {
                var value = row.Signals.FirstOrDefault(item => item.Code == signal.Code)?
                    .Features.FirstOrDefault(item =>
                        item.Code == feature.Code &&
                        item.PhaseCode == feature.PhaseCode &&
                        item.PhaseOrder == feature.PhaseOrder)?.Value;
                return value.HasValue ? new WeightedValue(value.Value, row.EvidenceWeight) : null;
            }).Where(static item => item is not null).Cast<WeightedValue>().ToArray();
            var median = WeightedPercentile(weighted, 0.5);
            var deviations = median.HasValue
                ? weighted.Select(item => new WeightedValue(Math.Abs(item.Value - median.Value), item.Weight)).ToArray()
                : [];
            var mad = WeightedPercentile(deviations, 0.5);
            double? baselinePercentile = feature.Value.HasValue && weighted.Length > 0
                ? weighted.Where(item => item.Value <= feature.Value.Value).Sum(static item => item.Weight) /
                  weighted.Sum(static item => item.Weight)
                : null;
            return new CycleSignalComparison
            {
                SignalCode = signal.Code,
                FeatureCode = feature.Code,
                PhaseCode = feature.PhaseCode,
                PhaseName = feature.PhaseName,
                PhaseOrder = feature.PhaseOrder,
                BaselineValue = feature.Value,
                HistoricalMedian = median,
                HistoricalP10 = WeightedPercentile(weighted, 0.1),
                HistoricalP90 = WeightedPercentile(weighted, 0.9),
                BaselinePercentile = baselinePercentile,
                RobustDeviation = feature.Value.HasValue && median.HasValue && mad is > 0
                    ? (feature.Value.Value - median.Value) / (1.4826d * mad.Value)
                    : null,
                EffectiveWeight = weighted.Sum(static item => item.Weight)
            };
        })).ToArray();
    }

    private static double? WeightedPercentile(IReadOnlyList<WeightedValue> values, double percentile)
    {
        if (values.Count == 0)
            return null;
        var ordered = values.OrderBy(static item => item.Value).ToArray();
        var target = ordered.Sum(static item => item.Weight) * percentile;
        var cumulative = 0d;
        foreach (var item in ordered)
        {
            cumulative += item.Weight;
            if (cumulative >= target)
                return item.Value;
        }
        return ordered[^1].Value;
    }

    private CycleComparisonRow BuildRow(
        string correlationId,
        IReadOnlyList<PlatformProductionEvent> rows,
        IReadOnlyList<InspectionRecord> inspectionRecords,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews,
        ResolvedProcessAnalysis? analysis,
        MaterializedCycleAnalysis materialized)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var first = ordered[0];
        var started = ordered.FirstOrDefault(static row => row.Event.EventType == "cycle.started");
        var completed = ordered.LastOrDefault(static row => row.Event.EventType == "cycle.completed");
        var samples = ordered.Where(static row => row.Event.EventType == "process.sample").ToArray();
        var wholeCycle = materialized.Analysis;
        var visualRecord = inspectionRecords.Where(static record => record.Attachments.Count > 0)
            .OrderByDescending(static record => record.MeasuredAt)
            .FirstOrDefault();
        var visualReview = visualRecord is null ? null : latestReviews.GetValueOrDefault(visualRecord.RecordId);
        var context = ResolveContext(ordered);
        var lifecycleComplete = started is not null && completed is not null;
        return new CycleComparisonRow
        {
            CorrelationId = correlationId,
            MachineId = first.Event.Subject.Id,
            Context = context,
            HasStarted = started is not null,
            HasCompleted = completed is not null,
            LifecycleComplete = lifecycleComplete,
            StartedAt = started?.Event.OccurredAt ?? first.Event.OccurredAt,
            CompletedAt = completed?.Event.OccurredAt,
            DurationMs = lifecycleComplete
                ? (completed!.Event.OccurredAt - started!.Event.OccurredAt).TotalMilliseconds
                : null,
            ProductSeries = ProcessAnalysisResolver.ContextValue(context, "product_series") ?? "unknown",
            ProductCode = ProcessAnalysisResolver.ContextValue(context, "product_code"),
            RecipeId = ProcessAnalysisResolver.ContextValue(context, "recipe_id"),
            RecipeVersion = ProcessAnalysisResolver.ContextValue(context, "recipe_version"),
            ToolingInstallationId = ProcessAnalysisResolver.ContextValue(context, "tooling_installation_id"),
            ToolingId = ProcessAnalysisResolver.ContextValue(context, "tooling_id") ??
                        ProcessAnalysisResolver.ContextValue(context, "mold_id"),
            MoldId = ProcessAnalysisResolver.ContextValue(context, "mold_id"),
            AssemblyRevisionId = ProcessAnalysisResolver.ContextValue(context, "assembly_revision_id"),
            AssemblyRevision = ProcessAnalysisResolver.ContextValue(context, "assembly_revision"),
            SampleCount = samples.Length,
            ExpectedSampleCount = 0,
            SampleCompleteness = wholeCycle.Quality.Status switch
            {
                ProcessDataStatuses.Available => 1d,
                ProcessDataStatuses.Degraded => 0.5d,
                _ => 0d
            },
            ProcessDataQuality = wholeCycle.Quality,
            EvidenceWeight = wholeCycle.Quality.Status switch
            {
                ProcessDataStatuses.Available => 1d,
                ProcessDataStatuses.Degraded => 0.5d,
                _ => 0d
            },
            PhaseCount = wholeCycle.Phases.Count(static phase => phase.Code != "unknown"),
            InspectionOutcomes = inspectionRecords.Select(static record => record.Outcome)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            VisualReviewDecision = visualReview?.Decision,
            Signals = wholeCycle.Signals,
            Phases = wholeCycle.Phases,
            AnalysisMaterialization = materialized.Materialization,
            RecipeParameters = BuildRecipeParameters(analysis?.DataModel, ordered)
        };
    }

    private async Task<MaterializedCycleAnalysis> AnalyzeAsync(
        string correlationId,
        IReadOnlyList<PlatformProductionEvent> rows,
        ResolvedProcessAnalysis? analysis,
        CancellationToken ct)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var startedAt = ordered.FirstOrDefault(static row => row.Event.EventType == "cycle.started")?.Event.OccurredAt;
        var completedAt = ordered.LastOrDefault(static row => row.Event.EventType == "cycle.completed")?.Event.OccurredAt;
        if (_materializer is not null)
        {
            return await _materializer.GetOrComputeAsync(
                correlationId,
                ordered,
                startedAt,
                completedAt,
                analysis?.DataModel,
                analysis?.Plan,
                ct).ConfigureAwait(false);
        }

        return new MaterializedCycleAnalysis(
            _wholeCycleAnalysis.Analyze(
                ordered,
                startedAt,
                completedAt,
                analysis?.DataModel,
                analysis?.Plan),
            new CycleAnalysisMaterialization
            {
                Status = "query-time",
                AlgorithmVersion = WholeCycleAnalysisEngine.AlgorithmVersion,
                SourceMaxIngestId = ordered.Length == 0 ? 0 : ordered.Max(static row => row.IngestId),
                SourceEventCount = ordered.Length
            });
    }

    private static IReadOnlyList<CycleRecipeParameter> BuildRecipeParameters(
        ProcessDataModel? model,
        IReadOnlyList<PlatformProductionEvent> rows)
    {
        var definitions = model?.RecipeParameters.ToDictionary(static item => item.Code, StringComparer.Ordinal)
                          ?? new Dictionary<string, RecipeParameterDefinition>(StringComparer.Ordinal);
        var applied = rows
            .Where(static row => row.Event.EventType == "recipe.applied")
            .OrderByDescending(static row => row.Event.OccurredAt)
            .ThenByDescending(static row => row.IngestId)
            .FirstOrDefault();
        if (applied is not null &&
            applied.Event.Data.TryGetValue("resolvedParameters", out var raw) &&
            TryReadObject(raw, out var actual))
        {
            var captured = actual
                .Select(pair =>
                {
                    definitions.TryGetValue(pair.Key, out var definition);
                    return TryReadDouble(pair.Value, out var value)
                        ? new CycleRecipeParameter
                        {
                            Code = pair.Key,
                            Name = definition?.SourceField,
                            Unit = definition?.Unit,
                            Value = JsonSerializer.SerializeToElement(value)
                        }
                        : null;
                })
                .Where(static value => value is not null)
                .Select(static value => value!)
                .OrderBy(static value => value.Code, StringComparer.Ordinal)
                .ToArray();
            if (captured.Length > 0)
                return captured;
        }
        return [];
    }

    private static bool TryReadObject(
        object? raw,
        out IReadOnlyDictionary<string, object?> values)
    {
        if (raw is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            values = element.EnumerateObject().ToDictionary(
                static property => property.Name,
                static property => (object?)property.Value,
                StringComparer.Ordinal);
            return true;
        }
        if (raw is IReadOnlyDictionary<string, object?> readOnly)
        {
            values = readOnly;
            return true;
        }
        if (raw is IDictionary<string, object?> dictionary)
        {
            values = dictionary.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.Ordinal);
            return true;
        }
        values = new Dictionary<string, object?>();
        return false;
    }

    private static bool TryReadDouble(object? raw, out double value)
    {
        if (raw is JsonElement element)
        {
            value = default;
            return element.ValueKind == JsonValueKind.Number &&
                   element.TryGetDouble(out value) &&
                   double.IsFinite(value);
        }
        try
        {
            value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return double.IsFinite(value);
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidCastException or OverflowException)
        {
            value = default;
            return false;
        }
    }

    private static IReadOnlyDictionary<string, string> BuildComparisonContext(
        ProcessAnalysisPlan? plan,
        IReadOnlyDictionary<string, string> baselineContext)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in ResolveComparisonKeys(plan))
        {
            var value = ProcessAnalysisResolver.ContextValue(baselineContext, key);
            if (!string.IsNullOrWhiteSpace(value))
                result[ToEventContextKey(key)] = value;
        }
        return result;
    }

    private static IReadOnlyList<string> ResolveComparisonKeys(ProcessAnalysisPlan? plan)
        => plan?.ComparisonKeys.Count > 0 ? plan.ComparisonKeys : ["product_series"];

    private static void EnsureComparisonKeysPresent(
        ProcessAnalysisPlan? plan,
        IReadOnlyDictionary<string, string> context,
        string source)
    {
        var missing = ResolveComparisonKeys(plan)
            .Where(key => string.IsNullOrWhiteSpace(ProcessAnalysisResolver.ContextValue(context, key)))
            .ToArray();
        if (missing.Length > 0)
            throw new ArgumentException($"{source}缺少同类比较上下文：{string.Join("、", missing)}。");
    }

    private static bool ContextsMatch(
        IReadOnlyDictionary<string, string> baseline,
        IReadOnlyDictionary<string, string> candidate,
        IReadOnlyList<string> keys)
        => keys.All(key => string.Equals(
            ProcessAnalysisResolver.ContextValue(baseline, key),
            ProcessAnalysisResolver.ContextValue(candidate, key),
            StringComparison.OrdinalIgnoreCase));

    private static string ToEventContextKey(string key) => key.Replace('.', '_');

    private static IReadOnlyDictionary<string, string> ResolveContext(
        IReadOnlyList<PlatformProductionEvent> rows)
        => rows.Select(static row => row.Event.Context).FirstOrDefault(static context => context.Count > 0)
           ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private async Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
        PlatformEventQuery query,
        CancellationToken ct)
    {
        var cursor = 0L;
        var result = new List<PlatformProductionEvent>();
        while (true)
        {
            var page = await events.QueryAsync(query with { AfterIngestId = cursor, Limit = 500 }, ct).ConfigureAwait(false);
            if (page.Count == 0)
                break;
            result.AddRange(page);
            var next = page.Max(static item => item.IngestId);
            if (next <= cursor)
                throw new InvalidOperationException("历史周期比较查询游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<PlatformProductionEvent>>> LoadCyclesAsync(
        IReadOnlyCollection<string> correlationIds,
        CancellationToken ct)
    {
        var rows = await events.QueryByCorrelationIdsAsync(correlationIds, ct).ConfigureAwait(false);
        return rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.CorrelationId))
            .GroupBy(static row => row.Event.CorrelationId!, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => (IReadOnlyList<PlatformProductionEvent>)group
                    .OrderBy(static row => row.IngestId)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    private sealed record WeightedValue(double Value, double Weight);
    private sealed record FeatureKey(
        string SignalCode,
        string FeatureCode,
        string? PhaseCode,
        string? PhaseName,
        int? PhaseOrder);
}
