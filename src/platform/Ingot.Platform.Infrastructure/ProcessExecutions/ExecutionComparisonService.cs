// 实现基础设施适配器 ExecutionComparisonService，满足应用层端口而不改变领域契约。

using Ingot.Platform.Application.Inspections;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.TimeSeries;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessResearch;
using System.Globalization;
using System.Text.Json;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ExecutionComparisonService(
    IPlatformEventStore events,
    IInspectionRecordStore inspections,
    IInspectionReviewStore reviews,
    IInspectionMasterDataStore inspectionMasterData,
    ProcessAnalysisResolver analysisResolver,
    ITimeSeriesStore timeSeries,
    ProcessExecutionAnalysisEngine? wholeProcessExecutionAnalysis = null,
    ProcessExecutionAnalysisMaterializer? materializer = null,
    IProcessOptimizerClient? optimizerClient = null,
    ExecutionComparisonMetrics? comparisonMetrics = null) : IExecutionComparisonService
{
    private readonly ProcessExecutionAnalysisEngine _wholeProcessExecutionAnalysis = wholeProcessExecutionAnalysis ?? new();
    private readonly ProcessExecutionAnalysisMaterializer? _materializer = materializer;
    private readonly ITimeSeriesStore _timeSeries = timeSeries;
    private readonly ExecutionDiagnosisEngine _diagnosisEngine = new();
    private readonly ExecutionInvestigationReportBuilder _investigationBuilder = new();

    public async Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
        string executionId,
        CancellationToken ct = default,
        string? siteId = null)
    {
        if (string.IsNullOrWhiteSpace(executionId))
            throw new ArgumentException("过程执行标识不能为空。", nameof(executionId));
        executionId = executionId.Trim();
        var rows = await GetProcessExecutionsAsync([executionId], ct, siteId).ConfigureAwait(false);
        return rows.GetValueOrDefault(executionId);
    }

    public async Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default,
        string? siteId = null)
    {
        ArgumentNullException.ThrowIfNull(executionIds);
        var ids = executionIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
            return new Dictionary<string, ExecutionComparisonRow>(StringComparer.Ordinal);

        var useMaterializer = _materializer is not null && string.IsNullOrWhiteSpace(siteId);
        var summarySources = !useMaterializer
            ? []
            : await events.QueryExecutionSummarySourcesAsync(ids, ct).ConfigureAwait(false);
        var summaryByExecution = summarySources.ToDictionary(
            static source => source.ExecutionId,
            StringComparer.Ordinal);
        var executionEvents = !useMaterializer
            ? new Dictionary<string, IReadOnlyList<PlatformProductionEvent>>(
                await LoadProcessExecutionsAsync(ids, ct, siteId).ConfigureAwait(false),
                StringComparer.Ordinal)
            : summaryByExecution.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Events,
                StringComparer.Ordinal);
        var allInspections = InspectionRecordSet.Effective(
            await inspections.QueryAllByExecutionIdsAsync(ids, ct).ConfigureAwait(false));
        var inspectionsByProcessExecution = allInspections
            .GroupBy(static item => item.ExecutionId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            allInspections.Select(static value => value.RecordId).ToArray(), ct).ConfigureAwait(false);
        var plans = await inspectionMasterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var contexts = ids
            .Select(id => ResolveContext(executionEvents.GetValueOrDefault(id, [])))
            .ToArray();
        var analyses = await analysisResolver.ResolveManyAsync(contexts, "production-execution", ct)
            .ConfigureAwait(false);
        var materializedByExecution = new Dictionary<string, MaterializedProcessExecutionAnalysis>(StringComparer.Ordinal);
        if (useMaterializer)
        {
            var missingIds = new List<string>();
            for (var index = 0; index < ids.Length; index++)
            {
                var cached = await _materializer!.TryLoadLatestAsync(
                    ids[index],
                    analyses[index]?.DataModel,
                    analyses[index]?.Plan,
                    ct).ConfigureAwait(false);
                if (cached is null)
                    missingIds.Add(ids[index]);
                else
                    materializedByExecution[ids[index]] = cached;
            }
            if (missingIds.Count > 0)
            {
                var fullRows = await LoadProcessExecutionsAsync(missingIds, ct, siteId).ConfigureAwait(false);
                foreach (var pair in fullRows)
                    executionEvents[pair.Key] = pair.Value;
            }
        }
        var result = new Dictionary<string, ExecutionComparisonRow>(StringComparer.Ordinal);
        for (var index = 0; index < ids.Length; index++)
        {
            var id = ids[index];
            if (!executionEvents.TryGetValue(id, out var rows) || rows.Count == 0)
                continue;
            var analysis = analyses[index];
            var materialized = materializedByExecution.GetValueOrDefault(id) ??
                               await AnalyzeAsync(id, rows, analysis, ct).ConfigureAwait(false);
            var inspectionPlan = ResolveInspectionPlan(plans, rows);
            var eligibleInspections = InspectionRecordSet.AnalysisEligible(
                inspectionsByProcessExecution.GetValueOrDefault(id, []),
                inspectionPlan,
                latestReviews);
            result[id] = BuildRow(
                id,
                rows,
                eligibleInspections,
                latestReviews,
                analysis,
                materialized,
                summaryByExecution.GetValueOrDefault(id)?.SampleCount);
        }
        return result;
    }

    public async Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
        string executionId,
        int limit,
        CancellationToken ct = default,
        string? siteId = null,
        IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
    {
        var baselineEvents = await QueryAllAsync(
            new PlatformEventQuery { SiteId = siteId, ExecutionId = executionId }, ct).ConfigureAwait(false);
        if (baselineEvents.Count == 0)
            return null;

        var baselineContext = ResolveContext(baselineEvents);
        var analysis = await analysisResolver.ResolveAsync(baselineContext, "production-execution", ct)
            .ConfigureAwait(false);
        EnsureComparisonKeysPresent(analysis?.Plan, baselineContext, "基准过程执行");
        var comparisonContext = BuildComparisonContext(analysis?.Plan, baselineContext);
        var completed = await QueryAllAsync(
            new PlatformEventQuery { SiteId = siteId, EventType = "process.execution.completed", Context = comparisonContext }, ct)
            .ConfigureAwait(false);
        var candidateIds = completed
            .Where(item => !string.IsNullOrWhiteSpace(item.Event.ExecutionId) &&
                           !string.Equals(item.Event.ExecutionId, executionId, StringComparison.Ordinal))
            .GroupBy(static item => item.Event.ExecutionId!, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static item => item.Event.OccurredAt).First())
            .OrderByDescending(static item => item.Event.OccurredAt)
            .Take(Math.Min(500, Math.Max(limit, limit * 10)))
            .Select(static item => item.Event.ExecutionId!)
            .ToArray();
        var loadedIds = new[] { executionId }.Concat(candidateIds).ToArray();
        var executionEvents = await LoadProcessExecutionsAsync(loadedIds, ct, siteId).ConfigureAwait(false);
        var comparisonKeys = ResolveComparisonKeys(analysis?.Plan);
        var allIds = new[] { executionId }.Concat(loadedIds.Skip(1).Where(id => ContextsMatch(
                baselineContext,
                ResolveContext(executionEvents.GetValueOrDefault(id, [])),
                comparisonKeys)).Take(limit))
            .ToArray();
        return await BuildComparisonAsync(
            executionId,
            allIds,
            executionEvents,
            analysis,
            additionalKnownUnmeasuredConfounders,
            ct).ConfigureAwait(false);
    }

    public async Task<ExecutionComparisonResult?> CompareSelectedAsync(
        string baselineProcessExecutionId,
        IReadOnlyList<string> executionIds,
        CancellationToken ct = default,
        string? siteId = null,
        IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
    {
        var allIds = new[] { baselineProcessExecutionId }
            .Concat(executionIds.Where(id => !string.Equals(id, baselineProcessExecutionId, StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (allIds.Length < 2)
            throw new ArgumentException("请选择至少两个不同的生产过程执行。", nameof(executionIds));

        var executionEvents = await LoadProcessExecutionsAsync(allIds, ct, siteId).ConfigureAwait(false);
        if (allIds.Any(id => !executionEvents.TryGetValue(id, out var rows) || rows.Count == 0))
            return null;

        var baselineContext = ResolveContext(executionEvents[baselineProcessExecutionId]);
        var analysis = await analysisResolver.ResolveAsync(baselineContext, "production-execution", ct)
            .ConfigureAwait(false);
        var comparisonKeys = ResolveComparisonKeys(analysis?.Plan);
        EnsureComparisonKeysPresent(analysis?.Plan, baselineContext, "基准过程执行");
        var incompatible = allIds.Skip(1).FirstOrDefault(id =>
        {
            var candidateContext = ResolveContext(executionEvents[id]);
            EnsureComparisonKeysPresent(analysis?.Plan, candidateContext, $"过程执行 {id}");
            return !ContextsMatch(baselineContext, candidateContext, comparisonKeys);
        });
        if (incompatible is not null)
        {
            throw new ArgumentException(
                $"过程执行 {incompatible} 与基准过程执行的同类比较键不一致：{string.Join("、", comparisonKeys)}。",
                nameof(executionIds));
        }

        return await BuildComparisonAsync(
                baselineProcessExecutionId,
                allIds,
                executionEvents,
                analysis,
                additionalKnownUnmeasuredConfounders,
                ct)
            .ConfigureAwait(false);
    }

    private async Task<ExecutionComparisonResult> BuildComparisonAsync(
        string baselineProcessExecutionId,
        IReadOnlyList<string> allIds,
        IReadOnlyDictionary<string, IReadOnlyList<PlatformProductionEvent>> executionEvents,
        ResolvedProcessAnalysis? analysis,
        IReadOnlyList<string>? additionalKnownUnmeasuredConfounders,
        CancellationToken ct)
    {
        var allInspections = InspectionRecordSet.Effective(
            await inspections.QueryAllByExecutionIdsAsync(allIds, ct).ConfigureAwait(false));
        var latestReviews = await reviews.GetLatestByInspectionRecordIdsAsync(
            allInspections.Select(static record => record.RecordId).ToArray(), ct).ConfigureAwait(false);
        var plans = await inspectionMasterData.ListInspectionPlansAsync(ct).ConfigureAwait(false);
        var inspectionsByProcessExecution = allInspections.GroupBy(static record => record.ExecutionId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.Ordinal);
        var materializedByProcessExecution = new Dictionary<string, MaterializedProcessExecutionAnalysis>(StringComparer.Ordinal);
        foreach (var id in allIds)
        {
            materializedByProcessExecution[id] = await AnalyzeAsync(id, executionEvents[id], analysis, ct).ConfigureAwait(false);
        }
        var rows = allIds.Select(id =>
            {
                var eventRows = executionEvents[id];
                var plan = ResolveInspectionPlan(plans, eventRows);
                var eligible = InspectionRecordSet.AnalysisEligible(
                    inspectionsByProcessExecution.GetValueOrDefault(id, []), plan, latestReviews);
                return BuildRow(
                    id,
                    eventRows,
                    eligible,
                    latestReviews,
                    analysis,
                    materializedByProcessExecution[id]);
            })
            .ToArray();
        var acceptance = new ExecutionComparisonAcceptance
        {
            ProcessExecutionCount = rows.Length,
            CompleteProcessExecutionCount = rows.Count(static row => row.LifecycleComplete),
            QualityLinkedProcessExecutionCount = rows.Count(static row => row.InspectionOutcomes.Count > 0),
            VisualReviewCompletedProcessExecutionCount = rows.Count(static row => !string.IsNullOrWhiteSpace(row.VisualReviewDecision)),
            AvailableProcessExecutionCount = rows.Count(static row =>
                row.ProcessDataQuality.Status == ProcessDataStatuses.Available),
            DegradedProcessExecutionCount = rows.Count(static row =>
                row.ProcessDataQuality.Status == ProcessDataStatuses.Degraded),
            UnavailableProcessExecutionCount = rows.Count(static row =>
                row.ProcessDataQuality.Status == ProcessDataStatuses.Unavailable),
            EffectiveProcessExecutionWeight = rows.Skip(1).Sum(static row => row.EvidenceWeight)
        };
        var effectiveWeight = acceptance.EffectiveProcessExecutionWeight;
        var diagnosis = await EnrichDiagnosisAsync(
            rows,
            _diagnosisEngine.Analyze(rows),
            optimizerClient,
            ct).ConfigureAwait(false);
        diagnosis = ApplyTransparencyPolicy(
            diagnosis,
            acceptance,
            BuildConfounderDisclosures(
                analysis?.Plan.KnownUnmeasuredConfounders ?? [],
                additionalKnownUnmeasuredConfounders));
        comparisonMetrics?.Observe(diagnosis.Readiness);
        var signalComparisons = BuildSignalComparisons(rows[0], rows.Skip(1).ToArray());
        var qualityAssociations = BuildQualityAssociations(rows);
        var investigation = _investigationBuilder.Build(
            rows[0],
            rows.Skip(1).ToArray(),
            signalComparisons,
            diagnosis,
            acceptance,
            ResolveComparisonKeys(analysis?.Plan));
        return new ExecutionComparisonResult
        {
            BaselineProcessExecutionId = baselineProcessExecutionId,
            ProductFamilyCode = rows[0].ProductFamilyCode,
            AnalysisPlanId = analysis?.Plan.PlanId,
            AnalysisPlanVersion = analysis?.Plan.Version,
            DataModelId = analysis?.DataModel.ModelId,
            DataModelVersion = analysis?.DataModel.Version,
            AnalysisScope = analysis?.Plan.AnalysisScope ?? "production-execution",
            AlignmentMode = analysis?.Plan.AlignmentMode,
            FeatureAlgorithmVersion = ProcessExecutionAnalysisEngine.AlgorithmVersion,
            EvidenceLevel = rows[0].ProcessDataQuality.Status == ProcessDataStatuses.Unavailable ||
                            effectiveWeight < 5
                ? "insufficient"
                : effectiveWeight < 20 ? "exploratory" : "stable",
            Baseline = rows[0],
            HistoricalProcessExecutions = rows.Skip(1).ToArray(),
            SignalComparisons = signalComparisons,
            QualityAssociations = qualityAssociations,
            Diagnosis = diagnosis,
            Investigation = investigation,
            Acceptance = acceptance
        };
    }

    private static ExecutionDiagnosisSummary ApplyTransparencyPolicy(
        ExecutionDiagnosisSummary diagnosis,
        ExecutionComparisonAcceptance acceptance,
        IReadOnlyList<ExecutionConfounderDisclosure> knownUnmeasuredConfounders)
    {
        var reasons = new List<string>();
        if (acceptance.QualityLinkedProcessExecutionCount == 0)
            reasons.Add("quality-outcomes-missing");
        if (diagnosis.PassProcessExecutionCount == 0 || diagnosis.FailProcessExecutionCount == 0)
            reasons.Add("outcome-class-missing");
        if (diagnosis.PassEffectiveWeight < 2 || diagnosis.FailEffectiveWeight < 2)
            reasons.Add("effective-weight-insufficient");
        if (acceptance.UnavailableProcessExecutionCount > 0)
            reasons.Add("process-data-unavailable");

        var mode = reasons.Any(reason => reason is
                "quality-outcomes-missing" or "outcome-class-missing" or "effective-weight-insufficient")
            ? "descriptive-only"
            : string.Equals(diagnosis.EvidenceLevel, "stable", StringComparison.Ordinal) &&
              diagnosis.CrossValidationScore is > 0
                ? "candidate-ranking"
                : "exploratory";
        var observed = diagnosis.Candidates
            .SelectMany(static candidate => candidate.PossibleConfounders)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return diagnosis with
        {
            Readiness = new ExecutionAnalysisReadiness
            {
                Mode = mode,
                BlockingReasons = reasons
            },
            AdjustedContextVariables = diagnosis.ContextVariables,
            ObservedPossibleConfounders = observed,
            KnownUnmeasuredConfounders = knownUnmeasuredConfounders,
            SensitivityAssessment = new ExecutionSensitivityAssessment(),
            Candidates = mode == "descriptive-only" ? [] : diagnosis.Candidates,
            Interactions = mode == "descriptive-only" ? [] : diagnosis.Interactions
        };
    }

    private static IReadOnlyList<ExecutionConfounderDisclosure> BuildConfounderDisclosures(
        IReadOnlyList<KnownUnmeasuredConfounderDefinition> configured,
        IReadOnlyList<string>? additional)
    {
        var values = configured.Select(static value => new ExecutionConfounderDisclosure
        {
            Code = value.Code,
            Name = value.Name,
            Description = value.Description,
            Source = "analysis-plan"
        }).ToList();
        var seen = values.Select(static value => value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sequence = 0;
        foreach (var raw in additional ?? [])
        {
            var name = raw?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 240 || !seen.Add(name))
                continue;
            sequence++;
            values.Add(new ExecutionConfounderDisclosure
            {
                Code = $"additional-{sequence}",
                Name = name,
                Source = "request"
            });
        }
        return values;
    }

    private static async Task<ExecutionDiagnosisSummary> EnrichDiagnosisAsync(
        IReadOnlyList<ExecutionComparisonRow> rows,
        ExecutionDiagnosisSummary robust,
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
                ExecutionKey = row.ExecutionId,
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
                        CandidateScore =
                            ProcessExecutionAnalysisThresholds.CandidateScoreWeight * candidate.CandidateScore +
                            ProcessExecutionAnalysisThresholds.ModelRankScoreWeight * model.RankScore,
                        EvidenceLevel = advanced.CrossValidationScore <= 0
                            ? "exploratory"
                            : model.StabilitySelectionRate >= ProcessExecutionAnalysisThresholds.HighStabilitySelectionRate
                                ? "stable"
                                : model.StabilitySelectionRate >= ProcessExecutionAnalysisThresholds.ModerateStabilitySelectionRate
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
                AlgorithmVersion = $"{ExecutionDiagnosisEngine.AlgorithmVersion}+{advanced.AlgorithmVersion}",
                ModelFamily = advanced.ModelFamily,
                AdjustmentMethod = advanced.AdjustmentMethod,
                CrossValidationScore = advanced.CrossValidationScore,
                FoldCount = advanced.FoldCount,
                StabilityRuns = advanced.StabilityRuns,
                ContextVariables = advanced.ContextVariables,
                Candidates = candidates,
                Interactions = advanced.Interactions.Select(static interaction =>
                    new ExecutionCauseInteraction
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
        ExecutionComparisonRow row)
    {
        var context = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["product_family_code"] = row.ProductFamilyCode,
            ["equipment_id"] = row.EquipmentId
        };
        foreach (var key in new[]
                 {
                     "product_code", "material", "material_code", "material_lot", "material_lot_ref",
                     "material_batch", "equipment_id", "tooling_assembly_id", "tooling_assembly_id",
                     "batch_id", "lot_id", "process_specification_id", "process_specification_version"
                 })
        {
            var source = row.Context.FirstOrDefault(pair =>
                string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(source.Value))
                context[key] = source.Value;
        }
        Add("product_code", row.ProductCode);
        Add("process_specification", row.ProcessSpecificationId is null ? null : $"{row.ProcessSpecificationId}@{row.ProcessSpecificationVersion}");
        Add("tooling_assembly_id", row.ToolingAssemblyId);
        Add("tooling_installation_id", row.ToolingInstallationId);
        return context;

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                context[key] = value;
        }
    }

    private static double? ReadCandidateValue(
        ExecutionComparisonRow row,
        ExecutionCauseCandidate candidate)
    {
        if (candidate.SourceKind == ExecutionCauseSourceKinds.ProcessSpecificationParameter)
        {
            var parameter = row.ControlParameters.FirstOrDefault(value =>
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

    private static IReadOnlyList<ExecutionQualityAssociation> BuildQualityAssociations(
        IReadOnlyList<ExecutionComparisonRow> rows)
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
                return new ExecutionQualityAssociation
                {
                    SignalCode = key.SignalCode,
                    FeatureCode = key.FeatureCode,
                    PhaseCode = key.PhaseCode,
                    PhaseName = key.PhaseName,
                    PhaseOrder = key.PhaseOrder,
                    PassProcessExecutionCount = pass.Length,
                    FailProcessExecutionCount = fail.Length,
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
            .Where(static item => item.PassProcessExecutionCount > 0 && item.FailProcessExecutionCount > 0)
            .OrderByDescending(static item => item.CandidateScore)
            .ThenBy(static item => item.SignalCode, StringComparer.Ordinal)
            .ThenBy(static item => item.PhaseOrder)
            .ThenBy(static item => item.FeatureCode, StringComparer.Ordinal)
            .Take(100)
            .ToArray();
    }

    private static WeightedValue[] FeatureValues(
        IReadOnlyList<ExecutionComparisonRow> rows,
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
        IReadOnlyList<ExecutionComparisonRow> passRows,
        IReadOnlyList<ExecutionComparisonRow> failRows)
    {
        var result = new List<string>();
        AddIfDifferent("product_code", static row => row.ProductCode);
        AddIfDifferent("equipment_id", static row => row.EquipmentId);
        AddIfDifferent("process_specification", static row => $"{row.ProcessSpecificationId}@{row.ProcessSpecificationVersion}");
        AddIfDifferent("tooling_assembly_id", static row => row.ToolingAssemblyId);
        return result;

        void AddIfDifferent(string name, Func<ExecutionComparisonRow, string?> selector)
        {
            var pass = passRows.Select(selector).Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>().ToHashSet(StringComparer.Ordinal);
            var fail = failRows.Select(selector).Where(static value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>().ToHashSet(StringComparer.Ordinal);
            if (pass.Count > 0 && fail.Count > 0 && !pass.SetEquals(fail))
                result.Add(name);
        }
    }

    private static IReadOnlyList<ProcessSignalComparison> BuildSignalComparisons(
        ExecutionComparisonRow baseline,
        IReadOnlyList<ExecutionComparisonRow> historical)
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
            var robustScaleFloor = Math.Max(Math.Abs(median ?? 0d) * 1e-9d, 1e-9d);
            double? baselinePercentile = feature.Value.HasValue && weighted.Length > 0
                ? weighted.Where(item => item.Value <= feature.Value.Value).Sum(static item => item.Weight) /
                  weighted.Sum(static item => item.Weight)
                : null;
            return new ProcessSignalComparison
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
                RobustDeviation = feature.Value.HasValue && median.HasValue && mad > robustScaleFloor
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

    private ExecutionComparisonRow BuildRow(
        string executionId,
        IReadOnlyList<PlatformProductionEvent> rows,
        IReadOnlyList<InspectionRecord> inspectionRecords,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews,
        ResolvedProcessAnalysis? analysis,
        MaterializedProcessExecutionAnalysis materialized,
        int? sampleCountOverride = null)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var first = ordered[0];
        var started = ordered.FirstOrDefault(static row => row.Event.EventType == "process.execution.started");
        var completed = ordered.LastOrDefault(static row => row.Event.EventType == "process.execution.completed");
        var wholeProcessExecution = materialized.Analysis;
        var visualRecord = inspectionRecords.Where(static record => record.Attachments.Count > 0)
            .OrderByDescending(static record => record.MeasuredAt)
            .FirstOrDefault();
        var visualReview = visualRecord is null ? null : latestReviews.GetValueOrDefault(visualRecord.RecordId);
        var context = ResolveContext(ordered);
        var lifecycleComplete = started is not null && completed is not null;
        return new ExecutionComparisonRow
        {
            ExecutionId = executionId,
            EquipmentId = first.Event.Subject.Id,
            EdgeIds = ordered.Select(static row => row.EdgeId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            Context = context,
            HasStarted = started is not null,
            HasCompleted = completed is not null,
            LifecycleComplete = lifecycleComplete,
            StartedAt = started?.Event.OccurredAt ?? first.Event.OccurredAt,
            CompletedAt = completed?.Event.OccurredAt,
            DurationMs = lifecycleComplete
                ? (completed!.Event.OccurredAt - started!.Event.OccurredAt).TotalMilliseconds
                : null,
            ProductFamilyCode = ProcessAnalysisResolver.ContextValue(context, "product_family_code") ?? "unknown",
            ProductCode = ProcessAnalysisResolver.ContextValue(context, "product_code"),
            ProcessSpecificationId = ProcessAnalysisResolver.ContextValue(context, "actual_process_specification_id") ??
                                     ProcessAnalysisResolver.ContextValue(context, "process_specification_id"),
            ProcessSpecificationVersion = ProcessAnalysisResolver.ContextValue(context, "actual_process_specification_version") ??
                                          ProcessAnalysisResolver.ContextValue(context, "process_specification_version"),
            ToolingInstallationId = ProcessAnalysisResolver.ContextValue(context, "tooling_installation_id"),
            ToolingAssemblyId = ProcessAnalysisResolver.ContextValue(context, "tooling_assembly_id"),
            AssemblyRevisionId = ProcessAnalysisResolver.ContextValue(context, "assembly_revision_id"),
            AssemblyRevision = ProcessAnalysisResolver.ContextValue(context, "assembly_revision"),
            OutputItemId = ProcessAnalysisResolver.ContextValue(context, "output_item_id"),
            ExternalBatchRef = ProcessAnalysisResolver.ContextValue(context, "external_batch_ref"),
            MaterialLotRef = ProcessAnalysisResolver.ContextValue(context, "material_lot_ref") ??
                             ProcessAnalysisResolver.ContextValue(context, "material_lot"),
            SampleCount = sampleCountOverride ?? wholeProcessExecution.Quality.SampleCount,
            ExpectedSampleCount = 0,
            ProcessDataQuality = wholeProcessExecution.Quality,
            EvidenceWeight = wholeProcessExecution.Quality.Status switch
            {
                ProcessDataStatuses.Available => 1d,
                ProcessDataStatuses.Degraded => 0.5d,
                _ => 0d
            },
            PhaseCount = wholeProcessExecution.Phases.Count(static phase => phase.Code != "unknown"),
            InspectionOutcomes = inspectionRecords.Select(static record => record.Outcome)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            VisualReviewDecision = visualReview?.Decision,
            Signals = wholeProcessExecution.Signals,
            Phases = wholeProcessExecution.Phases,
            AnalysisMaterialization = materialized.Materialization,
            ControlParameters = BuildControlParameterValues(analysis?.DataModel, ordered)
        };
    }

    private static InspectionPlan? ResolveInspectionPlan(
        IReadOnlyList<InspectionPlan> plans,
        IReadOnlyList<PlatformProductionEvent> rows)
    {
        if (rows.Count == 0)
            return null;
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt)
            .ThenBy(static row => row.IngestId).ToArray();
        var first = ordered[0];
        var startedAt = ordered.FirstOrDefault(static row => row.Event.EventType == "process.execution.started")?
            .Event.OccurredAt ?? first.Event.OccurredAt;
        return InspectionPlanMatcher.Resolve(
            plans,
            ResolveContext(ordered),
            first.Event.Subject.Id,
            startedAt);
    }

    private async Task<MaterializedProcessExecutionAnalysis> AnalyzeAsync(
        string executionId,
        IReadOnlyList<PlatformProductionEvent> rows,
        ResolvedProcessAnalysis? analysis,
        CancellationToken ct)
    {
        var ordered = rows.OrderBy(static row => row.Event.OccurredAt).ThenBy(static row => row.IngestId).ToArray();
        var startedAt = ordered.FirstOrDefault(static row => row.Event.EventType == "process.execution.started")?.Event.OccurredAt;
        var completedAt = ordered.LastOrDefault(static row => row.Event.EventType == "process.execution.completed")?.Event.OccurredAt;
        var samples = await TimeSeriesFrameReader.QueryAllAsync(
            _timeSeries,
            new TimeSeriesQuery { SiteId = ordered[0].SiteId, ExecutionId = executionId },
            ct).ConfigureAwait(false);
        if (_materializer is not null)
        {
            return await _materializer.GetOrComputeAsync(
                executionId,
                samples,
                startedAt,
                completedAt,
                analysis?.DataModel,
                analysis?.Plan,
                ct).ConfigureAwait(false);
        }

        var source = ProcessExecutionAnalysisMaterializer.CreateSourceFingerprint(samples);
        return new MaterializedProcessExecutionAnalysis(
            _wholeProcessExecutionAnalysis.Analyze(
                samples,
                startedAt,
                completedAt,
                analysis?.DataModel,
                analysis?.Plan),
            new ProcessExecutionAnalysisMaterialization
            {
                Status = "query-time",
                AlgorithmVersion = ProcessExecutionAnalysisEngine.AlgorithmVersion,
                SourceMinIngestId = source.MinIngestId,
                SourceMaxIngestId = source.MaxIngestId,
                SourceEventCount = source.EventCount,
                SourceContentHash = source.ContentHash
            });
    }

    private static IReadOnlyList<ExecutionControlParameterValue> BuildControlParameterValues(
        ProcessDataModel? model,
        IReadOnlyList<PlatformProductionEvent> rows)
    {
        var definitions = model?.ControlParameters.ToDictionary(static item => item.Code, StringComparer.Ordinal)
                          ?? new Dictionary<string, ControlParameterDefinition>(StringComparer.Ordinal);
        var applied = rows
            .Where(static row => row.Event.EventType == "process.specification.applied")
            .OrderByDescending(static row => row.Event.OccurredAt)
            .ThenByDescending(static row => row.IngestId)
            .FirstOrDefault();
        if (applied is not null &&
            applied.Event.Data.TryGetValue("resolvedParameters", out var raw) &&
            TryReadObject(raw, out var actual))
        {
            var source = ProcessAnalysisResolver.ContextValue(applied.Event.Context, "control_parameter_source");
            var captureStatus = ProcessAnalysisResolver.ContextValue(applied.Event.Context, "control_parameter_capture_status");
            var captured = actual
                .Select(pair =>
                {
                    definitions.TryGetValue(pair.Key, out var definition);
                    return TryReadDouble(pair.Value, out var value)
                        ? new ExecutionControlParameterValue
                        {
                            Code = pair.Key,
                            Name = definition?.DisplayName,
                            Unit = definition?.Unit,
                            Source = source,
                            CaptureStatus = captureStatus,
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
            if (key.StartsWith("actual_", StringComparison.Ordinal))
                continue;
            var value = ProcessAnalysisResolver.ContextValue(baselineContext, key);
            if (!string.IsNullOrWhiteSpace(value))
                result[ToEventContextKey(key)] = value;
        }
        return result;
    }

    private static IReadOnlyList<string> ResolveComparisonKeys(ProcessAnalysisPlan? plan)
    {
        var keys = (plan?.ComparisonKeys.Count > 0 ? plan.ComparisonKeys : ["product_family_code"]).ToList();
        if (plan?.CohortDimension?.Trim().ToLowerInvariant() is "process_specification" or "process_specification_version")
        {
            if (!keys.Contains("actual_process_specification_id", StringComparer.Ordinal))
                keys.Add("actual_process_specification_id");
            if (!keys.Contains("actual_process_specification_version", StringComparer.Ordinal))
                keys.Add("actual_process_specification_version");
        }
        return keys;
    }

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
    {
        var context = new Dictionary<string, string>(
            rows.Select(static row => row.Event.Context).FirstOrDefault(static value => value.Count > 0)
            ?? new Dictionary<string, string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var applied = rows.Where(static row => row.Event.EventType == "process.specification.applied")
            .OrderByDescending(static row => row.Event.OccurredAt)
            .ThenByDescending(static row => row.IngestId)
            .FirstOrDefault();
        if (applied is not null)
        {
            AddActualContext(context, "actual_process_specification_id", applied.Event.Data.GetValueOrDefault("processSpecificationId"));
            AddActualContext(context, "actual_process_specification_version", applied.Event.Data.GetValueOrDefault("processSpecificationVersion"));
        }
        return context;
    }

    private static void AddActualContext(IDictionary<string, string> context, string key, object? raw)
    {
        var value = raw is JsonElement element ? element.ToString() : Convert.ToString(raw, CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(value))
            context[key] = value.Trim();
    }

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
                throw new InvalidOperationException("历史过程执行比较查询游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<PlatformProductionEvent>>> LoadProcessExecutionsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct,
        string? siteId = null)
    {
        var rows = await events.QueryByExecutionIdsAsync(executionIds, ct).ConfigureAwait(false);
        return rows
            .Where(row => string.IsNullOrWhiteSpace(siteId) ||
                          string.Equals(row.SiteId, siteId.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(static row => !string.IsNullOrWhiteSpace(row.Event.ExecutionId))
            .GroupBy(static row => row.Event.ExecutionId!, StringComparer.Ordinal)
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
