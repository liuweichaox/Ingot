
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ProcessResearch;

// 研发工作流的假设写入边界；假设仅作为建议生成的输入，不替代真实运行证据。
public sealed partial class ProcessResearchWorkflow
{
    public async Task<ResearchHypothesis> SaveHypothesisAsync(
        Guid projectId,
        ResearchHypothesis request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await RequireMutableProjectAsync(projectId, ct).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var existing = request.HypothesisId == Guid.Empty
            ? null
            : await store.GetHypothesisAsync(request.HypothesisId, ct).ConfigureAwait(false);
        if (existing is not null && existing.ProjectId != projectId)
            throw new ProcessResearchRuleException("研发假设不属于当前项目。");

        var statement = RequiredText(request.Statement, "研发假设", 4000);
        var rationale = RequiredText(request.Rationale, "假设依据", 8000);
        var variableCodes = NormalizeCodes(request.VariableCodes, "假设变量");
        var knownVariables = project.Variables.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (variableCodes.Any(code => !knownVariables.Contains(code)))
            throw new ProcessResearchRuleException("研发假设引用了项目中未定义的变量。");
        var objectiveCodes = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var validationOutcomeCode = request.ValidationOutcomeCode is null
            ? null
            : NormalizeCode(request.ValidationOutcomeCode, "假设验证目标");
        var expectedEffectDirection = request.ExpectedEffectDirection is null
            ? null
            : RequiredText(request.ExpectedEffectDirection, "预期效应方向", 40)
                .ToLowerInvariant();
        var hasValidationCriterion = validationOutcomeCode is not null ||
                                   expectedEffectDirection is not null ||
                                   request.MinimumEffect is not null;
        if (hasValidationCriterion &&
            (validationOutcomeCode is null || expectedEffectDirection is null ||
             request.MinimumEffect is not { } minimumEffect ||
             !objectiveCodes.Contains(validationOutcomeCode) ||
             !ResearchHypothesisEffectDirections.IsValid(expectedEffectDirection) ||
             !double.IsFinite(minimumEffect) || minimumEffect <= 0))
        {
            throw new ProcessResearchRuleException(
                "假设验证必须同时定义项目目标、预期效应方向和正的最小效应。");
        }
        if (!ResearchHypothesisStatuses.IsValid(request.Status))
            throw new ProcessResearchRuleException("研发假设状态无效。");
        if (request.Status == ResearchHypothesisStatuses.Validated &&
            existing?.Status != ResearchHypothesisStatuses.Validated)
            throw new ProcessResearchRuleException("已验证原因只能由跨区组重复真实运行自动确认。");
        if (request.Confidence is < 0 or > 1 || !double.IsFinite(request.Confidence))
            throw new ProcessResearchRuleException("研发假设置信度必须位于 0 到 1 之间。");
        var falsificationConditions = NormalizeTextList(request.FalsificationConditions, "反证条件", 2000);
        if (falsificationConditions.Count == 0)
            throw new ProcessResearchRuleException("研发假设至少需要一个可执行的反证条件。");

        var value = request with
        {
            HypothesisId = existing?.HypothesisId ??
                           (request.HypothesisId == Guid.Empty
                               ? Guid.CreateVersion7()
                               : request.HypothesisId),
            ProjectId = projectId,
            Statement = statement,
            Rationale = rationale,
            VariableCodes = variableCodes,
            ValidationOutcomeCode = validationOutcomeCode,
            ExpectedEffectDirection = expectedEffectDirection,
            MinimumEffect = request.MinimumEffect,
            PossibleConfounders = NormalizeTextList(request.PossibleConfounders, "可能混杂因素", 240),
            Applicability = OptionalText(request.Applicability, 8000),
            CausalChain = NormalizeCausalChain(request.CausalChain, knownVariables),
            TemporalFeatures = NormalizeTemporalFeatures(request.TemporalFeatures, knownVariables),
            Interactions = NormalizeInteractions(request.Interactions, knownVariables),
            FailureConditions = NormalizeFailureConditions(request.FailureConditions),
            FalsificationConditions = falsificationConditions,
            SupportingEvidence = NormalizeEvidence(projectId, request.SupportingEvidence),
            OpposingEvidence = NormalizeEvidence(projectId, request.OpposingEvidence),
            ValidationEvidence = NormalizeEvidence(projectId, request.ValidationEvidence),
            CreatedBy = existing?.CreatedBy ?? NormalizeUser(userId),
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        var saved = await store.SaveHypothesisAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(projectId, "hypothesis", saved.HypothesisId.ToString(),
            existing is null ? "created" : "updated", userId, existing?.Status, saved.Status, ct)
            .ConfigureAwait(false);
        return saved;
    }

    private static IReadOnlyList<ResearchHypothesisCausalLink> NormalizeCausalChain(
        IReadOnlyList<ResearchHypothesisCausalLink> values,
        IReadOnlySet<string> knownVariables)
        => values.Select(value =>
        {
            var from = NormalizeCode(value.FromVariableCode, "作用链起点变量");
            var to = NormalizeCode(value.ToVariableCode, "作用链终点变量");
            if (!knownVariables.Contains(from) || !knownVariables.Contains(to))
                throw new ProcessResearchRuleException("假设作用链引用了项目中未定义的变量。");
            if (from == to)
                throw new ProcessResearchRuleException("作用链起点和终点不能是同一变量。");
            var direction = OptionalText(value.Direction, 40)?.ToLowerInvariant();
            if (direction is not null && direction is not ("increase" or "decrease" or "nonlinear" or "unknown"))
                throw new ProcessResearchRuleException("作用链方向无效。");
            return value with
            {
                FromVariableCode = from,
                ToVariableCode = to,
                Mechanism = RequiredText(value.Mechanism, "作用机制", 2000),
                Direction = direction
            };
        }).DistinctBy(value => (value.FromVariableCode, value.ToVariableCode, value.Mechanism)).ToArray();

    private static IReadOnlyList<ResearchHypothesisTemporalFeature> NormalizeTemporalFeatures(
        IReadOnlyList<ResearchHypothesisTemporalFeature> values,
        IReadOnlySet<string> knownVariables)
        => values.Select(value =>
        {
            var variableCode = NormalizeCode(value.VariableCode, "时间特征变量");
            if (!knownVariables.Contains(variableCode))
                throw new ProcessResearchRuleException("时间特征引用了项目中未定义的变量。");
            if (value.DelayMilliseconds is < 0 || value.WindowMilliseconds is <= 0)
                throw new ProcessResearchRuleException("时间特征的时滞不能为负，窗口必须为正。");
            return value with
            {
                VariableCode = variableCode,
                FeatureCode = NormalizeCode(value.FeatureCode, "时间特征代码"),
                PhaseCode = value.PhaseCode is null ? null : NormalizeCode(value.PhaseCode, "阶段代码")
            };
        }).DistinctBy(value => (value.VariableCode, value.FeatureCode, value.PhaseCode)).ToArray();

    private static IReadOnlyList<ResearchHypothesisInteraction> NormalizeInteractions(
        IReadOnlyList<ResearchHypothesisInteraction> values,
        IReadOnlySet<string> knownVariables)
        => values.Select(value =>
        {
            var codes = NormalizeCodes(value.VariableCodes, "交互变量");
            if (codes.Count < 2 || codes.Any(code => !knownVariables.Contains(code)))
                throw new ProcessResearchRuleException("交互作用必须引用至少两个已定义项目变量。");
            return value with
            {
                VariableCodes = codes,
                Description = RequiredText(value.Description, "交互说明", 2000)
            };
        }).ToArray();

    private static IReadOnlyList<ResearchHypothesisFailureCondition> NormalizeFailureConditions(
        IReadOnlyList<ResearchHypothesisFailureCondition> values)
        => values.Select(value => value with
        {
            Condition = RequiredText(value.Condition, "失效条件", 2000),
            ObservableSignal = RequiredText(value.ObservableSignal, "失效征兆", 2000),
            RequiredResponse = RequiredText(value.RequiredResponse, "失效处置", 2000)
        }).ToArray();

}
