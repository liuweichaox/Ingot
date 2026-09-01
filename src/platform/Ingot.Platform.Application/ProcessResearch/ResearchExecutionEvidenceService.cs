// 将达到候选排序门槛的运行对比固化为待验证研发证据，不提升探索性关联。
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessExecutions;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>
/// 在样本外验证和稳定性门槛通过后，将运行对比候选转换为研发假设或历史观察。
/// </summary>
public sealed class ResearchExecutionEvidenceService(
    IProcessResearchStore store,
    ProcessResearchWorkflow workflow,
    IExecutionComparisonService executionComparisons)
{
    public async Task<IReadOnlyList<ResearchHypothesis>> ProposeHypothesesAsync(
        Guid projectId,
        ResearchHypothesisFromExecutionComparisonRequest request,
        string userId,
        CancellationToken ct)
    {
        var baselineId = request.BaselineProcessExecutionId?.Trim();
        var executionIds = request.ProcessExecutionIds
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(baselineId) || executionIds.Length < 2 ||
            !executionIds.Contains(baselineId, StringComparer.Ordinal) ||
            request.MaximumHypotheses is < 1 or > 10)
        {
            throw new ProcessResearchRuleException(
                "请选择包含基准过程执行的至少两个过程执行，并指定 1 到 10 条候选假设。");
        }

        var project = await RequireProjectAsync(projectId, ct).ConfigureAwait(false);
        var siteId = RequireSiteCode(project);
        var comparison = await executionComparisons.CompareSelectedAsync(
                baselineId, executionIds, ct, siteId)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("所选过程执行不存在，无法形成追因证据。");
        if (!string.Equals(
                comparison.Diagnosis.Readiness.Mode,
                "candidate-ranking",
                StringComparison.Ordinal) ||
            comparison.Diagnosis.CrossValidationScore is not > 0)
        {
            throw new ProcessResearchRuleException(
                "当前运行对比仅形成探索性证据，不能批量生成候选假设。请补充质量结果、重复运行和上下文变量，待样本外验证通过后重试。");
        }
        var contentHash = Convert.ToHexStringLower(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(comparison)));
        var evidence = new EvidenceReference
        {
            EvidenceId = Guid.CreateVersion7(),
            ProjectId = projectId,
            Kind = EvidenceKinds.ExecutionComparison,
            ReferenceId = $"{comparison.BaselineProcessExecutionId}:{contentHash[..16]}",
            Summary = $"过程执行比较：{comparison.BaselineProcessExecutionId} 与 {comparison.HistoricalProcessExecutions.Count} 条历史过程执行。",
            ContentHash = contentHash,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var candidates = comparison.Diagnosis.Candidates
            .Where(static value => value.EvidenceLevel == "stable")
            .Select(candidate => new
            {
                Candidate = candidate,
                VariableCodes = ResolveControllableVariables(project, candidate)
            })
            .Where(static value => value.VariableCodes.Count > 0)
            .OrderByDescending(static value => value.Candidate.CandidateScore)
            .Take(request.MaximumHypotheses)
            .ToArray();
        if (candidates.Length == 0)
        {
            throw new ProcessResearchRuleException(
                "比较结果没有与项目可控变量数据来源匹配的候选原因；请检查项目变量的实际数据来源映射。");
        }

        var objective = project.Objectives
            .OrderByDescending(static value => value.Weight)
            .ThenBy(static value => value.Code, StringComparer.Ordinal)
            .FirstOrDefault();
        var created = new List<ResearchHypothesis>(candidates.Length);
        foreach (var resolved in candidates)
        {
            var candidate = resolved.Candidate;
            var sourceLabel = candidate.SourceKind == ExecutionCauseSourceKinds.ProcessSpecificationParameter
                ? "实际控制参数"
                : "过程轨迹特征";
            var direction = candidate.MedianDifference is > 0
                ? "不合格组更高"
                : candidate.MedianDifference is < 0 ? "不合格组更低" : "组间存在差异";
            var confoundingPenalty = candidate.PossibleConfounders.Count == 0 ? 0d : 0.15d;
            created.Add(await workflow.SaveHypothesisAsync(
                projectId,
                new ResearchHypothesis
                {
                    Statement = $"{candidate.DisplayName} 的差异可能影响项目质量目标。",
                    Rationale =
                        $"{sourceLabel}在合格与不合格过程执行间表现为“{direction}”，" +
                        $"诊断证据为 {candidate.EvidenceLevel}，候选分数 {candidate.CandidateScore:F3}。" +
                    "该结论只是观察性关联，需要持续收集后续生产运行证据。",
                    VariableCodes = resolved.VariableCodes,
                    ValidationOutcomeCode = objective?.Code,
                    ExpectedEffectDirection = objective is null ? null : ResolveValidationDirection(objective),
                    MinimumEffect = objective is null ? null : ResolveMinimumEffect(objective),
                    PossibleConfounders = candidate.PossibleConfounders,
                    Confidence = Math.Max(
                        0.2d,
                        (candidate.EvidenceLevel == "stable" ? 0.65d : 0.4d) - confoundingPenalty),
                    SupportingEvidence = [evidence with { EvidenceId = Guid.CreateVersion7() }],
                    FalsificationConditions =
                    [
                        "后续同条件重复比较未再观察到该变量差异与结果差异同向出现。"
                    ],
                    Applicability =
                        $"产品系列：{comparison.ProductFamilyCode}；分析范围：{comparison.AnalysisScope}；" +
                        $"数据来源：{candidate.DataSource}。"
                },
                userId,
                ct).ConfigureAwait(false));
        }
        return created;
    }

    private async Task<ResearchProject> RequireProjectAsync(Guid projectId, CancellationToken ct)
        => await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
           ?? throw new ProcessResearchRuleException("研发项目不存在。");

    private static string RequireSiteCode(ResearchProject project)
        => string.IsNullOrWhiteSpace(project.SiteCode)
            ? throw new ProcessResearchRuleException("研发项目必须绑定站点后才能读取生产运行证据。")
            : project.SiteCode.Trim();

    private static IReadOnlyList<string> ResolveControllableVariables(
        ResearchProject project,
        ExecutionCauseCandidate candidate)
        => project.Variables
            .Where(static variable => variable.Role == ResearchVariableRoles.Control)
            .Where(variable =>
            {
                var source = variable.DataSource?.Trim();
                if (!string.IsNullOrWhiteSpace(source))
                    return string.Equals(source, candidate.DataSource, StringComparison.OrdinalIgnoreCase);
                return candidate.SourceKind == ExecutionCauseSourceKinds.ProcessSpecificationParameter &&
                       string.Equals(variable.Code, candidate.VariableCode, StringComparison.Ordinal);
            })
            .Select(static variable => variable.Code)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string ResolveValidationDirection(ResearchObjective objective)
        => objective.Direction.ToLowerInvariant() switch
        {
            "maximize" or "max" or "increase" => ResearchHypothesisEffectDirections.Increase,
            "minimize" or "min" or "decrease" => ResearchHypothesisEffectDirections.Decrease,
            _ when objective.Baseline is { } baseline && baseline < objective.Target =>
                ResearchHypothesisEffectDirections.Increase,
            _ => ResearchHypothesisEffectDirections.Decrease
        };

    private static double ResolveMinimumEffect(ResearchObjective objective)
    {
        if (objective.Baseline is { } baseline && Math.Abs(baseline - objective.Target) > 1e-12)
            return Math.Max(Math.Abs(baseline - objective.Target) * 0.1, 1e-9);
        if (objective.LowerLimit is { } lower && objective.UpperLimit is { } upper && upper > lower)
            return Math.Max((upper - lower) * 0.01, 1e-9);
        return Math.Max(Math.Abs(objective.Target) * 0.01, 0.001);
    }
}
