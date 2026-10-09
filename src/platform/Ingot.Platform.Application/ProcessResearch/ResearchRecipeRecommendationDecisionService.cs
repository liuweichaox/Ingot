// 冻结日常下一配方建议的工程师回执与实际生产结果，不生成设备控制命令。
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessExecutions;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>
/// 管理日常配方建议的工程师决策和一次性结果取证；所有校验只依据建议生成时冻结的条件。
/// </summary>
public sealed class ResearchRecipeRecommendationDecisionService(
    IProcessResearchStore store,
    IResearchObservationAssembler observationAssembler,
    IExecutionComparisonService executionComparisons)
{
    public async Task<ResearchRecipeRecommendationDecision> RecordDecisionAsync(
        Guid recommendationId,
        string recommendationKey,
        ResearchRecipeRecommendationDecisionRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var recommendation = await store.GetRecipeRecommendationAsync(recommendationId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("下一配方建议不存在。");
        var brief = RequireFrozenBrief(recommendation);

        recommendationKey = Required(recommendationKey, "建议项标识", 120);
        var item = recommendation.Items.SingleOrDefault(value => string.Equals(
            value.RecommendationKey, recommendationKey, StringComparison.Ordinal))
            ?? throw new ProcessResearchRuleException("下一配方建议项不存在。");
        var decision = Required(request.Decision, "工程师决策", 40).ToLowerInvariant();
        if (!ResearchRecipeRecommendationDecisionStatuses.IsValid(decision))
            throw new ProcessResearchRuleException("工程师决策必须是 accepted、modified 或 rejected。");
        var reason = Optional(request.Reason, 2000);
        var usefulnessRating = Optional(request.UsefulnessRating, 40)?.ToLowerInvariant();
        var decidedBy = Required(userId, "工程师", 240);
        var requestedParameters = request.EngineerSelectedParameters ?? [];
        var selected = decision == ResearchRecipeRecommendationDecisionStatuses.Rejected &&
            requestedParameters.Count == 0
                ? []
                : NormalizeSelectedParameters(brief, requestedParameters);
        var sameAsSuggestion = selected.Count > 0 && ParametersEqual(item.Parameters, selected);
        if (decision == ResearchRecipeRecommendationDecisionStatuses.Accepted && !sameAsSuggestion)
            throw new ProcessResearchRuleException("接受建议时，工程师选择必须与冻结的模型建议一致。");
        if (decision == ResearchRecipeRecommendationDecisionStatuses.Modified && sameAsSuggestion)
            throw new ProcessResearchRuleException("修改建议时，必须登记不同的工程师实际选择。");
        if (decision == ResearchRecipeRecommendationDecisionStatuses.Rejected && sameAsSuggestion)
            throw new ProcessResearchRuleException("拒绝建议时无需登记原建议参数；如登记替代参数，必须与建议不同。");
        if (decision != ResearchRecipeRecommendationDecisionStatuses.Accepted && reason is null)
            throw new ProcessResearchRuleException("修改或拒绝建议时必须说明原因。");
        if (usefulnessRating is not null && !ResearchUsefulnessRatings.IsValid(usefulnessRating))
            throw new ProcessResearchRuleException(
                "工程师有用性评分必须是 useful、partly-useful 或 not-useful。");

        ValidateHardBoundaries(brief, item.Parameters, "模型建议");
        if (selected.Count > 0)
            ValidateHardBoundaries(brief, selected, "工程师选择");
        var now = DateTimeOffset.UtcNow;
        var snapshotHash = Hash(new
        {
            recommendation.RecommendationId,
            recommendation.BriefHash,
            recommendation.ModelVersion,
            recommendation.InputHash,
            recommendationKey,
            SuggestedParameters = item.Parameters.OrderBy(static value => value.VariableCode),
            item.Prediction,
            decision,
            EngineerSelectedParameters = selected.OrderBy(static value => value.VariableCode),
            reason,
            usefulnessRating,
            decidedBy
        });
        if (await store.GetRecipeRecommendationDecisionByItemAsync(
                recommendationId, recommendationKey, ct).ConfigureAwait(false) is { } existing)
            return ExactRetryOrConflict(existing, snapshotHash);
        if (recommendation.ExpiresAt is { } expiresAt && expiresAt <= now)
            throw new ProcessResearchRuleException("该配方建议已过期，请重新生成建议后再登记工程师决定。");
        var value = new ResearchRecipeRecommendationDecision
        {
            DecisionId = Guid.CreateVersion7(),
            RecommendationId = recommendation.RecommendationId,
            RecommendationKey = recommendationKey,
            SiteCode = recommendation.SiteCode,
            ProcessSpecificationId = recommendation.ProcessSpecificationId,
            BriefHash = recommendation.BriefHash,
            Decision = decision,
            ActualExecutionKey = null,
            SuggestedParameters = item.Parameters,
            EngineerSelectedParameters = selected,
            Prediction = item.Prediction,
            Reason = reason,
            UsefulnessRating = usefulnessRating,
            DecisionSnapshotHash = snapshotHash,
            DecidedBy = decidedBy,
            DecidedAt = now
        };
        try
        {
            return await store.CreateRecipeRecommendationDecisionTransactionAsync(value, null, ct)
                .ConfigureAwait(false);
        }
        catch (ProcessResearchRuleException)
        {
            var concurrent = await store.GetRecipeRecommendationDecisionByItemAsync(
                recommendationId, recommendationKey, ct).ConfigureAwait(false);
            if (concurrent is not null)
                return ExactRetryOrConflict(concurrent, snapshotHash);
            throw;
        }
    }

    public async Task<ResearchRecipeRecommendationDecision> LinkActualExecutionAsync(
        Guid decisionId,
        ResearchRecipeRecommendationExecutionLinkRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var decision = await store.GetRecipeRecommendationDecisionAsync(decisionId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("日常配方建议决策不存在。");
        var actualExecutionKey = Required(request.ActualExecutionKey, "工程师实际运行标识", 120);
        if (string.Equals(decision.ActualExecutionKey, actualExecutionKey, StringComparison.Ordinal))
            return decision;
        if (decision.Outcome is not null)
            throw new ProcessResearchRuleException("实际结果已冻结，不能重新关联实际运行。");
        if (decision.Decision == ResearchRecipeRecommendationDecisionStatuses.Rejected)
            throw new ProcessResearchRuleException("已拒绝的建议是终态，不能关联实际运行。");
        var brief = await RequireDecisionBriefAsync(decision, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(decision.ActualExecutionKey))
            throw new ProcessResearchRuleException("该工程师决定已经关联了其他实际运行，不能覆盖。");
        await RequireExecutionAsync(brief, actualExecutionKey, decision.DecidedAt, ct).ConfigureAwait(false);
        return await store.LinkRecipeRecommendationDecisionExecutionTransactionAsync(
                decisionId,
                actualExecutionKey,
                Required(userId, "操作人", 240),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<ResearchRecipeRecommendationDecision> MaterializeOutcomeAsync(
        Guid decisionId,
        string userId,
        CancellationToken ct = default)
    {
        var decision = await store.GetRecipeRecommendationDecisionAsync(decisionId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("日常配方建议决策不存在。");
        if (decision.Outcome is not null)
            return decision;
        if (decision.Decision == ResearchRecipeRecommendationDecisionStatuses.Rejected)
            throw new ProcessResearchRuleException("已拒绝的建议是终态，不产生运行结果证据。");
        var brief = await RequireDecisionBriefAsync(decision, ct).ConfigureAwait(false);

        var actualExecutionKey = Required(decision.ActualExecutionKey, "已关联实际运行标识", 120);
        var execution = await RequireExecutionAsync(brief, actualExecutionKey, decision.DecidedAt, ct)
            .ConfigureAwait(false);
        if (!execution.HasCompleted || !execution.LifecycleComplete || execution.CompletedAt is null)
            throw new ProcessResearchRuleException("实际运行尚未完成，不能冻结日常建议结果。");
        var assembly = await observationAssembler.AssembleProductionRunAsync(
            brief, actualExecutionKey, ct).ConfigureAwait(false);
        var observation = assembly.Observations.SingleOrDefault()
            ?? throw new ProcessResearchRuleException(
                "实际运行尚未形成可关联的完整过程执行，不能冻结日常建议结果。");
        var actualControlCodes = observation.ActualFactors
            .Select(static value => value.VariableCode)
            .ToHashSet(StringComparer.Ordinal);
        RequireAll(
            brief.Variables.Where(static value => value.Role == ResearchVariableRoles.Control)
                .Select(static value => value.Code),
            actualControlCodes.Contains,
            "实际运行缺少完整参数回读");
        RequireAll(
            brief.Objectives.Select(static value => value.Code),
            observation.Outcomes.ContainsKey,
            "实际运行尚未形成完整质量结果");
        RequireAll(
            brief.OutcomeConstraints.Select(static value => value.Code),
            observation.ConstraintOutcomes.ContainsKey,
            "实际运行尚未形成完整结果约束");
        if (observation.ProcessFeatures.Count == 0)
            throw new ProcessResearchRuleException("实际运行尚未形成过程特征，不能冻结日常建议结果。");
        var outcome = new ResearchRecipeRecommendationOutcome
        {
            BriefHash = decision.BriefHash,
            ActualExecutionKey = actualExecutionKey,
            ActualParameters = observation.ActualFactors,
            SettingDeviationFromSuggestion = Differences(
                decision.SuggestedParameters, observation.ActualFactors),
            SettingDeviationFromEngineerSelection = Differences(
                decision.EngineerSelectedParameters, observation.ActualFactors),
            ProcessFeatures = observation.ProcessFeatures,
            Outcomes = observation.Outcomes,
            ConstraintOutcomes = observation.ConstraintOutcomes,
            ActualContextSnapshot = observation.Context,
            ValidForOptimization = observation.ValidForOptimization,
            ExclusionReason = observation.ExclusionReason,
            SourceContentHash = observation.SourceContentHash,
            CapturedAt = DateTimeOffset.UtcNow
        };
        return await store.AttachRecipeRecommendationOutcomeTransactionAsync(
            decision.DecisionId, outcome, Required(userId, "操作人", 240), ct).ConfigureAwait(false);
    }

    private async Task<RecipeRecommendationBrief> RequireDecisionBriefAsync(
        ResearchRecipeRecommendationDecision decision,
        CancellationToken ct)
    {
        var recommendation = await store.GetRecipeRecommendationAsync(decision.RecommendationId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("日常配方建议不存在。");
        var brief = RequireFrozenBrief(recommendation);
        if (!string.Equals(decision.BriefHash, recommendation.BriefHash, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("工程师决定与建议的冻结条件不一致。");
        return brief;
    }

    private static RecipeRecommendationBrief RequireFrozenBrief(ResearchRecipeRecommendation recommendation)
    {
        if (!string.Equals(
                RecipeRecommendationBriefPolicy.Hash(recommendation.Brief),
                recommendation.BriefHash,
                StringComparison.Ordinal) ||
            !string.Equals(recommendation.Brief.SiteCode, recommendation.SiteCode, StringComparison.Ordinal) ||
            !string.Equals(
                recommendation.Brief.ProcessSpecificationId,
                recommendation.ProcessSpecificationId,
                StringComparison.Ordinal))
            throw new ProcessResearchRuleException("下一配方建议的冻结条件校验失败。");
        return recommendation.Brief;
    }

    private async Task<ExecutionComparisonRow> RequireExecutionAsync(
        RecipeRecommendationBrief brief,
        string executionKey,
        DateTimeOffset decidedAt,
        CancellationToken ct)
    {
        var execution = await executionComparisons.GetProcessExecutionAsync(
                executionKey, ct, brief.SiteCode).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("实际运行不存在或不在建议所属站点。");
        if (!execution.HasStarted)
            throw new ProcessResearchRuleException("实际运行尚未开始，不能关联到工程师决定。");
        if (execution.StartedAt <= decidedAt)
            throw new ProcessResearchRuleException("实际运行必须在工程师决定之后开始，不能事后挑选历史结果。");
        ValidateExecutionScope(brief, execution);
        return execution;
    }

    private static void ValidateExecutionScope(RecipeRecommendationBrief brief, ExecutionComparisonRow execution)
    {
        ValidateScopeValue(brief, RecipeRecommendationScopeKeys.ProductFamilyCode, execution.ProductFamilyCode, "产品族");
        ValidateScopeValue(brief, RecipeRecommendationScopeKeys.ProductCode, execution.ProductCode, "产品");
        ValidateScopeValue(brief, RecipeRecommendationScopeKeys.EquipmentId, execution.EquipmentId, "设备");
        ValidateScopeValue(brief, RecipeRecommendationScopeKeys.ProcessSpecificationId,
            execution.ProcessSpecificationId, "配方");
        ValidateScopeValue(brief, RecipeRecommendationScopeKeys.ProcessSpecificationVersion,
            execution.ProcessSpecificationVersion, "配方版本号");
        ValidateScopeValue(brief, RecipeRecommendationScopeKeys.OutputItemId, execution.OutputItemId, "产出物料");
    }

    private static void ValidateScopeValue(
        RecipeRecommendationBrief brief,
        string contextKey,
        string? actual,
        string label)
    {
        if (brief.Context.TryGetValue(contextKey, out var expected) &&
            !string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(expected.Trim(), actual?.Trim(), StringComparison.Ordinal))
            throw new ProcessResearchRuleException($"实际运行的{label}不属于建议的冻结范围。");
    }

    private static void RequireAll(IEnumerable<string> codes, Func<string, bool> present, string message)
    {
        var missing = codes.Where(code => !present(code)).ToArray();
        if (missing.Length > 0)
            throw new ProcessResearchRuleException($"{message}：{string.Join("、", missing)}。");
    }

    private static ResearchRecipeRecommendationDecision ExactRetryOrConflict(
        ResearchRecipeRecommendationDecision existing,
        string requestHash)
    {
        if (string.Equals(existing.DecisionSnapshotHash, requestHash, StringComparison.Ordinal))
            return existing;
        throw new ProcessResearchRuleException(
            "该建议项已登记不同的工程师决定；幂等重试必须与原决定、参数、原因、评分和操作人完全一致。");
    }

    private static IReadOnlyList<ResearchVariableSetting> NormalizeSelectedParameters(
        RecipeRecommendationBrief brief,
        IReadOnlyList<ResearchVariableSetting> parameters)
    {
        var controls = brief.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (!parameters.Select(static value => value.VariableCode)
                .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys) ||
            parameters.Count != controls.Count)
            throw new ProcessResearchRuleException("工程师选择必须包含且仅包含全部可调参数。");
        return parameters.Select(value =>
        {
            if (!controls.TryGetValue(value.VariableCode, out var variable) ||
                !double.IsFinite(value.Value) ||
                variable.LowerLimit is { } lower && value.Value < lower ||
                variable.UpperLimit is { } upper && value.Value > upper)
                throw new ProcessResearchRuleException($"工程师选择 {value.VariableCode} 超出建议条件的参数范围。");
            if (!string.Equals(value.Unit?.Trim(), variable.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException($"工程师选择 {value.VariableCode} 的单位不一致。");
            return value with { Unit = variable.Unit };
        }).OrderBy(static value => value.VariableCode, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateHardBoundaries(
        RecipeRecommendationBrief brief,
        IReadOnlyList<ResearchVariableSetting> parameters,
        string label)
    {
        var values = parameters.ToDictionary(static value => value.VariableCode,
            static value => value.Value, StringComparer.Ordinal);
        foreach (var constraint in brief.Constraints)
        {
            if (!values.TryGetValue(constraint.VariableCode, out var value))
                throw new ProcessResearchRuleException($"{label}缺少安全约束变量 {constraint.VariableCode}。");
            var passed = constraint.Operator switch
            {
                "<=" => value <= constraint.Limit,
                ">=" => value >= constraint.Limit,
                _ => throw new ProcessResearchRuleException($"安全约束 {constraint.Code} 的操作符无效。")
            };
            if (!passed)
                throw new ProcessResearchRuleException($"{label}违反已声明安全边界 {constraint.Code}。");
        }
    }

    private static IReadOnlyDictionary<string, double> Differences(
        IReadOnlyList<ResearchVariableSetting> expected,
        IReadOnlyList<ResearchVariableSetting> actual)
    {
        var actualValues = actual.ToDictionary(static value => value.VariableCode,
            static value => value.Value, StringComparer.Ordinal);
        return expected.Where(value => actualValues.ContainsKey(value.VariableCode))
            .ToDictionary(static value => value.VariableCode,
                value => actualValues[value.VariableCode] - value.Value,
                StringComparer.Ordinal);
    }

    private static bool ParametersEqual(
        IReadOnlyList<ResearchVariableSetting> left,
        IReadOnlyList<ResearchVariableSetting> right)
    {
        if (left.Count != right.Count)
            return false;
        var rightByCode = right.ToDictionary(static value => value.VariableCode,
            StringComparer.Ordinal);
        return left.All(value => rightByCode.TryGetValue(value.VariableCode, out var other) &&
            string.Equals(value.Unit, other.Unit, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(value.Value - other.Value) <= Math.Max(1e-9, Math.Abs(value.Value) * 1e-9));
    }

    private static string Hash<T>(T value)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private static string Required(string? value, string field, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength)
            throw new ProcessResearchRuleException($"{field}不能为空且长度不能超过 {maximumLength}。");
        return normalized;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;
        if (normalized.Length > maximumLength)
            throw new ProcessResearchRuleException($"说明长度不能超过 {maximumLength}。");
        return normalized;
    }
}
