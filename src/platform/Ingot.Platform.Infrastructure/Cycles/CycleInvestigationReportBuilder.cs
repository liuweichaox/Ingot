using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.Cycles;

/// <summary>
///     将周期比较的确定性计算结果整理成稳定的调查契约。这里不调用语言模型，
///     也不把观察性关联提升为因果结论。
/// </summary>
public sealed class CycleInvestigationReportBuilder
{
    public CycleInvestigationReport Build(
        CycleComparisonRow target,
        IReadOnlyList<CycleComparisonRow> historical,
        IReadOnlyList<CycleSignalComparison> signalComparisons,
        CycleDiagnosisSummary diagnosis,
        CycleComparisonAcceptance acceptance,
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
                return new CycleFirstDeviation
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

        return new CycleInvestigationReport
        {
            Status = ResolveStatus(target, historical, diagnosis),
            TargetCycleId = target.CorrelationId,
            DataQuality = new CycleInvestigationDataQuality
            {
                TargetStatus = target.ProcessDataQuality.Status,
                TargetEvidenceWeight = target.EvidenceWeight,
                AvailableComparisonCycles = historical.Count(static row =>
                    row.ProcessDataQuality.Status == ProcessDataStatuses.Available),
                DegradedComparisonCycles = historical.Count(static row =>
                    row.ProcessDataQuality.Status == ProcessDataStatuses.Degraded),
                UnavailableComparisonCycles = historical.Count(static row =>
                    row.ProcessDataQuality.Status == ProcessDataStatuses.Unavailable),
                Issues = BuildQualityIssues(target)
            },
            ComparisonBaseline = new CycleInvestigationBaseline
            {
                ComparisonCycleIds = historical.Select(static row => row.CorrelationId).ToArray(),
                MatchingContext = matchingContext,
                CompleteCycleCount = acceptance.CompleteCycleCount,
                QualityLinkedCycleCount = acceptance.QualityLinkedCycleCount,
                EffectiveCycleWeight = acceptance.EffectiveCycleWeight
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
        CycleComparisonRow target,
        IReadOnlyList<CycleComparisonRow> historical,
        CycleDiagnosisSummary diagnosis)
    {
        if (target.ProcessDataQuality.Status == ProcessDataStatuses.Unavailable ||
            historical.Count == 0 || diagnosis.EvidenceLevel == "insufficient")
            return "insufficient";
        return diagnosis.EvidenceLevel == "stable" ? "ready" : "exploratory";
    }

    private static IReadOnlyList<string> BuildQualityIssues(CycleComparisonRow target)
    {
        var issues = target.ProcessDataQuality.Issues.ToList();
        if (!target.LifecycleComplete)
            issues.Add("目标周期生命周期记录不完整。");
        if (target.InspectionOutcomes.Count == 0)
            issues.Add("目标周期尚未关联有效检验结果。");
        if (string.IsNullOrWhiteSpace(target.AnalysisMaterialization.SourceContentHash))
            issues.Add("目标周期分析缺少精确原始事件内容哈希。");
        return issues.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<CycleCounterEvidence> BuildCounterEvidence(
        IReadOnlyList<CycleCauseCandidate> candidates)
    {
        var result = new List<CycleCounterEvidence>();
        foreach (var candidate in candidates)
        {
            result.Add(new CycleCounterEvidence
            {
                CandidateId = candidate.CandidateId,
                Kind = "observational-only",
                Statement = "当前证据来自合格/不合格组观察性差异，尚未执行受控干预。"
            });
            if (candidate.PossibleConfounders.Count > 0)
            {
                result.Add(new CycleCounterEvidence
                {
                    CandidateId = candidate.CandidateId,
                    Kind = "confounding",
                    Statement = $"该候选仍可能受 {string.Join("、", candidate.PossibleConfounders)} 影响。"
                });
            }
            if (candidate.Actionability == CycleCauseActionability.Observable)
            {
                result.Add(new CycleCounterEvidence
                {
                    CandidateId = candidate.CandidateId,
                    Kind = "not-directly-controllable",
                    Statement = "该变量是过程响应，不是可直接设定的工艺变量。"
                });
            }
            if (candidate.StabilitySelectionRate is < 0.6)
            {
                result.Add(new CycleCounterEvidence
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
        CycleComparisonRow target,
        IReadOnlyList<CycleComparisonRow> historical,
        IReadOnlyList<CycleFirstDeviation> deviations,
        IReadOnlyList<CycleCauseCandidate> candidates)
    {
        var missing = new List<string>();
        if (target.RecipeParameters.Count == 0)
            missing.Add("目标周期没有可核对的实际配方参数快照。");
        if (target.InspectionOutcomes.Count == 0)
            missing.Add("目标周期没有有效检验结果。");
        var unlinked = historical.Count(static row => row.InspectionOutcomes.Count == 0);
        if (unlinked > 0)
            missing.Add($"对比基线中有 {unlinked} 个周期未关联检验结果。");
        if (deviations.Count == 0)
            missing.Add("当前阶段特征不足以定位稳健偏离首次出现的位置。");
        if (candidates.Count == 0)
            missing.Add("当前样本没有形成可排序的候选原因。");
        if (candidates.Count > 0 && candidates.All(static item =>
                item.Actionability != CycleCauseActionability.Controllable))
            missing.Add("候选原因尚未映射到可直接干预的工艺变量。");
        return missing;
    }

    private static IReadOnlyList<CycleValidationExperiment> BuildExperiments(
        IReadOnlyList<CycleCauseCandidate> candidates,
        IReadOnlyList<string> confounders)
        => candidates
            .Where(static item => item.Actionability == CycleCauseActionability.Controllable)
            .Take(3)
            .Select(candidate => new CycleValidationExperiment
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
