// 冻结日常下一配方建议的工程师回执与实际生产结果，不生成设备控制命令。
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessExecutions;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>管理日常配方建议的工程师决策和一次性结果取证。</summary>
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
        var project = await store.GetProjectAsync(recommendation.ProjectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");

        recommendationKey = Required(recommendationKey, "建议项标识", 120);
        var item = recommendation.Items.SingleOrDefault(value => string.Equals(
            value.RecommendationKey, recommendationKey, StringComparison.Ordinal))
            ?? throw new ProcessResearchRuleException("下一配方建议项不存在。");
        var decision = Required(request.Decision, "工程师决策", 40).ToLowerInvariant();
        if (!ResearchRecipeRecommendationDecisionStatuses.IsValid(decision))
            throw new ProcessResearchRuleException("工程师决策必须是 accepted、modified 或 rejected。");
        var actualExecutionKey = Optional(request.ActualExecutionKey, 120);
        var reason = Optional(request.Reason, 2000);
        var usefulnessRating = Optional(request.UsefulnessRating, 40)?.ToLowerInvariant();
        var decidedBy = Required(userId, "工程师", 240);
        var projectEvidence = ResolveRecommendationSnapshot(recommendation, project);
        var frozenProject = ResearchProjectEvidenceSnapshots.Restore(projectEvidence.Snapshot);
        var selected = decision == ResearchRecipeRecommendationDecisionStatuses.Rejected &&
            request.EngineerSelectedParameters.Count == 0
                ? []
                : NormalizeSelectedParameters(frozenProject, request.EngineerSelectedParameters);
        var sameAsSuggestion = selected.Count > 0 && ParametersEqual(item.Parameters, selected);
        if (decision == ResearchRecipeRecommendationDecisionStatuses.Accepted && !sameAsSuggestion)
            throw new ProcessResearchRuleException("接受建议时，工程师选择必须与冻结的模型建议一致。");
        if (decision == ResearchRecipeRecommendationDecisionStatuses.Modified && sameAsSuggestion)
            throw new ProcessResearchRuleException("修改建议时，必须登记不同的工程师实际选择。");
        if (decision == ResearchRecipeRecommendationDecisionStatuses.Rejected && sameAsSuggestion)
            throw new ProcessResearchRuleException("拒绝建议时无需登记原建议参数；如登记替代参数，必须与建议不同。");
        if (decision != ResearchRecipeRecommendationDecisionStatuses.Accepted && reason is null)
            throw new ProcessResearchRuleException("修改或拒绝建议时必须说明原因。");
        if (actualExecutionKey is not null)
            throw new ProcessResearchRuleException(
                "请先冻结工程师决定，再在实际运行开始后通过独立关联操作登记运行。");
        if (usefulnessRating is not null && !ResearchUsefulnessRatings.IsValid(usefulnessRating))
            throw new ProcessResearchRuleException(
                "工程师有用性评分必须是 useful、partly-useful 或 not-useful。");

        ValidateHardBoundaries(frozenProject, item.Parameters, "模型建议");
        if (selected.Count > 0)
            ValidateHardBoundaries(frozenProject, selected, "工程师选择");
        var now = DateTimeOffset.UtcNow;
        var snapshotHash = Hash(new
        {
            recommendation.RecommendationId,
            recommendation.ProjectRevision,
            projectEvidence.Hash,
            recommendation.ModelVersion,
            recommendation.InputHash,
            recommendationKey,
            SuggestedParameters = item.Parameters.OrderBy(static value => value.VariableCode),
            item.Prediction,
            decision,
            EngineerSelectedParameters = selected.OrderBy(static value => value.VariableCode),
            reason,
            usefulnessRating,
            actualExecutionKey,
            decidedBy
        });
        if (await store.GetRecipeRecommendationDecisionByItemAsync(
                recommendationId, recommendationKey, ct).ConfigureAwait(false) is { } existing)
            return ExactRetryOrConflict(existing, snapshotHash);
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");
        if (recommendation.ProjectRevision != project.Revision)
            throw new ProcessResearchRuleException(
                "项目定义已在建议生成后变更；请重新生成建议，不能把旧建议登记为当前决定。");
        var value = new ResearchRecipeRecommendationDecision
        {
            DecisionId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = recommendation.ProjectRevision,
            ProjectSnapshot = projectEvidence.Snapshot,
            ProjectSnapshotHash = projectEvidence.Hash,
            RecommendationId = recommendation.RecommendationId,
            RecommendationKey = recommendationKey,
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
        var audit = new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "recipe-recommendation-decision",
            ResourceId = value.DecisionId.ToString(),
            Action = "decision-frozen",
            ToStatus = decision,
            UserId = decidedBy,
            CreatedAt = now
        };
        try
        {
            return await store.CreateRecipeRecommendationDecisionTransactionAsync(
                    value, actualExecutionKey, audit, ct)
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
        var currentProject = await store.GetProjectAsync(decision.ProjectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (currentProject.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");
        var recommendation = await store.GetRecipeRecommendationAsync(decision.RecommendationId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("日常配方建议不存在。");
        var project = RequireFrozenProject(recommendation, decision, currentProject);
        if (!string.IsNullOrWhiteSpace(decision.ActualExecutionKey))
            throw new ProcessResearchRuleException("该工程师决定已经关联了其他实际运行，不能覆盖。");
        await RequireExecutionForLinkAsync(
            project, actualExecutionKey, decision.DecidedAt, ct).ConfigureAwait(false);
        return await store.LinkRecipeRecommendationDecisionExecutionTransactionAsync(
                decisionId,
                actualExecutionKey,
                new ResearchAuditEntry
                {
                    EntryId = Guid.CreateVersion7(),
                    ProjectId = project.ProjectId,
                    ResourceType = "recipe-recommendation-decision",
                    ResourceId = decisionId.ToString(),
                    Action = "actual-execution-linked",
                    UserId = Required(userId, "操作人", 240),
                    CreatedAt = DateTimeOffset.UtcNow
                },
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
        var currentProject = await store.GetProjectAsync(decision.ProjectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (currentProject.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");
        var recommendation = await store.GetRecipeRecommendationAsync(decision.RecommendationId, ct)
            .ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("日常配方建议不存在。");
        var project = RequireFrozenProject(recommendation, decision, currentProject);

        var actualExecutionKey = Required(decision.ActualExecutionKey, "已关联实际运行标识", 120);
        await RequireCompletedExecutionAsync(
            project, actualExecutionKey, decision.DecidedAt, ct).ConfigureAwait(false);
        var assembly = await observationAssembler.AssembleProductionRunAsync(
            project, actualExecutionKey, ct).ConfigureAwait(false);
        var observation = assembly.Observations.SingleOrDefault()
            ?? throw new ProcessResearchRuleException(
                "实际运行尚未形成可关联的完整过程执行，不能冻结日常建议结果。");
        var actualControlCodes = observation.ActualFactors
            .Select(static value => value.VariableCode)
            .ToHashSet(StringComparer.Ordinal);
        var missingControls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .Where(value => !actualControlCodes.Contains(value.Code))
            .Select(static value => value.Code)
            .ToArray();
        if (missingControls.Length > 0)
            throw new ProcessResearchRuleException(
                $"实际运行缺少完整参数回读：{string.Join("、", missingControls)}。");
        var missingOutcomes = project.Objectives
            .Where(value => !observation.Outcomes.ContainsKey(value.Code))
            .Select(static value => value.Code)
            .ToArray();
        if (missingOutcomes.Length > 0)
            throw new ProcessResearchRuleException(
                $"实际运行尚未形成完整质量结果：{string.Join("、", missingOutcomes)}。");
        var missingConstraintOutcomes = project.OutcomeConstraints
            .Where(value => !observation.ConstraintOutcomes.ContainsKey(value.Code))
            .Select(static value => value.Code)
            .ToArray();
        if (missingConstraintOutcomes.Length > 0)
            throw new ProcessResearchRuleException(
                $"实际运行尚未形成完整结果约束：{string.Join("、", missingConstraintOutcomes)}。");
        if (observation.ProcessFeatures.Count == 0)
            throw new ProcessResearchRuleException("实际运行尚未形成过程特征，不能冻结日常建议结果。");
        if (!observation.ValidForOptimization)
            throw new ProcessResearchRuleException(
                $"实际运行尚未通过优化证据准入：{observation.ExclusionReason ?? "原因未记录"}。");
        await RequireCompletedExecutionAsync(
            project, actualExecutionKey, decision.DecidedAt, ct).ConfigureAwait(false);
        var outcome = new ResearchRecipeRecommendationOutcome
        {
            ProjectRevision = decision.ProjectRevision,
            ProjectSnapshotHash = decision.ProjectSnapshotHash,
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
        var audit = new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "recipe-recommendation-decision",
            ResourceId = decision.DecisionId.ToString(),
            Action = "source-outcome-frozen",
            FromStatus = decision.Decision,
            ToStatus = decision.Decision,
            UserId = Required(userId, "操作人", 240),
            CreatedAt = outcome.CapturedAt
        };
        return await store.AttachRecipeRecommendationOutcomeTransactionAsync(
            decision.DecisionId, outcome, audit, ct).ConfigureAwait(false);
    }

    private async Task<ExecutionComparisonRow> RequireExecutionForLinkAsync(
        ResearchProject project,
        string executionKey,
        DateTimeOffset decidedAt,
        CancellationToken ct)
    {
        var execution = await RequireExecutionAsync(project, executionKey, decidedAt, ct)
            .ConfigureAwait(false);
        if (execution.HasCompleted || execution.LifecycleComplete || execution.CompletedAt is not null ||
            execution.InspectionOutcomes.Count > 0)
            throw new ProcessResearchRuleException(
                "不能在结果已知后补选历史运行；请在工程师决定之后、运行完成之前关联实际运行。");
        return execution;
    }

    private async Task<ExecutionComparisonRow> RequireCompletedExecutionAsync(
        ResearchProject project,
        string executionKey,
        DateTimeOffset decidedAt,
        CancellationToken ct)
    {
        var execution = await RequireExecutionAsync(project, executionKey, decidedAt, ct)
            .ConfigureAwait(false);
        if (!execution.HasCompleted || !execution.LifecycleComplete || execution.CompletedAt is null)
            throw new ProcessResearchRuleException("实际运行尚未完成，不能冻结日常建议结果。");
        return execution;
    }

    private async Task<ExecutionComparisonRow> RequireExecutionAsync(
        ResearchProject project,
        string executionKey,
        DateTimeOffset decidedAt,
        CancellationToken ct)
    {
        var siteId = Required(project.SiteCode, "项目站点", 120);
        var execution = await executionComparisons.GetProcessExecutionAsync(
                executionKey, ct, siteId).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("实际运行不存在或不在项目站点范围内。");
        if (!execution.HasStarted)
            throw new ProcessResearchRuleException("实际运行尚未开始，不能关联到工程师决定。");
        if (execution.StartedAt < decidedAt)
            throw new ProcessResearchRuleException("实际运行必须在工程师决定之后开始，不能事后挑选历史结果。");
        ValidateExecutionScope(project, execution);
        return execution;
    }

    private static void ValidateExecutionScope(ResearchProject project, ExecutionComparisonRow execution)
    {
        ValidateScopeValue(project, "product_family_code", execution.ProductFamilyCode, "产品族");
        ValidateScopeValue(project, "product_code", execution.ProductCode, "产品");
        ValidateScopeValue(project, "equipment_id", execution.EquipmentId, "设备");
        ValidateScopeValue(project, "process_specification_id", execution.ProcessSpecificationId, "工艺规范");
        ValidateScopeValue(project, "output_item_id", execution.OutputItemId, "产出物料");
    }

    private static void ValidateScopeValue(
        ResearchProject project,
        string contextKey,
        string? actual,
        string label)
    {
        if (project.Context.TryGetValue(contextKey, out var expected) &&
            !string.IsNullOrWhiteSpace(expected) &&
            !string.Equals(expected.Trim(), actual?.Trim(), StringComparison.Ordinal))
            throw new ProcessResearchRuleException($"实际运行的{label}不属于冻结的项目范围。");
    }

    private static ResearchProject RequireFrozenProject(
        ResearchRecipeRecommendation recommendation,
        ResearchRecipeRecommendationDecision decision,
        ResearchProject currentProject)
    {
        if (decision.ProjectSnapshotHash != "none")
        {
            var decisionHash = ResearchProjectEvidenceSnapshots.Hash(decision.ProjectSnapshot);
            if (!string.Equals(decisionHash, decision.ProjectSnapshotHash, StringComparison.Ordinal) ||
                decision.ProjectSnapshot.Revision != decision.ProjectRevision ||
                decision.ProjectSnapshot.ProjectId != decision.ProjectId ||
                decision.ProjectRevision != recommendation.ProjectRevision)
                throw new ProcessResearchRuleException("工程师决定的冻结项目快照校验失败。");
            return ResearchProjectEvidenceSnapshots.Restore(decision.ProjectSnapshot);
        }

        var evidence = ResolveRecommendationSnapshot(recommendation, currentProject);
        if (decision.ProjectRevision != evidence.Snapshot.Revision ||
            decision.ProjectSnapshotHash != "none" &&
            !string.Equals(decision.ProjectSnapshotHash, evidence.Hash, StringComparison.Ordinal))
            throw new ProcessResearchRuleException("工程师决定与建议的冻结项目快照不一致。");
        return ResearchProjectEvidenceSnapshots.Restore(evidence.Snapshot);
    }

    private static (ResearchProjectEvidenceSnapshot Snapshot, string Hash) ResolveRecommendationSnapshot(
        ResearchRecipeRecommendation recommendation,
        ResearchProject currentProject)
    {
        if (recommendation.ProjectSnapshotHash != "none")
        {
            var hash = ResearchProjectEvidenceSnapshots.Hash(recommendation.ProjectSnapshot);
            if (!string.Equals(hash, recommendation.ProjectSnapshotHash, StringComparison.Ordinal) ||
                recommendation.ProjectSnapshot.Revision != recommendation.ProjectRevision ||
                recommendation.ProjectSnapshot.ProjectId != recommendation.ProjectId)
                throw new ProcessResearchRuleException("下一配方建议的项目证据快照校验失败。");
            return (recommendation.ProjectSnapshot, hash);
        }

        if (recommendation.ProjectRevision != currentProject.Revision ||
            recommendation.ProjectId != currentProject.ProjectId)
            throw new ProcessResearchRuleException(
                "旧版下一配方建议缺少可恢复的项目快照且项目定义已变化，请重新生成建议。");
        var snapshot = ResearchProjectEvidenceSnapshots.Freeze(currentProject);
        return (snapshot, ResearchProjectEvidenceSnapshots.Hash(snapshot));
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
        ResearchProject project,
        IReadOnlyList<ResearchVariableSetting> parameters)
    {
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (!parameters.Select(static value => value.VariableCode)
                .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys) ||
            parameters.Count != controls.Count)
            throw new ProcessResearchRuleException("工程师选择必须包含且仅包含全部可控变量。");
        return parameters.Select(value =>
        {
            if (!controls.TryGetValue(value.VariableCode, out var variable) ||
                !double.IsFinite(value.Value) ||
                variable.LowerLimit is { } lower && value.Value < lower ||
                variable.UpperLimit is { } upper && value.Value > upper)
                throw new ProcessResearchRuleException($"工程师选择 {value.VariableCode} 超出项目边界。");
            if (!string.Equals(value.Unit?.Trim(), variable.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessResearchRuleException($"工程师选择 {value.VariableCode} 的单位不一致。");
            return value with { Unit = variable.Unit };
        }).OrderBy(static value => value.VariableCode, StringComparer.Ordinal).ToArray();
    }

    private static void ValidateHardBoundaries(
        ResearchProject project,
        IReadOnlyList<ResearchVariableSetting> parameters,
        string label)
    {
        var values = parameters.ToDictionary(static value => value.VariableCode,
            static value => value.Value, StringComparer.Ordinal);
        foreach (var constraint in project.Constraints)
        {
            if (!values.TryGetValue(constraint.VariableCode, out var value))
                throw new ProcessResearchRuleException($"{label}缺少安全约束变量 {constraint.VariableCode}。");
            var passed = constraint.Operator switch
            {
                "<=" => value <= constraint.Limit,
                ">=" => value >= constraint.Limit,
                "<" => value < constraint.Limit,
                ">" => value > constraint.Limit,
                "==" => Math.Abs(value - constraint.Limit) <= 1e-9,
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
            throw new ProcessResearchRuleException($"{field}不能为空且长度不能超过 {maximumLength}。 ");
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
