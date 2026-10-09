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
    RecipeRecommendationBriefPolicy briefPolicy,
    IMechanismKnowledgeStore? mechanismKnowledgeStore = null,
    IResearchAssetStore? researchAssetStore = null)
{
    public async Task<RecipeRecommendationReadiness> GetReadinessAsync(
        RecipeRecommendationBrief draft,
        CancellationToken ct = default)
    {
        var brief = await briefPolicy.ResolveAsync(draft, ct).ConfigureAwait(false);
        var assembly = await observationAssembler.AssembleProductionRunsAsync(brief, ct).ConfigureAwait(false);
        return new RecipeRecommendationReadiness
        {
            CandidateRunCount = assembly.CandidateRunCount,
            ValidObservationCount = assembly.ValidObservationCount,
            ExcludedObservationCount = assembly.Observations.Count - assembly.ValidObservationCount,
            Truncated = assembly.IsTruncated,
            ObservedExecutionKeys = assembly.Observations.Select(static value => value.ExecutionKey).ToArray(),
            ExcludedObservations = assembly.Observations
                .Where(static value => !value.ValidForOptimization)
                .Take(8)
                .Select(static value => new RecipeRecommendationExclusion
                {
                    ExecutionKey = value.ExecutionKey,
                    Reason = string.IsNullOrWhiteSpace(value.ExclusionReason) ? "数据不完整" : value.ExclusionReason
                }).ToArray()
        };
    }

    public async Task<ResearchRecipeRecommendation> CreateNextRecipeRecommendationAsync(
        ResearchRecipeRecommendationRequest request,
        string userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var brief = await briefPolicy.ResolveAsync(request.Brief, ct).ConfigureAwait(false);
        var briefHash = RecipeRecommendationBriefPolicy.Hash(brief);

        var mechanismKnowledge = mechanismKnowledgeStore is null
            ? new AppliedMechanismKnowledge([], [], [], [])
            : MechanismKnowledgeRecommendationPolicy.Select(brief,
                await mechanismKnowledgeStore.ListClaimsAsync(brief.SiteCode, brief.ProcessSpecificationId, ct)
                    .ConfigureAwait(false),
                await mechanismKnowledgeStore.ListConflictsAsync(brief.SiteCode, brief.ProcessSpecificationId, ct)
                    .ConfigureAwait(false));
        var mechanismModels = researchAssetStore is null
            ? new AppliedMechanismModels([], [])
            : MechanismModelRecommendationPolicy.Select(brief,
                await researchAssetStore.ListMechanismModelsAsync(ct).ConfigureAwait(false),
                await researchAssetStore.ListMechanismFusionsAsync(ct).ConfigureAwait(false));

        var assembly = await observationAssembler.AssembleProductionRunsAsync(brief, ct).ConfigureAwait(false);
        var objectiveCodes = brief.Objectives.Select(static value => value.Code).ToHashSet(StringComparer.Ordinal);
        var constraintCodes = brief.OutcomeConstraints.Select(static value => value.Code).ToHashSet(StringComparer.Ordinal);
        var controls = brief.Variables.Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
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

        var pendingPoints = (await store.ListPendingRecipeRecommendationDecisionsAsync(
                    brief.SiteCode, brief.ProcessSpecificationId, ct)
                .ConfigureAwait(false))
            .Where(value => string.Equals(value.BriefHash, briefHash, StringComparison.Ordinal))
            .Select(value => MapPendingPoint(value, controls))
            .ToArray();

        // The platform exposes one actionable recommendation. Keep the optimizer
        // contract aligned with that UX instead of calculating and discarding candidates.
        const int topK = 1;
        var call = new OptimizerSuggestionCall
        {
            Campaign = MechanismModelRecommendationPolicy.Apply(
                MechanismKnowledgeRecommendationPolicy.ApplyHardConstraints(BuildCampaign(brief), mechanismKnowledge),
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
        var existing = await store.GetRecipeRecommendationByInputHashAsync(
            brief.SiteCode, brief.ProcessSpecificationId, inputHash, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await RecordKnowledgeUsagesAsync(existing.RecommendationId, mechanismKnowledge, ct).ConfigureAwait(false);
            return existing;
        }

        var response = await optimizerClient.SuggestAsync(call, ct).ConfigureAwait(false);
        if (response.ObservationCount != observations.Length || response.Suggestions.Count != topK)
            throw new ProcessResearchRuleException("优化服务使用的数据快照或返回的配方建议数量不一致。");
        var campaignVariables = call.Campaign.Variables.ToDictionary(
            static value => value.Name, StringComparer.Ordinal);
        var coverage = BuildObservedCoverage(call.Campaign.Variables, observations);
        ValidateReportedCoverage(response.CoverageEnvelope, coverage, campaignVariables);
        var alignedSuggestions = new List<OptimizerSuggestionOutput>(response.Suggestions.Count);
        foreach (var candidate in response.Suggestions)
        {
            var aligned = candidate with
            {
                RecommendedParameters = await briefPolicy.SnapRecommendedParametersAsync(
                    brief, candidate.RecommendedParameters, ct).ConfigureAwait(false)
            };
            ValidateSuggestion(brief, response.ModelVersion, aligned);
            ValidateObservedCoverage(coverage, campaignVariables, aligned);
            MechanismKnowledgeRecommendationPolicy.ValidateHardConstraints(aligned, mechanismKnowledge);
            alignedSuggestions.Add(aligned);
        }
        var suggestion = MechanismKnowledgeRecommendationPolicy.Rank(alignedSuggestions, mechanismKnowledge, controls).First();
        var recommendationId = CreateDeterministicRecommendationId(
            brief.SiteCode, brief.ProcessSpecificationId, inputHash);
        var recommendationKey = $"recipe-{recommendationId:N}"[..22];
        var now = DateTimeOffset.UtcNow;
        var recommendation = new ResearchRecipeRecommendation
        {
            RecommendationId = recommendationId,
            SiteCode = brief.SiteCode,
            ProcessSpecificationId = brief.ProcessSpecificationId,
            Brief = brief,
            BriefHash = briefHash,
            ModelVersion = response.ModelVersion,
            InputHash = inputHash,
            ObservationCount = observations.Length,
            AutoAssembledObservationCount = observations.Length,
            ProcessFeatureCount = CommonProcessFeatureCount(observations),
            FeatureSetId = brief.OptimizationFeatures.FeatureSetId,
            FeatureSetVersion = brief.OptimizationFeatures.Version,
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
            GeneratedAt = now,
            ExpiresAt = now.AddHours(24)
        };
        ResearchRecipeRecommendation saved;
        try
        {
            saved = await store.CreateRecipeRecommendationAsync(recommendation, ct).ConfigureAwait(false);
        }
        catch (ProcessResearchRuleException)
        {
            saved = await store.GetRecipeRecommendationAsync(recommendationId, ct).ConfigureAwait(false)
                ?? throw new ProcessResearchRuleException("相同输入快照的配方建议写入失败，请重试。");
        }
        await RecordKnowledgeUsagesAsync(saved.RecommendationId, mechanismKnowledge, ct).ConfigureAwait(false);
        return saved;
    }

    private async Task RecordKnowledgeUsagesAsync(
        Guid recommendationId,
        AppliedMechanismKnowledge knowledge,
        CancellationToken ct)
    {
        if (mechanismKnowledgeStore is null || knowledge.Claims.Count == 0)
            return;
        var usages = knowledge.Claims.SelectMany(claim =>
        {
            var types = new List<string>();
            if (claim.Constraints.Any(static value => value.Severity == "hard")) types.Add("hard-constraint");
            if (claim.Constraints.Any(static value => value.Severity == "soft")) types.Add("soft-ranking");
            if (claim.ForbiddenCombinations.Count > 0) types.Add("forbidden-combination");
            if (types.Count == 0) types.Add("context");
            return types.Select(type => new MechanismClaimUsage
            {
                RecommendationId = recommendationId,
                ClaimId = claim.ClaimId,
                ClaimVersion = claim.Version,
                UsageType = type,
                ContentHash = claim.ContentHash,
                ClaimName = claim.Name
            });
        }).ToArray();
        await mechanismKnowledgeStore.SaveRecipeRecommendationUsagesAsync(usages, ct).ConfigureAwait(false);
    }

    internal static OptimizerCampaignInput BuildCampaign(RecipeRecommendationBrief brief)
    {
        var controls = brief.Variables.Where(static value => value.Role == ResearchVariableRoles.Control).ToArray();
        if (controls.Length == 0 || controls.Any(static value => value.LowerLimit is null || value.UpperLimit is null))
            throw new ProcessResearchRuleException("优化要求全部可控变量都定义上下界。");
        return new OptimizerCampaignInput
        {
            Name = brief.Name, FeatureSetId = brief.OptimizationFeatures.FeatureSetId,
            FeatureSetVersion = brief.OptimizationFeatures.Version,
            DerivedFeatures = brief.OptimizationFeatures.DerivedFeatures.Select(value => new OptimizerDerivedFeatureInput
            { Name = value.Name, Operator = value.Operator, Inputs = value.Inputs, NormalizationOffset = value.NormalizationOffset,
                NormalizationScale = value.NormalizationScale, Epsilon = value.Epsilon }).ToArray(),
            DecisionIntent = ResearchOptimizationIntents.ReachSpecification,
            Variables = controls.Select(value => new OptimizerVariableInput(value.Code, value.LowerLimit!.Value,
                value.UpperLimit!.Value, value.Unit)).ToArray(),
            Objectives = brief.Objectives.Select(MapObjective).ToArray(),
            Constraints = brief.Constraints.Select(value => new OptimizerConstraintInput
            { Variable = value.VariableCode, Operator = value.Operator, Limit = value.Limit, SafetyCritical = value.SafetyCritical }).ToArray(),
            OutcomeConstraints = brief.OutcomeConstraints.Select(value => new OptimizerOutcomeConstraintInput
            { Name = value.Code, Operator = value.Operator, Limit = value.Limit, Unit = value.Unit,
                SafetyCritical = value.SafetyCritical, MinimumProbability = value.MinimumProbability }).ToArray(),
            Context = brief.Context
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
        ColdStart = value.ColdStart, Rationale = ExplainRationale(value.Rationale)
    };

    internal static string ExplainRationale(string? rationale)
    {
        if (string.IsNullOrWhiteSpace(rationale))
            return "根据当前可见的真实运行给出下一版参数。";
        if (rationale.Contains("production surrogate", StringComparison.OrdinalIgnoreCase))
            return "在已观察的参数范围内，按当前模型选出更接近质量目标的设定。";
        if (rationale.Contains("verified safe baseline", StringComparison.OrdinalIgnoreCase))
            return "有效运行还不足以建立预测模型，已在已验证的安全设定附近取下一步。";
        if (rationale.Contains("space-filling", StringComparison.OrdinalIgnoreCase))
            return "有效运行还不足以建立预测模型，已在可行范围内取下一步。";
        if (rationale.Contains("Monte Carlo", StringComparison.OrdinalIgnoreCase))
            return "按预期改进选出下一版参数，并保留每个质量目标的不确定性。";
        return rationale;
    }

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
            throw new ProcessResearchRuleException("待观察配方的可控变量集合与当前建议条件不一致。");
        foreach (var (code, value) in values)
            if (!double.IsFinite(value) || value < controls[code].LowerLimit || value > controls[code].UpperLimit)
                throw new ProcessResearchRuleException($"待观察配方的优化变量 {code} 超出建议条件的参数范围。");
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

    private static void ValidateSuggestion(RecipeRecommendationBrief brief, string modelVersion, OptimizerSuggestionOutput value)
    {
        var controls = brief.Variables.Where(static item => item.Role == ResearchVariableRoles.Control)
            .ToDictionary(static item => item.Code, StringComparer.Ordinal);
        if (!string.Equals(modelVersion, value.ModelVersion, StringComparison.Ordinal) ||
            !value.RecommendedParameters.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
            throw new ProcessResearchRuleException("优化建议的模型版本或变量集合无效。");
        foreach (var (code, parameter) in value.RecommendedParameters)
            if (!double.IsFinite(parameter) || parameter < controls[code].LowerLimit || parameter > controls[code].UpperLimit)
                throw new ProcessResearchRuleException($"优化变量 {code} 超出建议条件的参数范围。");
    }

    // Mirrors the range gate in optimizer/ingot_optimizer/coverage.py; the platform
    // recomputes it so a recommendation outside the covered region fails closed even
    // when the optimization service does not apply the gate.
    private const double CoverageRelativeMargin = 0.10;
    private const double CoverageMinimumStep = 0.02;
    private const double CoverageTolerance = 1e-9;

    private static IReadOnlyDictionary<string, (double Lower, double Upper)> BuildObservedCoverage(
        IReadOnlyList<OptimizerVariableInput> variables,
        IReadOnlyList<OptimizerObservationInput> observations)
    {
        var coverage = new Dictionary<string, (double Lower, double Upper)>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            var values = observations.Select(value => value.Params[variable.Name]).ToArray();
            var observedLower = values.Min();
            var observedUpper = values.Max();
            var margin = Math.Max(
                CoverageRelativeMargin * (observedUpper - observedLower),
                CoverageMinimumStep * (variable.High - variable.Low));
            coverage[variable.Name] = (
                Math.Max(variable.Low, observedLower - margin),
                Math.Min(variable.High, observedUpper + margin));
        }
        return coverage;
    }

    private static double CoverageToleranceFor(OptimizerVariableInput variable)
        => CoverageTolerance * Math.Max(variable.High - variable.Low, 1);

    private static void ValidateReportedCoverage(
        OptimizerCoverageEnvelope? envelope,
        IReadOnlyDictionary<string, (double Lower, double Upper)> coverage,
        IReadOnlyDictionary<string, OptimizerVariableInput> variables)
    {
        if (envelope is null)
            throw new ProcessResearchRuleException(
                "优化服务未报告观察覆盖包络，无法确认建议落在历史运行的覆盖范围内。");
        var reported = envelope.Variables.ToDictionary(static value => value.Name, StringComparer.Ordinal);
        if (!reported.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(coverage.Keys))
            throw new ProcessResearchRuleException("观察覆盖包络的变量集合与本次优化输入不一致。");
        foreach (var (code, bounds) in coverage)
        {
            var tolerance = CoverageToleranceFor(variables[code]);
            if (Math.Abs(reported[code].Lower - bounds.Lower) > tolerance ||
                Math.Abs(reported[code].Upper - bounds.Upper) > tolerance)
                throw new ProcessResearchRuleException(
                    $"优化服务报告的变量 {code} 覆盖范围与平台依据同一批运行计算的结果不一致。");
        }
    }

    private static void ValidateObservedCoverage(
        IReadOnlyDictionary<string, (double Lower, double Upper)> coverage,
        IReadOnlyDictionary<string, OptimizerVariableInput> variables,
        OptimizerSuggestionOutput value)
    {
        foreach (var (code, parameter) in value.RecommendedParameters)
        {
            var (lower, upper) = coverage[code];
            var tolerance = CoverageToleranceFor(variables[code]);
            if (parameter < lower - tolerance || parameter > upper + tolerance)
                throw new ProcessResearchRuleException(
                    $"配方建议的变量 {code} 超出历史运行的观察覆盖范围。");
        }
    }

    private static int CommonProcessFeatureCount(IReadOnlyList<OptimizerObservationInput> observations)
    {
        var common = observations[0].ProcessFeatures.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var value in observations.Skip(1)) common.IntersectWith(value.ProcessFeatures.Keys);
        return common.Count;
    }

    private static Guid CreateDeterministicRecommendationId(
        string siteCode,
        string processSpecificationId,
        string inputHash)
        => new(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            SiteCode = siteCode,
            ProcessSpecificationId = processSpecificationId,
            InputHash = inputHash
        })).AsSpan(0, 16));
}
