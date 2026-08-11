using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

/// <summary>
///     将过程执行比较的确定性计算结果整理成稳定的调查契约。这里不调用语言模型，
///     也不把观察性关联提升为因果结论。
/// </summary>
public sealed class ExecutionInvestigationReportBuilder
{
    public ExecutionInvestigationReport Build(
        ExecutionComparisonRow target,
        IReadOnlyList<ExecutionComparisonRow> historical,
        IReadOnlyList<ProcessSignalComparison> signalComparisons,
        ExecutionDiagnosisSummary diagnosis,
        ExecutionComparisonAcceptance acceptance,
        IReadOnlyList<string> comparisonKeys)
    {
        var matchingContext = comparisonKeys
            .Select(key => (Key: key, Value: ProcessAnalysisResolver.ContextValue(target.Context, key)))
            .Where(static item => !string.IsNullOrWhiteSpace(item.Value))
            .ToDictionary(static item => item.Key, static item => item.Value!, StringComparer.Ordinal);
        var firstDeviations = signalComparisons
            .Where(static item => item.RobustDeviation.HasValue &&
                                  Math.Abs(item.RobustDeviation.Value) >= 2)
            .Select(item =>
            {
                var feature = target.Signals
                    .FirstOrDefault(signal => signal.Code == item.SignalCode)?
                    .Features.FirstOrDefault(value =>
                        value.Code == item.FeatureCode &&
                        value.PhaseCode == item.PhaseCode &&
                        value.PhaseOrder == item.PhaseOrder);
                return new ExecutionFirstDeviation
                {
                    SignalCode = item.SignalCode,
                    FeatureCode = item.FeatureCode,
                    PhaseCode = item.PhaseCode,
                    PhaseName = item.PhaseName,
                    PhaseOrder = item.PhaseOrder,
                    StartedAt = feature?.StartedAt,
                    TargetValue = item.BaselineValue,
                    HistoricalMedian = item.HistoricalMedian,
                    RobustDeviation = item.RobustDeviation
                };
            })
            .OrderBy(static item => item.StartedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(static item => item.PhaseOrder ?? int.MaxValue)
            .ThenByDescending(static item => Math.Abs(item.RobustDeviation ?? 0))
            .Take(10)
            .ToArray();
        var candidates = diagnosis.Candidates.Take(10).ToArray();
        var confounders = candidates.SelectMany(static item => item.PossibleConfounders)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new ExecutionInvestigationReport
        {
            Status = ResolveStatus(target, historical, diagnosis),
            TargetProcessExecutionId = target.ExecutionId,
            DataQuality = new ExecutionInvestigationDataQuality
            {
                TargetStatus = target.ProcessDataQuality.Status,
                TargetEvidenceWeight = target.EvidenceWeight,
                AvailableComparisonProcessExecutions = historical.Count(static row =>
                    row.ProcessDataQuality.Status == ProcessDataStatuses.Available),
                DegradedComparisonProcessExecutions = historical.Count(static row =>
                    row.ProcessDataQuality.Status == ProcessDataStatuses.Degraded),
                UnavailableComparisonProcessExecutions = historical.Count(static row =>
                    row.ProcessDataQuality.Status == ProcessDataStatuses.Unavailable),
                Issues = BuildQualityIssues(target)
            },
            ComparisonBaseline = new ExecutionInvestigationBaseline
            {
                ComparisonProcessExecutionIds = historical.Select(static row => row.ExecutionId).ToArray(),
                MatchingContext = matchingContext,
                CompleteProcessExecutionCount = acceptance.CompleteProcessExecutionCount,
                QualityLinkedProcessExecutionCount = acceptance.QualityLinkedProcessExecutionCount,
                EffectiveProcessExecutionWeight = acceptance.EffectiveProcessExecutionWeight
            },
            FirstDeviations = firstDeviations,
            CandidateCauses = candidates,
            CounterEvidence = BuildCounterEvidence(candidates),
            Confounders = confounders,
            MissingData = BuildMissingData(target, historical, firstDeviations, candidates),
            NextExperiments = BuildExperiments(candidates, confounders)
        };
    }

    private static string ResolveStatus(
        ExecutionComparisonRow target,
        IReadOnlyList<ExecutionComparisonRow> historical,
        ExecutionDiagnosisSummary diagnosis)
    {
        if (target.ProcessDataQuality.Status == ProcessDataStatuses.Unavailable ||
            historical.Count == 0 || diagnosis.EvidenceLevel == "insufficient")
            return "insufficient";
        return diagnosis.EvidenceLevel == "stable" ? "ready" : "exploratory";
    }

    private static IReadOnlyList<string> BuildQualityIssues(ExecutionComparisonRow target)
    {
        var issues = target.ProcessDataQuality.Issues.ToList();
        if (!target.LifecycleComplete)
            issues.Add("目标过程执行生命过程执行记录不完整。");
        if (target.InspectionOutcomes.Count == 0)
            issues.Add("目标过程执行尚未关联有效检验结果。");
        if (string.IsNullOrWhiteSpace(target.AnalysisMaterialization.SourceContentHash))
            issues.Add("目标过程执行分析缺少精确原始事件内容哈希。");
        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<ExecutionCounterEvidence> BuildCounterEvidence(
        IReadOnlyList<ExecutionCauseCandidate> candidates)
    {
        var result = new List<ExecutionCounterEvidence>();
        foreach (var candidate in candidates)
        {
            result.Add(new ExecutionCounterEvidence
            {
                CandidateId = candidate.CandidateId,
                Kind = "observational-only",
                Statement = "当前证据来自合格/不合格组观察性差异，尚未执行受控干预。"
            });
            if (candidate.PossibleConfounders.Count > 0)
            {
                result.Add(new ExecutionCounterEvidence
                {
                    CandidateId = candidate.CandidateId,
                    Kind = "confounding",
                    Statement = $"该候选仍可能受 {string.Join("、", candidate.PossibleConfounders)} 影响。"
                });
            }
            if (candidate.Actionability == ExecutionCauseActionability.Observable)
            {
                result.Add(new ExecutionCounterEvidence
                {
                    CandidateId = candidate.CandidateId,
                    Kind = "not-directly-controllable",
                    Statement = "该变量是过程响应，不是可直接设定的工艺变量。"
                });
            }
            if (candidate.StabilitySelectionRate is < ProcessExecutionAnalysisThresholds.HighStabilitySelectionRate)
            {
                result.Add(new ExecutionCounterEvidence
                {
                    CandidateId = candidate.CandidateId,
                    Kind = "unstable-selection",
                    Statement = "该候选在重采样或多变量调整中的选择稳定性不足。"
                });
            }
        }
        return result;
    }

    private static IReadOnlyList<string> BuildMissingData(
        ExecutionComparisonRow target,
        IReadOnlyList<ExecutionComparisonRow> historical,
        IReadOnlyList<ExecutionFirstDeviation> deviations,
        IReadOnlyList<ExecutionCauseCandidate> candidates)
    {
        var missing = new List<string>();
        if (target.ControlParameters.Count == 0)
            missing.Add("目标过程执行没有可核对的实际控制参数快照。");
        if (target.InspectionOutcomes.Count == 0)
            missing.Add("目标过程执行没有有效检验结果。");
        var unlinked = historical.Count(static row => row.InspectionOutcomes.Count == 0);
        if (unlinked > 0)
            missing.Add($"对比基线中有 {unlinked} 个过程执行未关联检验结果。");
        if (deviations.Count == 0)
            missing.Add("当前阶段特征不足以定位稳健偏离首次出现的位置。");
        if (candidates.Count == 0)
            missing.Add("当前样本没有形成可排序的候选原因。");
        if (candidates.Count > 0 && candidates.All(static item =>
                item.Actionability != ExecutionCauseActionability.Controllable))
            missing.Add("候选原因尚未映射到可直接干预的工艺变量。");
        return missing;
    }

    private static IReadOnlyList<ExecutionValidationExperiment> BuildExperiments(
        IReadOnlyList<ExecutionCauseCandidate> candidates,
        IReadOnlyList<string> confounders)
        => candidates
            .Where(static item => item.Actionability == ExecutionCauseActionability.Controllable)
            .Take(3)
            .Select(candidate => new ExecutionValidationExperiment
            {
                CandidateId = candidate.CandidateId,
                VariableCode = candidate.VariableCode,
                DataSource = candidate.DataSource,
                BlockingFactors = confounders,
                Rationale = $"固定其余可控参数，在至少两个区组内重复比较 {candidate.DisplayName} 的两个安全水平；" +
                            "以独立检验结果判断该候选是否得到支持。"
            })
            .ToArray();
}
