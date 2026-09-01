// 从真实生产运行生成下一配方建议；建议本身不创建或下发运行计划。
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ResearchAssets;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>基于冻结的真实生产观察生成下一配方建议，不创建、批准或执行运行计划。</summary>
public sealed class ResearchOptimizationService(
    IProcessResearchStore store,
    IProcessOptimizerClient optimizerClient,
    IResearchObservationAssembler observationAssembler,
    IMechanismKnowledgeStore? mechanismKnowledgeStore = null,
    IResearchAssetStore? researchAssetStore = null)
{
    public async Task<ResearchRecipeRecommendation> CreateNextRecipeRecommendationAsync(
        Guid projectId,
        ResearchRecipeRecommendationRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");

        var mechanismKnowledge = mechanismKnowledgeStore is null
            ? new AppliedMechanismKnowledge([], [], [], [])
            : MechanismKnowledgeRecommendationPolicy.Select(project,
                await mechanismKnowledgeStore.ListClaimsAsync(projectId, ct).ConfigureAwait(false),
                await mechanismKnowledgeStore.ListConflictsAsync(projectId, ct).ConfigureAwait(false));
        var mechanismModels = researchAssetStore is null
            ? new AppliedMechanismModels([], [])
            : MechanismModelRecommendationPolicy.Select(project,
                await researchAssetStore.ListMechanismModelsAsync(ct).ConfigureAwait(false),
                await researchAssetStore.ListMechanismFusionsAsync(ct).ConfigureAwait(false));

        var assembly = await observationAssembler.AssembleProductionRunsAsync(project, ct).ConfigureAwait(false);
        var objectiveCodes = project.Objectives.Select(static value => value.Code).ToHashSet(StringComparer.Ordinal);
        var constraintCodes = project.OutcomeConstraints.Select(static value => value.Code).ToHashSet(StringComparer.Ordinal);
        var controls = project.Variables.Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var projectSnapshot = ResearchProjectEvidenceSnapshots.Freeze(project);
        var projectSnapshotHash = ResearchProjectEvidenceSnapshots.Hash(projectSnapshot);
        var observations = assembly.Observations
            .Where(value => value.ValidForOptimization &&
                value.Outcomes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(objectiveCodes) &&
                value.ConstraintOutcomes.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(constraintCodes) &&
                value.ActualFactors.Select(static factor => factor.VariableCode).ToHashSet(StringComparer.Ordinal)
                    .SetEquals(controls.Keys))
            .GroupBy(static value => value.ExecutionKey, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .Select(static value => new OptimizerObservationInput
            {
                Params = value.ActualFactors.ToDictionary(static factor => factor.VariableCode,
                    static factor => factor.Value, StringComparer.Ordinal),
                Outcomes = value.Outcomes,
                ConstraintOutcomes = value.ConstraintOutcomes,
                ProcessFeatures = value.ProcessFeatures
            }).ToArray();
        if (observations.Length < 3)
            throw new ProcessResearchRuleException("至少需要 3 条具有完整参数和质量结果的生产运行，才能生成下一配方建议。");
        if (observations.Select(static value => string.Join('|', value.Params.OrderBy(static pair => pair.Key,
                StringComparer.Ordinal).Select(static pair => $"{pair.Key}:{pair.Value:R}")))
            .Distinct(StringComparer.Ordinal).Count() < 2)
            throw new ProcessResearchRuleException("当前生产记录只有一种实际配方，尚无法比较配方效果或推荐下一配方。");

        var pendingPoints = (await store.ListPendingRecipeRecommendationDecisionsAsync(projectId, ct)
                .ConfigureAwait(false))
            .Where(value => value.ProjectRevision == project.Revision &&
                string.Equals(value.ProjectSnapshotHash, projectSnapshotHash, StringComparison.Ordinal))
            .Select(value => MapPendingPoint(value, controls))
            .ToArray();

        var topK = mechanismKnowledge.RankingConstraints.Count == 0 ? 1 : 4;
        var call = new OptimizerSuggestionCall
        {
            Campaign = MechanismModelRecommendationPolicy.Apply(
                MechanismKnowledgeRecommendationPolicy.ApplyHardConstraints(BuildCampaign(project), mechanismKnowledge),
                mechanismModels),
            Observations = observations,
            PendingPoints = pendingPoints,
            TopK = topK,
            Seed = request.Seed
        };
        var inputHash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Operation = "next-recipe-recommendation",
            OptimizerCall = call,
            MechanismClaims = mechanismKnowledge.Claims.Select(static value => new { value.ClaimId, value.Version, value.ContentHash }),
            MechanismModels = mechanismModels.References
        })));
        var existing = await store.GetRecipeRecommendationByInputHashAsync(projectId, inputHash, ct).ConfigureAwait(false);
        if (existing is not null) return existing;

        var response = await optimizerClient.SuggestAsync(call, ct).ConfigureAwait(false);
        if (response.ObservationCount != observations.Length || response.Suggestions.Count != topK)
            throw new ProcessResearchRuleException("优化服务使用的数据快照或返回的配方建议数量不一致。");
        foreach (var candidate in response.Suggestions)
        {
            ValidateSuggestion(project, response.ModelVersion, candidate);
            MechanismKnowledgeRecommendationPolicy.ValidateHardConstraints(candidate, mechanismKnowledge);
        }
        var suggestion = MechanismKnowledgeRecommendationPolicy.Rank(response.Suggestions, mechanismKnowledge, controls).First();
        var recommendationId = CreateDeterministicRecommendationId(projectId, inputHash);
        var recommendationKey = $"recipe-{recommendationId:N}"[..22];
        var now = DateTimeOffset.UtcNow;
        var recommendation = new ResearchRecipeRecommendation
        {
            RecommendationId = recommendationId,
            ProjectId = projectId,
            ProjectRevision = project.Revision,
            ProjectSnapshot = projectSnapshot,
            ProjectSnapshotHash = projectSnapshotHash,
            ModelVersion = response.ModelVersion,
            InputHash = inputHash,
            ObservationCount = observations.Length,
            AutoAssembledObservationCount = observations.Length,
            ProcessFeatureCount = CommonProcessFeatureCount(observations),
            FeatureSetId = project.OptimizationFeatures.FeatureSetId,
            FeatureSetVersion = project.OptimizationFeatures.Version,
            DerivedFeatureCount = call.Campaign.DerivedFeatures.Count,
            MechanismKnowledgeSnapshotHash = MechanismKnowledgeRecommendationPolicy.SnapshotHash(mechanismKnowledge),
            MechanismModelSnapshotHash = mechanismModels.SnapshotHash,
            MechanismModels = mechanismModels.References,
            Items = [new ResearchRecipeRecommendationItem
            {
                RecommendationKey = recommendationKey,
                Parameters = suggestion.RecommendedParameters.Select(pair => new ResearchVariableSetting
                {
                    VariableCode = pair.Key,
                    Value = pair.Value,
                    Unit = controls[pair.Key].Unit
                }).OrderBy(static value => value.VariableCode, StringComparer.Ordinal).ToArray(),
                Prediction = MapPrediction(recommendationKey, suggestion)
            }],
            CreatedBy = userId,
            GeneratedAt = now
        };
        try
        {
            return await store.CreateRecipeRecommendationTransactionAsync(recommendation, new ResearchAuditEntry
            {
                EntryId = Guid.CreateVersion7(), ProjectId = projectId, ResourceType = "recipe-recommendation",
                ResourceId = recommendationId.ToString(), Action = "created", UserId = userId, CreatedAt = now
            }, ct).ConfigureAwait(false);
        }
        catch (ProcessResearchRuleException)
        {
            var concurrent = await store.GetRecipeRecommendationAsync(recommendationId, ct).ConfigureAwait(false);
            if (concurrent is not null)
                return concurrent;
            throw;
        }
    }

    internal static OptimizerCampaignInput BuildCampaign(ResearchProject project)
    {
        var controls = project.Variables.Where(static value => value.Role == ResearchVariableRoles.Control).ToArray();
        if (controls.Length == 0 || controls.Any(static value => value.LowerLimit is null || value.UpperLimit is null))
            throw new ProcessResearchRuleException("优化要求全部可控变量都定义上下界。");
        return new OptimizerCampaignInput
        {
            Name = project.Name, FeatureSetId = project.OptimizationFeatures.FeatureSetId,
            FeatureSetVersion = project.OptimizationFeatures.Version,
            DerivedFeatures = project.OptimizationFeatures.DerivedFeatures.Select(value => new OptimizerDerivedFeatureInput
            { Name = value.Name, Operator = value.Operator, Inputs = value.Inputs, NormalizationOffset = value.NormalizationOffset,
                NormalizationScale = value.NormalizationScale, Epsilon = value.Epsilon }).ToArray(),
            DecisionIntent = ResearchOptimizationIntents.ReachSpecification,
            Variables = controls.Select(value => new OptimizerVariableInput(value.Code, value.LowerLimit!.Value,
                value.UpperLimit!.Value, value.Unit)).ToArray(),
            Objectives = project.Objectives.Select(MapObjective).ToArray(),
            Constraints = project.Constraints.Select(value => new OptimizerConstraintInput
            { Variable = value.VariableCode, Operator = value.Operator, Limit = value.Limit, SafetyCritical = value.SafetyCritical }).ToArray(),
            OutcomeConstraints = project.OutcomeConstraints.Select(value => new OptimizerOutcomeConstraintInput
            { Name = value.Code, Operator = value.Operator, Limit = value.Limit, Unit = value.Unit,
                SafetyCritical = value.SafetyCritical, MinimumProbability = value.MinimumProbability }).ToArray(),
            Context = project.Context
        };
    }

    private static OptimizationRunPrediction MapPrediction(string key, OptimizerSuggestionOutput value) => new()
    {
        ExecutionKey = key,
        Objectives = value.Predictions.ToDictionary(static pair => pair.Key, static pair => new OptimizationMetricPrediction
        { Mean = pair.Value.Mean, StandardDeviation = pair.Value.StandardDeviation, Lower95 = pair.Value.Lower95,
            Upper95 = pair.Value.Upper95, Unit = pair.Value.Unit }, StringComparer.Ordinal),
        Constraints = value.ConstraintPredictions.ToDictionary(static pair => pair.Key, static pair => new OptimizationMetricPrediction
        { Mean = pair.Value.Mean, StandardDeviation = pair.Value.StandardDeviation, Lower95 = pair.Value.Lower95,
            Upper95 = pair.Value.Upper95, Unit = pair.Value.Unit }, StringComparer.Ordinal),
        FeasibilityProbability = value.FeasibilityProbability, AcquisitionValue = value.AcquisitionValue,
        ColdStart = value.ColdStart, Rationale = value.Rationale
    };

    private static IReadOnlyDictionary<string, double> MapPendingPoint(
        ResearchRecipeRecommendationDecision decision,
        IReadOnlyDictionary<string, ResearchVariable> controls)
    {
        var values = decision.EngineerSelectedParameters.OrderBy(static value => value.VariableCode, StringComparer.Ordinal)
            .ToDictionary(
            static value => value.VariableCode,
            static value => value.Value,
            StringComparer.Ordinal);
        if (!values.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
            throw new ProcessResearchRuleException("待观察配方的可控变量集合与当前项目不一致。");
        foreach (var (code, value) in values)
            if (!double.IsFinite(value) || value < controls[code].LowerLimit || value > controls[code].UpperLimit)
                throw new ProcessResearchRuleException($"待观察配方的优化变量 {code} 超出项目范围。");
        return values;
    }

    private static OptimizerObjectiveInput MapObjective(ResearchObjective value) => value.Direction switch
    {
        "minimize" => new() { Name = value.Code, Kind = "le", Threshold = value.UpperLimit ?? value.Target, Weight = value.Weight, Unit = value.Unit },
        "maximize" => new() { Name = value.Code, Kind = "ge", Threshold = value.LowerLimit ?? value.Target, Weight = value.Weight, Unit = value.Unit },
        "range" when value.LowerLimit is { } lower && value.UpperLimit is { } upper => new() { Name = value.Code, Kind = "range", Lower = lower, Upper = upper, Weight = value.Weight, Unit = value.Unit },
        "target" when value.LowerLimit is { } lower && value.UpperLimit is { } upper => new() { Name = value.Code, Kind = "target", Target = value.Target, Tol = Math.Min(value.Target - lower, upper - value.Target), Weight = value.Weight, Unit = value.Unit },
        _ => throw new ProcessResearchRuleException($"目标 {value.Code} 的方向或规格定义不支持优化。")
    };

    private static void ValidateSuggestion(ResearchProject project, string modelVersion, OptimizerSuggestionOutput value)
    {
        var controls = project.Variables.Where(static item => item.Role == ResearchVariableRoles.Control)
            .ToDictionary(static item => item.Code, StringComparer.Ordinal);
        if (!string.Equals(modelVersion, value.ModelVersion, StringComparison.Ordinal) ||
            !value.RecommendedParameters.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
            throw new ProcessResearchRuleException("优化建议的模型版本或变量集合无效。");
        foreach (var (code, parameter) in value.RecommendedParameters)
            if (!double.IsFinite(parameter) || parameter < controls[code].LowerLimit || parameter > controls[code].UpperLimit)
                throw new ProcessResearchRuleException($"优化变量 {code} 超出项目范围。");
    }

    private static int CommonProcessFeatureCount(IReadOnlyList<OptimizerObservationInput> observations)
    {
        var common = observations[0].ProcessFeatures.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var value in observations.Skip(1)) common.IntersectWith(value.ProcessFeatures.Keys);
        return common.Count;
    }

    private static Guid CreateDeterministicRecommendationId(Guid projectId, string inputHash)
        => new(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { ProjectId = projectId, InputHash = inputHash })).AsSpan(0, 16));
}
