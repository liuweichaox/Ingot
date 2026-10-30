// 从有限范围的生产事件和检验事实计算可审计的数据可靠性基线。

using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Platform.Application.Acquisition;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessExecutions;

namespace Ingot.Platform.Infrastructure.Analytics;

public sealed class DataReliabilityBaselineService(
    IPlatformEventStore events,
    IExecutionComparisonService executions,
    IProcessSampleConfigurationReferenceVerifier sampleConfigurationReferenceVerifier,
    ResearchContextAdmissionEvaluator? contextAdmission = null,
    IEdgeTaskDeploymentHistoryReader? taskDeploymentHistory = null,
    IEdgeSiteDirectory? edgeSites = null,
    IIngestionTaskStore? ingestionTasks = null) : IDataReliabilityBaselineService
{
    private readonly ResearchContextAdmissionEvaluator _contextAdmission =
        contextAdmission ?? new ResearchContextAdmissionEvaluator();

    private static readonly string[] ContextFieldsToTrack =
    [
        "context_capture_status",
        "control_parameter_capture_status",
        "equipment_id",
        "execution_id",
        "product_family_code",
        "product_code",
        "process_specification_id",
        "process_specification_version",
        "output_item_id",
        "production_context_id",
        "material_lot_ref",
        "material_specification",
        "external_order_ref",
        "external_batch_ref",
        "tooling_installation_id",
        "tooling_assembly_id",
        "assembly_revision",
        "assembly_revision_id",
        "tooling_usage_count",
        "maintenance_status",
        "calibration_status",
        "calibration_ref",
        "calibration_valid_until"
    ];

    private static readonly string[] RequiredContextFields = ["equipment_id", "execution_id"];

    private static readonly (string Field, string Name)[] ContextFactors =
    [
        ("equipment_id", "设备"),
        ("tooling_assembly_id", "工装"),
        ("material_lot_ref", "材料批次")
    ];

    private static readonly (string Code, string Name)[] AdmissionExclusionReasons =
    [
        ("lifecycle_incomplete", "运行生命周期不完整"),
        ("process_data_incomplete", "过程数据未达到完整标准"),
        ("actual_parameters_missing", "缺少设备实际参数回读"),
        ("parameter_unit_missing", "实际参数或单位不完整"),
        ("context_capture_invalid", "生产上下文捕获失败"),
        ("minimal_context_missing", "最小上下文快照不完整"),
        ("quality_unlinked", "没有关联有效质量结果")
    ];

    private const int MaximumFactorLevels = 50;
    private const int MaximumScannedEvents = 50_000;
    private const int MaximumExpectedCoverageItems = 2_000;
    private const int MaximumExpectedCoverageCodesPerKind = 500;
    private const int MaximumExpectedCoverageCodesTotal = 100_000;
    private const int MaximumCoverageCandidateComparisons = 250_000;

    public async Task<DataReliabilityBaseline> CalculateAsync(
        DataReliabilityBaselineQuery query,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.SiteId))
            throw new ArgumentException("数据可靠性基线必须指定站点。", nameof(query));
        if (query.From > query.To)
            throw new ArgumentException("开始时间不能晚于结束时间。", nameof(query));
        var generatedAt = DateTimeOffset.UtcNow;
        var maximumRuns = Math.Clamp(query.MaximumRuns, 1, 2000);
        var scopedQuery = new PlatformEventQuery
        {
            SiteId = Normalize(query.SiteId),
            EdgeId = Normalize(query.EdgeId),
            EquipmentId = Normalize(query.EquipmentId),
            SubjectType = Normalize(query.SubjectType)?.ToLowerInvariant(),
            SubjectId = Normalize(query.SubjectId),
            From = query.From,
            To = query.To
        };
        var lifecycleEvents = await Task.WhenAll(
                QueryAllAsync(scopedQuery with { EventType = "process.execution.started" }, ct),
                QueryAllAsync(scopedQuery with { EventType = "process.execution.completed" }, ct))
            .ConfigureAwait(false);
        var completedEvents = lifecycleEvents[1];
        var matchingIds = lifecycleEvents
            .SelectMany(static items => items)
            .Where(static item => !string.IsNullOrWhiteSpace(item.Event.ExecutionId))
            .GroupBy(static item => item.Event.ExecutionId!, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static item => item.Event.OccurredAt).First())
            .OrderByDescending(static item => item.Event.OccurredAt)
            .Select(static item => item.Event.ExecutionId!)
            .ToArray();
        var completedIds = completedEvents
            .Where(static item => !string.IsNullOrWhiteSpace(item.Event.ExecutionId))
            .Select(static item => item.Event.ExecutionId!)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        var selectedIds = matchingIds.Take(maximumRuns).ToArray();
        var executionMap = await executions.GetProcessExecutionsAsync(
            selectedIds, ct, Normalize(query.SiteId)).ConfigureAwait(false);
        var rows = selectedIds
            .Where(executionMap.ContainsKey)
            .Select(id => executionMap[id])
            .ToArray();
        var evaluatedRows = rows
            .Select(row => (Row: row, ReasonCodes: AdmissionExclusionCodes(row)))
            .ToArray();
        var impactedRows = evaluatedRows
            .Where(static item => item.ReasonCodes.Count > 0)
            .ToArray();
        var reportedConfigurationEvidence = impactedRows
            .SelectMany(static item => item.Row.SampleConfigurationEvidence)
            .ToArray();
        var verifiedConfigurationEvidence = await sampleConfigurationReferenceVerifier.VerifyAsync(
                scopedQuery.SiteId!, reportedConfigurationEvidence, ct)
            .ConfigureAwait(false);
        if (verifiedConfigurationEvidence.Count != reportedConfigurationEvidence.Length)
            throw new InvalidOperationException("采样来源核验结果与输入证据数量不一致。");

        var runImpacts = new List<ReliabilityRunImpact>(impactedRows.Length);
        var evidenceCursor = 0;
        foreach (var item in impactedRows)
        {
            var sourceCount = item.Row.SampleConfigurationEvidence.Count;
            var verifiedForRun = new ProcessSampleConfigurationEvidence[sourceCount];
            for (var index = 0; index < sourceCount; index++)
                verifiedForRun[index] = verifiedConfigurationEvidence[evidenceCursor++];
            runImpacts.Add(new ReliabilityRunImpact
            {
                ExecutionId = item.Row.ExecutionId,
                EquipmentId = item.Row.EquipmentId,
                EdgeIds = item.Row.EdgeIds,
                HasStarted = item.Row.HasStarted,
                HasCompleted = item.Row.HasCompleted,
                StartedAt = item.Row.HasStarted ? item.Row.StartedAt : null,
                CompletedAt = item.Row.HasCompleted ? item.Row.CompletedAt : null,
                ProcessDataStatus = item.Row.ProcessDataQuality.Status,
                ReasonCodes = item.ReasonCodes,
                SampleConfigurationEvidence = verifiedForRun
            });
        }
        var coverageProjection = await ProjectExpectedConfigurationCoverageAsync(
                runImpacts, query, generatedAt, ct)
            .ConfigureAwait(false);
        runImpacts = coverageProjection.RunImpacts;

        var denominator = rows.Length;
        var lifecycleComplete = rows.Count(static row => row.LifecycleComplete);
        var processComplete = rows.Count(static row =>
            row.ProcessDataQuality.Status == ProcessDataStatuses.Available);
        var processUsable = rows.Count(static row =>
            row.ProcessDataQuality.Status != ProcessDataStatuses.Unavailable);
        var actualParameters = rows.Count(HasActualParameters);
        var parameterUnits = rows.Count(HasCompleteActualParameterUnits);
        var contextIntegrity = rows.Count(row =>
            _contextAdmission.Evaluate(row.Context, null).Admitted);
        var minimalContext = rows.Count(HasMinimalContext);
        var qualityLinked = rows.Count(static row => row.InspectionOutcomes.Count > 0);
        var eligible = evaluatedRows.Count(static item => item.ReasonCodes.Count == 0);

        var reasonCounts = evaluatedRows
            .SelectMany(static item => item.ReasonCodes)
            .GroupBy(static code => code, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var exclusions = AdmissionExclusionReasons
            .Select(reason => Exclusion(
                reason.Code,
                reason.Name,
                reasonCounts.GetValueOrDefault(reason.Code)))
            .Where(static item => item.RunCount > 0)
            .ToArray();

        var factorSummaries = BuildFactorSummaries(rows);
        var factorOverlaps = BuildFactorOverlaps(rows);
        return new DataReliabilityBaseline
        {
            GeneratedAt = generatedAt,
            SiteId = Normalize(query.SiteId),
            From = query.From,
            To = query.To,
            EdgeId = Normalize(query.EdgeId),
            EquipmentId = Normalize(query.EquipmentId),
            SubjectType = Normalize(query.SubjectType)?.ToLowerInvariant(),
            SubjectId = Normalize(query.SubjectId),
            MatchingCompletedRunCount = completedIds.Count,
            MatchingObservedRunCount = matchingIds.Length,
            AnalyzedRunCount = denominator,
            MissingExecutionRowCount = selectedIds.Length - rows.Length,
            Truncated = matchingIds.Length > maximumRuns,
            Rates =
            [
                Rate("lifecycle_completeness", "运行生命周期完整率", lifecycleComplete, denominator,
                    "同时存在 process.execution.started 与 process.execution.completed 的运行 / 已分析运行。"),
                Rate("process_data_completeness", "过程数据完整率", processComplete, denominator,
                    "过程数据状态为“可用”的运行 / 已分析运行；“降级”状态不计为完整。"),
                Rate("process_data_usability", "过程数据可用率", processUsable, denominator,
                    "过程数据状态不是“不可用”的运行 / 已分析运行。"),
                Rate("actual_parameter_coverage", "实际参数覆盖率", actualParameters, denominator,
                    "存在“配方版本已应用”现场实际参数回读的运行 / 已分析运行；不使用配方版本计划值。"),
                Rate("actual_parameter_unit_completeness", "实际参数单位完整率", parameterUnits, denominator,
                    "全部实际参数具有明确单位的运行 / 已分析运行。"),
                Rate("context_capture_integrity", "生产上下文捕获可信率", contextIntegrity, denominator,
                    "上下文未标记为“配置缺失”或其他无效捕获状态的运行 / 已分析运行。"),
                Rate("minimal_context_coverage", "最小上下文覆盖率", minimalContext, denominator,
                    "上下文捕获有效且同时包含设备编号与运行编号的运行 / 已分析运行。"),
                Rate("run_quality_association", "运行—质量关联率", qualityLinked, denominator,
                    "至少关联一条有效检验结果的运行 / 已分析运行。"),
                Rate("analysis_admission", "通用基线条件通过率", eligible, denominator,
                    "生命周期、过程数据、实际参数、单位、最小上下文和质量关联全部满足通用数据可靠性基线要求的运行 / 已分析运行；具体分析仍有各自的证据要求。")
            ],
            ContextFields = ContextFieldsToTrack.Select(field =>
            {
                var present = rows.Count(row => HasContext(row, field));
                return new ContextFieldCoverage
                {
                    Field = field,
                    PresentRunCount = present,
                    RunCount = denominator,
                    Coverage = Divide(present, denominator),
                    RequiredForAdmission = RequiredContextFields.Contains(field, StringComparer.Ordinal)
                };
            }).ToArray(),
            ContextFactors = factorSummaries,
            ContextFactorOverlaps = factorOverlaps,
            UnidentifiableConfoundingCount = factorOverlaps.Count(static item =>
                item.Identifiability == "confounded"),
            Exclusions = exclusions,
            RunImpacts = runImpacts,
            TaskDeploymentObservationHistoryTruncated = coverageProjection.HistoryTruncated,
            ExpectedConfigurationCoverageTruncated = coverageProjection.ResultTruncated,
            DuplicateTimestampCount = rows.Sum(static row => row.ProcessDataQuality.DuplicateTimestampCount),
            OutOfOrderCount = rows.Sum(static row => row.ProcessDataQuality.OutOfOrderCount),
            SequenceGapCount = rows.Sum(static row => row.ProcessDataQuality.SequenceGapCount),
            MaximumSampleGapMs = Maximum(rows.Select(static row => row.ProcessDataQuality.MaximumGapMs)),
            MaximumAbsoluteSourceClockOffsetMs = Maximum(rows.Select(static row =>
                row.ProcessDataQuality.MaximumAbsoluteSourceClockOffsetMs)),
            WorstRunP95PlatformIngestLatencyMs = Maximum(rows.Select(static row =>
                row.ProcessDataQuality.P95PlatformIngestLatencyMs)),
            MaximumPlatformIngestLatencyMs = Maximum(rows.Select(static row =>
                row.ProcessDataQuality.MaximumPlatformIngestLatencyMs)),
            NegativePlatformIngestLatencyCount = rows.Sum(static row =>
                row.ProcessDataQuality.NegativePlatformIngestLatencyCount)
        };
    }

    // Joins receipt-bounded applied-version observations to runs and detailed sample groups without asserting causality.
    private async Task<CoverageProjection> ProjectExpectedConfigurationCoverageAsync(
        List<ReliabilityRunImpact> runImpacts,
        DataReliabilityBaselineQuery query,
        DateTimeOffset generatedAt,
        CancellationToken ct)
    {
        if (runImpacts.Count == 0 || taskDeploymentHistory is null || edgeSites is null || ingestionTasks is null)
            return new CoverageProjection(runImpacts, false, false);

        var siteId = Normalize(query.SiteId)!;
        var authorizedEdgeIds = await edgeSites.ListEdgeIdsAsync(siteId, ct).ConfigureAwait(false);
        if (authorizedEdgeIds.Count == 0)
            return new CoverageProjection(runImpacts, false, false);

        var runSpans = new Dictionary<string, RunTimeSpan>(StringComparer.Ordinal);
        foreach (var impact in runImpacts)
        {
            if (ResolveRunTimeSpan(impact, query.To, generatedAt) is { } span)
                runSpans[impact.ExecutionId] = span;
        }
        if (runSpans.Count == 0)
            return new CoverageProjection(runImpacts, false, false);

        var scopedEdgeIds = runImpacts
            .SelectMany(static impact => impact.EdgeIds)
            .Where(edgeId => !string.IsNullOrWhiteSpace(edgeId) && authorizedEdgeIds.Contains(edgeId))
            .Select(static edgeId => edgeId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scopedEdgeIds.Length == 0)
            return new CoverageProjection(runImpacts, false, false);

        var from = runSpans.Values.Min(static span => span.Start);
        var to = runSpans.Values.Max(static span => span.End);
        var observationResult = await taskDeploymentHistory.FindOverlappingAsync(
                siteId, scopedEdgeIds, from, to, ct)
            .ConfigureAwait(false);
        var historyTruncated = observationResult.Truncated;
        var observationsByEdge = observationResult.Observations
            .Where(item => authorizedEdgeIds.Contains(item.EdgeId))
            .GroupBy(static item => item.EdgeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var candidates = new List<RunObservationCandidate>(Math.Min(
            MaximumExpectedCoverageItems, observationResult.Observations.Count));
        var candidateComparisons = 0;
        var resultTruncated = false;
        foreach (var impact in runImpacts)
        {
            if (!runSpans.TryGetValue(impact.ExecutionId, out var span))
                continue;
            var stop = false;
            foreach (var edgeId in impact.EdgeIds
                         .Where(edgeId => !string.IsNullOrWhiteSpace(edgeId) && authorizedEdgeIds.Contains(edgeId))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!observationsByEdge.TryGetValue(edgeId, out var edgeObservations))
                    continue;
                foreach (var observation in edgeObservations)
                {
                    candidateComparisons++;
                    if (candidateComparisons > MaximumCoverageCandidateComparisons)
                    {
                        resultTruncated = true;
                        stop = true;
                        break;
                    }
                    var observationEnd = observation.ClosedObservedAt ?? observation.LastObservedAt;
                    if (observation.FirstObservedAt > span.End || observationEnd < span.Start)
                        continue;
                    if (candidates.Count == MaximumExpectedCoverageItems)
                    {
                        resultTruncated = true;
                        stop = true;
                        break;
                    }
                    candidates.Add(new RunObservationCandidate(
                        impact,
                        observation,
                        observation.FirstObservedAt > span.Start ? observation.FirstObservedAt : span.Start,
                        observationEnd < span.End ? observationEnd : span.End));
                }
                if (stop)
                    break;
            }
            if (stop)
                break;
        }

        if (candidates.Count == 0)
            return new CoverageProjection(runImpacts, historyTruncated, resultTruncated);

        var references = candidates
            .Select(static candidate => new IngestionTaskVersionReference(
                candidate.Observation.TaskId, candidate.Observation.TaskVersion))
            .Distinct()
            .ToArray();
        var versionMetadata = await ingestionTasks.FindCoverageMetadataAsync(
                references, scopedEdgeIds, ct)
            .ConfigureAwait(false);
        var metadataByVersion = versionMetadata
            .GroupBy(static item => new TaskVersionKey(item.TaskId, item.Version))
            .ToDictionary(static group => group.Key, static group => group.First());

        var expectedByExecution = new Dictionary<string, List<ExpectedConfigurationCoverageEvidence>>(StringComparer.Ordinal);
        var emittedItemCount = 0;
        var emittedCodeCount = 0;
        foreach (var candidate in candidates)
        {
            var observation = candidate.Observation;
            var key = new TaskVersionKey(observation.TaskId, observation.TaskVersion);
            var hasMetadata = metadataByVersion.TryGetValue(key, out var metadata);
            var metadataEdgeMatches = hasMetadata && metadata!.AuthorizedEdgeId is not null &&
                                      authorizedEdgeIds.Contains(metadata.AuthorizedEdgeId) &&
                                      string.Equals(metadata.AuthorizedEdgeId, observation.EdgeId,
                                          StringComparison.OrdinalIgnoreCase);
            var subjectMatch = metadataEdgeMatches
                ? MatchRunSubject(metadata!, candidate.Impact)
                : SubjectMatch.Unknown;
            if (subjectMatch == SubjectMatch.Mismatch)
                continue;

            var metadataVerified = metadataEdgeMatches && subjectMatch == SubjectMatch.Match;
            var coverage = BuildCoverageEvidence(candidate, metadataVerified ? metadata : null,
                candidate.Impact.SampleConfigurationEvidence, historyTruncated);
            if (coverage.SampleEvidenceStatus ==
                ExpectedConfigurationSampleEvidenceStatuses.CoverageResultTruncated)
                resultTruncated = true;

            var rowCodes = coverage.RequiredDataItemCodes.Count +
                           coverage.ObservedDataItemCodes.Count +
                           coverage.NotObservedRequiredDataItemCodes.Count;
            if (!historyTruncated && metadataVerified &&
                (metadata!.RequiredDataItemCodesTruncated || rowCodes > MaximumExpectedCoverageCodesPerKind * 3 ||
                 emittedCodeCount + rowCodes > MaximumExpectedCoverageCodesTotal))
            {
                coverage = coverage with
                {
                    SampleEvidenceStatus = ExpectedConfigurationSampleEvidenceStatuses.CoverageResultTruncated,
                    SampleFrameCount = 0,
                    RequiredDataItemCodes = [],
                    ObservedDataItemCodes = [],
                    NotObservedRequiredDataItemCodes = []
                };
                resultTruncated = true;
                rowCodes = 0;
            }

            if (emittedItemCount >= MaximumExpectedCoverageItems)
            {
                resultTruncated = true;
                break;
            }
            emittedItemCount++;
            emittedCodeCount += rowCodes;
            if (!expectedByExecution.TryGetValue(candidate.Impact.ExecutionId, out var runEvidence))
            {
                runEvidence = [];
                expectedByExecution.Add(candidate.Impact.ExecutionId, runEvidence);
            }
            runEvidence.Add(coverage);
        }

        var enriched = runImpacts.Select(impact => expectedByExecution.TryGetValue(
                impact.ExecutionId, out var evidence)
            ? impact with
            {
                ExpectedConfigurationCoverage = evidence
                    .OrderBy(static item => item.FirstObservedAt)
                    .ThenBy(static item => item.EdgeId, StringComparer.Ordinal)
                    .ThenBy(static item => item.TaskId, StringComparer.Ordinal)
                    .ThenBy(static item => item.TaskVersion)
                    .ToArray()
            }
            : impact).ToList();
        return new CoverageProjection(enriched, historyTruncated, resultTruncated);
    }

    private static ExpectedConfigurationCoverageEvidence BuildCoverageEvidence(
        RunObservationCandidate candidate,
        IngestionTaskCoverageMetadata? metadata,
        IReadOnlyList<ProcessSampleConfigurationEvidence> sampleEvidence,
        bool historyTruncated)
    {
        var observation = candidate.Observation;
        var requiredCodes = metadata?.RequiredDataItemCodes ?? [];
        var exactEvidence = sampleEvidence.Where(item =>
                string.Equals(item.AppliedConfigurationKind, "ingestion-task", StringComparison.Ordinal) &&
                string.Equals(item.AppliedConfigurationId, observation.TaskId, StringComparison.OrdinalIgnoreCase) &&
                item.AppliedConfigurationVersion == observation.TaskVersion &&
                string.Equals(item.SourceEdgeId, observation.EdgeId, StringComparison.OrdinalIgnoreCase) &&
                item.SummarizedConfigurationGroupCount == 0)
            .ToArray();
        var verifiedExactEvidence = exactEvidence.Where(static item =>
                string.Equals(item.ReferenceVerificationStatus,
                    ProcessSampleConfigurationReferenceStatuses.Matched, StringComparison.Ordinal))
            .ToArray();
        var rawExactEvidenceExists = sampleEvidence.Any(item =>
            string.Equals(item.AppliedConfigurationKind, "ingestion-task", StringComparison.Ordinal) &&
            string.Equals(item.AppliedConfigurationId, observation.TaskId, StringComparison.OrdinalIgnoreCase) &&
            item.AppliedConfigurationVersion == observation.TaskVersion &&
            string.Equals(item.SourceEdgeId, observation.EdgeId, StringComparison.OrdinalIgnoreCase));

        var observedCodes = new HashSet<string>(StringComparer.Ordinal);
        var observedCodesTruncated = false;
        long frameCount = 0;
        foreach (var item in verifiedExactEvidence)
        {
            frameCount += Math.Max(0, item.SampleFrameCount);
            foreach (var code in item.ObservedDataItemCodes)
            {
                if (string.IsNullOrWhiteSpace(code))
                    continue;
                observedCodes.Add(code);
                if (observedCodes.Count > MaximumExpectedCoverageCodesPerKind)
                {
                    observedCodesTruncated = true;
                    break;
                }
            }
            if (observedCodesTruncated)
                break;
        }

        var hasSummarizedEvidence = sampleEvidence.Any(static item => item.SummarizedConfigurationGroupCount > 0);
        var hasUnattributableEvidence = sampleEvidence.Any(item =>
            item.SampleFrameCount > 0 &&
            string.Equals(item.SourceEdgeId, observation.EdgeId, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(item.AppliedConfigurationKind) ||
             string.IsNullOrWhiteSpace(item.AppliedConfigurationId) ||
             item.AppliedConfigurationVersion is null));
        var status = historyTruncated
            ? ExpectedConfigurationSampleEvidenceStatuses.ObservationHistoryTruncated
            : metadata is null
                ? ExpectedConfigurationSampleEvidenceStatuses.UnverifiableTaskVersion
                : metadata.RequiredDataItemCodesTruncated || observedCodesTruncated
                    ? ExpectedConfigurationSampleEvidenceStatuses.CoverageResultTruncated
                    : verifiedExactEvidence.Length > 0
                        ? ExpectedConfigurationSampleEvidenceStatuses.Matched
                        : rawExactEvidenceExists || hasUnattributableEvidence
                            ? ExpectedConfigurationSampleEvidenceStatuses.UnverifiableTaskVersion
                            : hasSummarizedEvidence
                                ? ExpectedConfigurationSampleEvidenceStatuses.SummarizedUnverifiable
                                : ExpectedConfigurationSampleEvidenceStatuses.NoMatchingFrames;

        IReadOnlyList<string> required = status is ExpectedConfigurationSampleEvidenceStatuses.ObservationHistoryTruncated or
            ExpectedConfigurationSampleEvidenceStatuses.UnverifiableTaskVersion or
            ExpectedConfigurationSampleEvidenceStatuses.CoverageResultTruncated
            ? []
            : requiredCodes;
        IReadOnlyList<string> observed = status == ExpectedConfigurationSampleEvidenceStatuses.Matched
            ? observedCodes.Order(StringComparer.Ordinal).ToArray()
            : [];
        IReadOnlyList<string> notObserved = status == ExpectedConfigurationSampleEvidenceStatuses.Matched
            ? required.Except(observed, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
            : status == ExpectedConfigurationSampleEvidenceStatuses.NoMatchingFrames
                ? required
                : [];
        var includeCounts = status == ExpectedConfigurationSampleEvidenceStatuses.Matched;

        return new ExpectedConfigurationCoverageEvidence
        {
            TaskId = observation.TaskId,
            TaskVersion = observation.TaskVersion,
            EdgeId = observation.EdgeId,
            ConfigurationHash = observation.ConfigurationHash,
            ConfigurationSource = observation.ConfigurationSource,
            ConfigurationStatus = metadata?.AuthorizedStatus ?? "unknown",
            FirstObservedAt = observation.FirstObservedAt,
            LastObservedAt = observation.LastObservedAt,
            RunOverlapStart = candidate.OverlapStart,
            RunOverlapEnd = candidate.OverlapEnd,
            SampleEvidenceStatus = status,
            SampleFrameCount = includeCounts && frameCount <= int.MaxValue ? (int)frameCount : 0,
            RequiredDataItemCodes = required,
            ObservedDataItemCodes = observed,
            NotObservedRequiredDataItemCodes = notObserved
        };
    }

    private static SubjectMatch MatchRunSubject(
        IngestionTaskCoverageMetadata metadata,
        ReliabilityRunImpact impact)
    {
        var expectedType = Normalize(metadata.SubjectType);
        var expectedId = Normalize(metadata.SubjectId);
        var runType = Normalize(impact.SubjectType);
        var runId = Normalize(impact.SubjectId);
        if (string.Equals(expectedType, "equipment", StringComparison.OrdinalIgnoreCase))
        {
            runType ??= "equipment";
            runId ??= Normalize(impact.EquipmentId);
        }
        if (expectedType is null || expectedId is null || runType is null || runId is null)
            return SubjectMatch.Unknown;
        return string.Equals(expectedType, runType, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(expectedId, runId, StringComparison.OrdinalIgnoreCase)
            ? SubjectMatch.Match
            : SubjectMatch.Mismatch;
    }

    private static RunTimeSpan? ResolveRunTimeSpan(
        ReliabilityRunImpact impact,
        DateTimeOffset? queryTo,
        DateTimeOffset generatedAt)
    {
        var start = impact.StartedAt ?? impact.CompletedAt;
        if (!start.HasValue || start.Value == default)
            return null;
        var end = impact.CompletedAt ?? (impact.HasStarted ? queryTo ?? generatedAt : start.Value);
        return end < start.Value ? null : new RunTimeSpan(start.Value, end);
    }

    private sealed record RunObservationCandidate(
        ReliabilityRunImpact Impact,
        EdgeTaskDeploymentObservation Observation,
        DateTimeOffset OverlapStart,
        DateTimeOffset OverlapEnd);

    private sealed record RunTimeSpan(DateTimeOffset Start, DateTimeOffset End);

    private sealed record CoverageProjection(
        List<ReliabilityRunImpact> RunImpacts,
        bool HistoryTruncated,
        bool ResultTruncated);

    private enum SubjectMatch
    {
        Match,
        Mismatch,
        Unknown
    }

    private readonly record struct TaskVersionKey
    {
        public string TaskId { get; }

        public int Version { get; }

        public TaskVersionKey(string taskId, int version)
        {
            TaskId = taskId.Trim().ToLowerInvariant();
            Version = version;
        }
    }

    private static double? Maximum(IEnumerable<double?> values)
    {
        var present = values.Where(static value => value.HasValue)
            .Select(static value => value!.Value)
            .ToArray();
        return present.Length == 0 ? null : present.Max();
    }

    private async Task<IReadOnlyList<PlatformProductionEvent>> QueryAllAsync(
        PlatformEventQuery query,
        CancellationToken ct)
    {
        var result = new List<PlatformProductionEvent>();
        var cursor = 0L;
        while (true)
        {
            var page = await events.QueryAsync(query with { AfterIngestId = cursor, Limit = 500 }, ct)
                .ConfigureAwait(false);
            if (page.Count == 0)
                break;
            if (result.Count + page.Count > MaximumScannedEvents)
            {
                throw new InspectionQueryLimitExceededException(
                    $"数据可靠性基线超过 {MaximumScannedEvents} 条事件扫描上限，请缩小站点或时间范围。");
            }
            result.AddRange(page);
            var next = page.Max(static item => item.IngestId);
            if (next <= cursor)
                throw new InvalidOperationException("数据可靠性基线查询游标没有前进。");
            cursor = next;
            if (page.Count < 500)
                break;
        }
        return result;
    }

    private static bool HasActualParameters(ExecutionComparisonRow row)
        => row.ControlParameters.Count > 0;

    private IReadOnlyList<string> AdmissionExclusionCodes(ExecutionComparisonRow row)
    {
        var reasons = new List<string>(AdmissionExclusionReasons.Length);
        if (!row.LifecycleComplete)
            reasons.Add("lifecycle_incomplete");
        if (row.ProcessDataQuality.Status != ProcessDataStatuses.Available)
            reasons.Add("process_data_incomplete");
        if (!HasActualParameters(row))
            reasons.Add("actual_parameters_missing");
        if (!HasCompleteActualParameterUnits(row))
            reasons.Add("parameter_unit_missing");
        if (!_contextAdmission.Evaluate(row.Context, null).Admitted)
            reasons.Add("context_capture_invalid");
        if (!HasMinimalContext(row))
            reasons.Add("minimal_context_missing");
        if (row.InspectionOutcomes.Count == 0)
            reasons.Add("quality_unlinked");
        return reasons;
    }

    private static bool HasCompleteActualParameterUnits(ExecutionComparisonRow row)
        => HasActualParameters(row) &&
           row.ControlParameters.All(static value => !string.IsNullOrWhiteSpace(value.Unit));

    private bool HasMinimalContext(ExecutionComparisonRow row)
        => _contextAdmission.Evaluate(row.Context, null).Admitted &&
           RequiredContextFields.All(field => HasContext(row, field));

    private static bool HasContext(ExecutionComparisonRow row, string field)
    {
        if (string.Equals(field, "material_lot_ref", StringComparison.Ordinal))
        {
            return !string.IsNullOrWhiteSpace(
                       ProcessAnalysisResolver.ContextValue(row.Context, field)) ||
                   !string.IsNullOrWhiteSpace(
                       ProcessAnalysisResolver.ContextValue(row.Context, "material_lot"));
        }
        return !string.IsNullOrWhiteSpace(
            ProcessAnalysisResolver.ContextValue(row.Context, field));
    }

    private static IReadOnlyList<ContextFactorSummary> BuildFactorSummaries(
        IReadOnlyList<ExecutionComparisonRow> rows)
        => ContextFactors.Select(factor =>
        {
            var populated = rows
                .Select(row => (Row: row, Value: ResolveFactor(row, factor.Field)))
                .Where(static item => item.Value is not null)
                .ToArray();
            var levels = populated
                .GroupBy(static item => item.Value!, StringComparer.Ordinal)
                .Select(group => BuildFactorLevel(group.Key, group.Select(static item => item.Row).ToArray()))
                .OrderByDescending(static item => item.RunCount)
                .ThenBy(static item => item.Value, StringComparer.Ordinal)
                .ToArray();
            return new ContextFactorSummary
            {
                Field = factor.Field,
                Name = factor.Name,
                PresentRunCount = populated.Length,
                MissingRunCount = rows.Count - populated.Length,
                DistinctLevelCount = levels.Length,
                Coverage = Divide(populated.Length, rows.Count),
                LevelsTruncated = levels.Length > MaximumFactorLevels,
                Levels = levels.Take(MaximumFactorLevels).ToArray()
            };
        }).ToArray();

    private static ContextFactorLevelSummary BuildFactorLevel(
        string value,
        IReadOnlyList<ExecutionComparisonRow> rows)
    {
        var durations = rows
            .Where(static row => row.CompletedAt.HasValue && row.CompletedAt > row.StartedAt)
            .Select(static row => (row.CompletedAt!.Value - row.StartedAt).TotalMilliseconds)
            .ToArray();
        return new ContextFactorLevelSummary
        {
            Value = value,
            RunCount = rows.Count,
            ProcessCompleteRunCount = rows.Count(static row =>
                row.ProcessDataQuality.Status == ProcessDataStatuses.Available),
            QualityLinkedRunCount = rows.Count(static row => row.InspectionOutcomes.Count > 0),
            PassRunCount = rows.Count(static row => QualityOutcome(row) == "PASS"),
            FailRunCount = rows.Count(static row => QualityOutcome(row) == "FAIL"),
            InconclusiveRunCount = rows.Count(static row => QualityOutcome(row) == "INCONCLUSIVE"),
            MeanDurationMs = durations.Length == 0 ? null : durations.Average()
        };
    }

    private static IReadOnlyList<ContextFactorOverlap> BuildFactorOverlaps(
        IReadOnlyList<ExecutionComparisonRow> rows)
    {
        var result = new List<ContextFactorOverlap>();
        for (var leftIndex = 0; leftIndex < ContextFactors.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < ContextFactors.Length; rightIndex++)
            {
                var left = ContextFactors[leftIndex].Field;
                var right = ContextFactors[rightIndex].Field;
                var pairs = rows.Select(row => (
                        Left: ResolveFactor(row, left),
                        Right: ResolveFactor(row, right)))
                    .Where(static pair => pair.Left is not null && pair.Right is not null)
                    .Select(static pair => (Left: pair.Left!, Right: pair.Right!))
                    .ToArray();
                var leftLevels = pairs.Select(static pair => pair.Left).Distinct(StringComparer.Ordinal).ToArray();
                var rightLevels = pairs.Select(static pair => pair.Right).Distinct(StringComparer.Ordinal).ToArray();
                var observed = pairs.Distinct().ToArray();
                var possible = leftLevels.Length * rightLevels.Length;
                var leftNested = leftLevels.Length > 0 && pairs
                    .GroupBy(static pair => pair.Left, StringComparer.Ordinal)
                    .All(static group => group.Select(static pair => pair.Right)
                        .Distinct(StringComparer.Ordinal).Count() == 1);
                var rightNested = rightLevels.Length > 0 && pairs
                    .GroupBy(static pair => pair.Right, StringComparer.Ordinal)
                    .All(static group => group.Select(static pair => pair.Left)
                        .Distinct(StringComparer.Ordinal).Count() == 1);
                var identifiability = leftLevels.Length < 2 || rightLevels.Length < 2
                    ? "insufficient_levels"
                    : leftNested || rightNested
                        ? "confounded"
                        : observed.Length == possible
                            ? "overlapping"
                            : "limited";
                result.Add(new ContextFactorOverlap
                {
                    LeftField = left,
                    RightField = right,
                    JointRunCount = pairs.Length,
                    LeftLevelCount = leftLevels.Length,
                    RightLevelCount = rightLevels.Length,
                    ObservedCombinationCount = observed.Length,
                    PossibleCombinationCount = possible,
                    OverlapRate = Divide(observed.Length, possible),
                    Identifiability = identifiability
                });
            }
        }
        return result;
    }

    private static string? ResolveFactor(ExecutionComparisonRow row, string field)
    {
        var value = field switch
        {
            "equipment_id" => Normalize(ProcessAnalysisResolver.ContextValue(
                                      row.Context, "equipment_id")) ?? Normalize(row.EquipmentId),
            "tooling_assembly_id" => ProcessAnalysisResolver.ContextValue(
                                row.Context, "tooling_assembly_id") ??
                            ProcessAnalysisResolver.ContextValue(row.Context, "tooling_assembly_id"),
            "material_lot_ref" => ProcessAnalysisResolver.ContextValue(
                                      row.Context, "material_lot_ref") ??
                                  ProcessAnalysisResolver.ContextValue(row.Context, "material_lot"),
            _ => ProcessAnalysisResolver.ContextValue(row.Context, field)
        };
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? QualityOutcome(ExecutionComparisonRow row)
        => row.InspectionOutcomes.Contains("FAIL", StringComparer.OrdinalIgnoreCase)
            ? "FAIL"
            : row.InspectionOutcomes.Contains("INCONCLUSIVE", StringComparer.OrdinalIgnoreCase)
                ? "INCONCLUSIVE"
                : row.InspectionOutcomes.Contains("PASS", StringComparer.OrdinalIgnoreCase)
                    ? "PASS"
                    : null;

    private static ReliabilityRate Rate(
        string code,
        string name,
        int numerator,
        int denominator,
        string definition)
        => new()
        {
            Code = code,
            Name = name,
            Numerator = numerator,
            Denominator = denominator,
            Rate = Divide(numerator, denominator),
            Definition = definition
        };

    private static ReliabilityExclusionCount Exclusion(string code, string name, int count)
        => new() { Code = code, Name = name, RunCount = count };

    private static double? Divide(int numerator, int denominator)
        => denominator == 0 ? null : (double)numerator / denominator;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
