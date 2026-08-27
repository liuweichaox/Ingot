// 负责把真实配方运行转为受门禁约束的下一配方建议；受控验证保留独立审批边界。
using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ResearchAssets;

namespace Ingot.Platform.Application.ProcessResearch;

/// <summary>
/// 从真实生产运行生成独立的下一配方建议，并为可选受控验证执行方法准入。
/// </summary>
public sealed class ResearchOptimizationService(
    IProcessResearchStore store,
    IProcessOptimizerClient optimizerClient,
    IResearchObservationAssembler observationAssembler,
    ResearchExperimentResultMaterializer resultMaterializer,
    ResearchExperimentCommands experimentCommands,
    ProcessResearchWorkflow workflow,
    ResearchOnlineAdmissionService? onlineAdmission = null,
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
            ?? throw new ProcessResearchRuleException("优化任务不存在。");
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的优化任务保持只读。");

        var mechanismKnowledge = mechanismKnowledgeStore is null
            ? new AppliedMechanismKnowledge([], [], [], [])
            : MechanismKnowledgeExperimentPolicy.Select(
                project,
                await mechanismKnowledgeStore.ListClaimsAsync(projectId, ct).ConfigureAwait(false),
                await mechanismKnowledgeStore.ListConflictsAsync(projectId, ct).ConfigureAwait(false));
        var mechanismKnowledgeSnapshotHash =
            MechanismKnowledgeExperimentPolicy.SnapshotHash(mechanismKnowledge);
        var mechanismModels = researchAssetStore is null
            ? new AppliedMechanismModels([], [])
            : MechanismModelExperimentPolicy.Select(
                project,
                await researchAssetStore.ListMechanismModelsAsync(ct).ConfigureAwait(false),
                await researchAssetStore.ListMechanismFusionsAsync(ct).ConfigureAwait(false));

        var objectiveCodes = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var constraintCodes = project.OutcomeConstraints.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var experiments = await store.ListExperimentsAsync(projectId, ct).ConfigureAwait(false);
        var experimentResults = await store.ListExperimentResultsAsync(projectId, ct)
            .ConfigureAwait(false);
        var assembled = await observationAssembler.AssembleProductionRunsAsync(project, ct)
            .ConfigureAwait(false);
        if (experiments.Count > 0)
        {
            var plannedAssembly = await observationAssembler.AssembleAsync(project, experiments, ct)
                .ConfigureAwait(false);
            var materialized = await resultMaterializer.MaterializeCompletedAsync(
                project,
                experiments,
                experimentResults,
                plannedAssembly,
                userId,
                ct).ConfigureAwait(false);
            if (materialized.Count > 0)
                experimentResults = experimentResults.Concat(materialized).ToArray();
        }

        var persisted = experimentResults
            .SelectMany(static result => result.RunObservations)
            .Where(value => value.ValidForOptimization &&
                            value.Outcomes.Keys.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(objectiveCodes) &&
                            value.ConstraintOutcomes.Keys.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(constraintCodes));
        var validProductionRuns = assembled.Observations
            .Where(value => value.ValidForOptimization &&
                            value.Outcomes.Keys.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(objectiveCodes) &&
                            value.ConstraintOutcomes.Keys.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(constraintCodes))
            .ToArray();
        var sourceObservations = persisted
            .Concat(validProductionRuns)
            .GroupBy(static value => value.ExecutionKey, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static value => value.ExecutionKey, StringComparer.Ordinal)
            .ToArray();
        var observedExecutionKeys = sourceObservations.Select(static value => value.ExecutionKey)
            .ToHashSet(StringComparer.Ordinal);
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        var observations = sourceObservations
            .Where(value => value.ActualFactors.Select(static factor => factor.VariableCode)
                .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
            .Select(static value => new OptimizerObservationInput
            {
                Params = value.ActualFactors.ToDictionary(
                    static factor => factor.VariableCode,
                    static factor => factor.Value,
                    StringComparer.Ordinal),
                Outcomes = value.Outcomes,
                ConstraintOutcomes = value.ConstraintOutcomes,
                ProcessFeatures = value.ProcessFeatures
            })
            .ToArray();
        if (observations.Length < 3)
            throw new ProcessResearchRuleException(
                "至少需要 3 条具有实际配方参数和有效质量结果的生产运行，才能生成下一配方建议。");
        var distinctRecipes = observations
            .Select(value => string.Join('|', value.Params
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key}:{pair.Value:R}")))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (distinctRecipes < 2)
            throw new ProcessResearchRuleException(
                "当前生产记录只有一种实际配方，尚无法比较配方效果或推荐下一配方。");

        var pendingPoints = experiments
            .Where(static experiment => experiment.Status is ResearchExperimentStatuses.Planned
                or ResearchExperimentStatuses.Approved
                or ResearchExperimentStatuses.Running)
            .Where(static experiment => experiment.Optimization?.Mode is not ResearchOptimizationModes.Shadow)
            .Where(experiment => experiment.Optimization is null || string.Equals(
                experiment.Optimization.MechanismKnowledgeSnapshotHash,
                mechanismKnowledgeSnapshotHash,
                StringComparison.Ordinal) && string.Equals(
                experiment.Optimization.MechanismModelSnapshotHash,
                mechanismModels.SnapshotHash,
                StringComparison.Ordinal))
            .SelectMany(static experiment => experiment.RunPlan)
            .Where(run => !observedExecutionKeys.Contains(run.ExecutionKey) &&
                          run.Factors.Select(static value => value.VariableCode)
                              .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
            .Select(run => (IReadOnlyDictionary<string, double>)run.Factors
                .OrderBy(static value => value.VariableCode, StringComparer.Ordinal)
                .ToDictionary(
                    static value => value.VariableCode,
                    static value => value.Value,
                    StringComparer.Ordinal))
            .Distinct(DictionaryValueComparer.Instance)
            .ToArray();
        var optimizerTopK = mechanismKnowledge.RankingConstraints.Count == 0 ? 1 : 4;
        var call = new OptimizerSuggestionCall
        {
            Campaign = MechanismModelExperimentPolicy.Apply(
                MechanismKnowledgeExperimentPolicy.ApplyHardConstraints(
                    BuildCampaign(project, ResearchOptimizationIntents.ReachSpecification, null),
                    mechanismKnowledge),
                mechanismModels),
            Observations = observations,
            PendingPoints = pendingPoints,
            TopK = optimizerTopK,
            Seed = request.Seed
        };
        var inputHash = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Operation = "next-recipe-recommendation",
                OptimizerCall = call,
                MechanismClaims = mechanismKnowledge.Claims.Select(static value => new
                {
                    value.ClaimId,
                    value.Version,
                    value.ContentHash
                }),
                MechanismModels = mechanismModels.References
            })));
        var existing = await store.GetRecipeRecommendationByInputHashAsync(
            projectId, inputHash, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            await SaveRecipeKnowledgeUsagesAsync(
                existing.RecommendationId, mechanismKnowledge, ct).ConfigureAwait(false);
            return existing;
        }

        var response = await optimizerClient.SuggestAsync(call, ct).ConfigureAwait(false);
        if (response.ObservationCount != observations.Length ||
            response.Suggestions.Count != optimizerTopK)
            throw new ProcessResearchRuleException("优化服务使用的数据快照或返回的配方建议数量不一致。");
        foreach (var candidate in response.Suggestions)
        {
            ValidateSuggestion(project, response.ModelVersion, candidate);
            MechanismKnowledgeExperimentPolicy.ValidateHardConstraints(candidate, mechanismKnowledge);
        }
        var suggestion = MechanismKnowledgeExperimentPolicy.Rank(
                response.Suggestions, mechanismKnowledge, controls)
            .First();
        ValidateControlledSuggestionInObservedEnvelope(suggestion, observations);

        var recommendationId = CreateDeterministicOptimizationId(projectId, inputHash);
        var recommendationKey = $"recipe-{recommendationId:N}"[..22];
        var generatedAt = DateTimeOffset.UtcNow;
        var recommendation = new ResearchRecipeRecommendation
        {
            RecommendationId = recommendationId,
            ProjectId = projectId,
            ProjectRevision = project.Revision,
            ModelVersion = response.ModelVersion,
            InputHash = inputHash,
            ObservationCount = observations.Length,
            AutoAssembledObservationCount = validProductionRuns.Length,
            PendingControlledValidationCount = pendingPoints.Length,
            ProcessFeatureCount = CommonProcessFeatureCount(observations),
            FeatureSetId = project.OptimizationFeatures.FeatureSetId,
            FeatureSetVersion = project.OptimizationFeatures.Version,
            DerivedFeatureCount = call.Campaign.DerivedFeatures.Count,
            MechanismKnowledgeSnapshotHash = mechanismKnowledgeSnapshotHash,
            MechanismModelSnapshotHash = mechanismModels.SnapshotHash,
            MechanismModels = mechanismModels.References,
            Items =
            [
                new ResearchRecipeRecommendationItem
                {
                    RecommendationKey = recommendationKey,
                    Parameters = suggestion.RecommendedParameters.Select(pair =>
                        new ResearchVariableSetting
                        {
                            VariableCode = pair.Key,
                            Value = pair.Value,
                            Unit = controls[pair.Key].Unit
                        }).ToArray(),
                    Prediction = MapPrediction(recommendationKey, suggestion)
                }
            ],
            CreatedBy = userId,
            GeneratedAt = generatedAt
        };
        var audit = new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = projectId,
            ResourceType = "recipe-recommendation",
            ResourceId = recommendationId.ToString(),
            Action = "created",
            UserId = userId,
            CreatedAt = generatedAt
        };
        try
        {
            var saved = await store.CreateRecipeRecommendationTransactionAsync(
                recommendation, audit, ct).ConfigureAwait(false);
            await SaveRecipeKnowledgeUsagesAsync(
                saved.RecommendationId, mechanismKnowledge, ct).ConfigureAwait(false);
            return saved;
        }
        catch (ProcessResearchRuleException)
        {
            var concurrent = await store.GetRecipeRecommendationAsync(recommendationId, ct)
                .ConfigureAwait(false);
            if (concurrent is not null &&
                string.Equals(concurrent.InputHash, inputHash, StringComparison.Ordinal))
            {
                await SaveRecipeKnowledgeUsagesAsync(
                    concurrent.RecommendationId, mechanismKnowledge, ct).ConfigureAwait(false);
                return concurrent;
            }
            throw;
        }
    }

    public async Task<ResearchMethodAdmissionAssessment> AssessMethodAdmissionAsync(
        Guid projectId,
        CancellationToken ct = default)
    {
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        var mechanismKnowledge = mechanismKnowledgeStore is null
            ? new AppliedMechanismKnowledge([], [], [], [])
            : MechanismKnowledgeExperimentPolicy.Select(
                project,
                await mechanismKnowledgeStore.ListClaimsAsync(projectId, ct).ConfigureAwait(false),
                await mechanismKnowledgeStore.ListConflictsAsync(projectId, ct).ConfigureAwait(false));
        var mechanismModels = researchAssetStore is null
            ? new AppliedMechanismModels([], [])
            : MechanismModelExperimentPolicy.Select(
                project,
                await researchAssetStore.ListMechanismModelsAsync(ct).ConfigureAwait(false),
                await researchAssetStore.ListMechanismFusionsAsync(ct).ConfigureAwait(false));
        return await AssessMethodAdmissionAsync(
                projectId,
                MechanismKnowledgeExperimentPolicy.SnapshotHash(mechanismKnowledge),
                mechanismModels.SnapshotHash,
                ct)
            .ConfigureAwait(false);
    }

    public async Task<ResearchExperiment> CreateNextExperimentAsync(
        Guid projectId,
        ResearchOptimizationRequest request,
        string userId,
        CancellationToken ct = default)
    {
        if (request.BatchSize is < 1 or > 8)
            throw new ProcessResearchRuleException("每批优化实验数量必须在 1 到 8 之间。");
        if (request.ReplicatesPerCondition is < 1 or > 5 ||
            request.BatchSize * request.ReplicatesPerCondition > 40)
            throw new ProcessResearchRuleException("每个条件的重复次数必须在 1 到 5 之间，单批总运行数不能超过 40。");
        var project = await store.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ProcessResearchRuleException("研发项目不存在。");
        if (project.Status is ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived)
            throw new ProcessResearchRuleException("已完成或已归档的研发项目保持只读。");
        var mechanismKnowledge = mechanismKnowledgeStore is null
            ? new AppliedMechanismKnowledge([], [], [], [])
            : MechanismKnowledgeExperimentPolicy.Select(
                project,
                await mechanismKnowledgeStore.ListClaimsAsync(projectId, ct).ConfigureAwait(false),
                await mechanismKnowledgeStore.ListConflictsAsync(projectId, ct).ConfigureAwait(false));
        var mechanismKnowledgeSnapshotHash =
            MechanismKnowledgeExperimentPolicy.SnapshotHash(mechanismKnowledge);
        var mechanismModels = researchAssetStore is null
            ? new AppliedMechanismModels([], [])
            : MechanismModelExperimentPolicy.Select(
                project,
                await researchAssetStore.ListMechanismModelsAsync(ct).ConfigureAwait(false),
                await researchAssetStore.ListMechanismFusionsAsync(ct).ConfigureAwait(false));
        var intent = NormalizeIntent(request.Intent);
        var mode = NormalizeMode(request.Mode);
        ResearchMethodAdmissionEvidence? methodAdmissionEvidence = null;
        if (intent == ResearchOptimizationIntents.ReachSpecification)
        {
            methodAdmissionEvidence = await RequireMethodAdmissionAsync(
                    projectId,
                    mechanismKnowledgeSnapshotHash,
                    mechanismModels.SnapshotHash,
                    ct)
                .ConfigureAwait(false);
        }
        ResearchOnlineAdmissionEvidence? onlineAdmissionEvidence = null;
        if (mode == ResearchOptimizationModes.Controlled)
        {
            if (request.BatchSize != 1 || request.ReplicatesPerCondition != 1)
                throw new ProcessResearchRuleException("受控在线每次只能生成一条建议和一次运行。");
            if (intent != ResearchOptimizationIntents.ReachSpecification || request.HypothesisId is not null)
                throw new ProcessResearchRuleException("受控在线只执行已经过影子验证的逼近规格建议；假设验证仍使用离线实验设计。");
            if (onlineAdmission is null)
                throw new ProcessResearchRuleException("受控在线准入服务不可用，按失败关闭处理。");
            onlineAdmissionEvidence = await onlineAdmission.RequireAsync(
                    projectId, mechanismKnowledgeSnapshotHash, ct)
                .ConfigureAwait(false);
        }
        if (intent == ResearchOptimizationIntents.ValidateHypothesis &&
            (request.BatchSize < 2 || request.ReplicatesPerCondition < 2))
        {
            throw new ProcessResearchRuleException(
                "假设验证至少需要两个干预条件，并在两个区组中重复，不能用单点运行升级原因证据。");
        }
        ResearchHypothesis? hypothesis = null;
        if (intent == ResearchOptimizationIntents.ValidateHypothesis)
        {
            if (request.HypothesisId is not { } hypothesisId)
                throw new ProcessResearchRuleException("验证假设的优化必须指定研发假设。");
            hypothesis = await store.GetHypothesisAsync(hypothesisId, ct).ConfigureAwait(false);
            if (hypothesis is null || hypothesis.ProjectId != projectId)
                throw new ProcessResearchRuleException("待验证的研发假设不属于当前项目。");
            if (hypothesis.ValidationOutcomeCode is null ||
                hypothesis.ExpectedEffectDirection is null || hypothesis.MinimumEffect is null)
            {
                throw new ProcessResearchRuleException(
                    "验证假设前必须定义验证目标、预期效应方向和最小效应。");
            }
        }
        else if (request.HypothesisId is not null)
        {
            throw new ProcessResearchRuleException("逼近规格的优化不能附带研发假设。");
        }

        var objectiveCodes = project.Objectives.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var constraintCodes = project.OutcomeConstraints.Select(static value => value.Code)
            .ToHashSet(StringComparer.Ordinal);
        var experiments = await store.ListExperimentsAsync(projectId, ct).ConfigureAwait(false);
        var shadowRecommendations = await store.ListShadowRecommendationsAsync(projectId, ct)
            .ConfigureAwait(false);
        var experimentResults = await store.ListExperimentResultsAsync(projectId, ct)
            .ConfigureAwait(false);
        var assembled = request.AutoAssembleObservations
            ? await observationAssembler.AssembleProductionRunsAsync(project, ct).ConfigureAwait(false)
            : new ResearchObservationAssembly([], 0);
        if (request.AutoAssembleObservations && experiments.Count > 0)
        {
            var plannedAssembly = await observationAssembler.AssembleAsync(project, experiments, ct)
                .ConfigureAwait(false);
            var materialized = await resultMaterializer.MaterializeCompletedAsync(
                project,
                experiments,
                experimentResults,
                plannedAssembly,
                userId,
                ct).ConfigureAwait(false);
            if (materialized.Count > 0)
                experimentResults = experimentResults.Concat(materialized).ToArray();
        }
        var persisted = experimentResults
            .SelectMany(static result => result.RunObservations)
            .Where(value => value.ValidForOptimization &&
                            value.Outcomes.Keys.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(objectiveCodes) &&
                            value.ConstraintOutcomes.Keys.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(constraintCodes))
            .ToArray();
        var validAuto = assembled.Observations
            .Where(value => value.ValidForOptimization &&
                            value.Outcomes.Keys.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(objectiveCodes) &&
                            value.ConstraintOutcomes.Keys.ToHashSet(StringComparer.Ordinal)
                                .SetEquals(constraintCodes))
            .ToArray();
        var sourceObservations = persisted
            .Concat(validAuto)
            .GroupBy(static value => value.ExecutionKey, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .OrderBy(static value => value.ExecutionKey, StringComparer.Ordinal)
            .ToArray();
        var observedExecutionKeys = sourceObservations.Select(static value => value.ExecutionKey)
            .ToHashSet(StringComparer.Ordinal);
        var decidedShadowRuns = shadowRecommendations
            .Select(static value => value.SuggestionExecutionKey)
            .ToHashSet(StringComparer.Ordinal);
        var activeOptimization = experiments
            .Where(experiment =>
                experiment.Optimization is { } optimization &&
                string.Equals(optimization.Intent, intent, StringComparison.Ordinal) &&
                string.Equals(optimization.Mode, mode, StringComparison.Ordinal) &&
                optimization.HypothesisId == hypothesis?.HypothesisId &&
                (experiment.Status is ResearchExperimentStatuses.Planned
                    or ResearchExperimentStatuses.Approved
                    or ResearchExperimentStatuses.Running) &&
                experiment.RunPlan.Any(run => mode == ResearchOptimizationModes.Shadow
                    ? !decidedShadowRuns.Contains(run.ExecutionKey)
                    : !observedExecutionKeys.Contains(run.ExecutionKey)))
            .OrderByDescending(static value => value.CreatedAt)
            .FirstOrDefault();
        if (activeOptimization is not null && string.Equals(
                activeOptimization.Optimization?.MechanismKnowledgeSnapshotHash,
                mechanismKnowledgeSnapshotHash,
                StringComparison.Ordinal) && string.Equals(
                activeOptimization.Optimization?.MechanismModelSnapshotHash,
                mechanismModels.SnapshotHash,
                StringComparison.Ordinal))
        {
            MechanismKnowledgeExperimentPolicy.ValidateHardConstraints(activeOptimization, mechanismKnowledge);
            await CompleteExperimentSideEffectsAsync(
                activeOptimization, mechanismKnowledge, hypothesis, projectId, userId, ct).ConfigureAwait(false);
            return activeOptimization;
        }

        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (hypothesis is not null && !hypothesis.VariableCodes.Any(controls.ContainsKey))
        {
            throw new ProcessResearchRuleException(
                "待验证的研发假设必须至少关联一个可控变量，才能设计安全的验证实验。");
        }
        var observations = sourceObservations
            .Where(value => value.ActualFactors.Select(static factor => factor.VariableCode)
                .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
            .Select(static value => new OptimizerObservationInput
            {
                Params = value.ActualFactors.ToDictionary(
                    static factor => factor.VariableCode,
                    static factor => factor.Value,
                    StringComparer.Ordinal),
                Outcomes = value.Outcomes,
                ConstraintOutcomes = value.ConstraintOutcomes,
                ProcessFeatures = value.ProcessFeatures
            })
            .ToArray();
        var pendingPoints = experiments
            .Where(static experiment => experiment.Status is ResearchExperimentStatuses.Planned
                or ResearchExperimentStatuses.Approved
                or ResearchExperimentStatuses.Running)
            .Where(static experiment =>
                experiment.Optimization?.Mode is not ResearchOptimizationModes.Shadow)
            .Where(experiment => experiment.Optimization is null || string.Equals(
                experiment.Optimization.MechanismKnowledgeSnapshotHash,
                mechanismKnowledgeSnapshotHash,
                StringComparison.Ordinal) && string.Equals(
                experiment.Optimization.MechanismModelSnapshotHash,
                mechanismModels.SnapshotHash,
                StringComparison.Ordinal))
            .SelectMany(static experiment => experiment.RunPlan)
            .Where(run => !observedExecutionKeys.Contains(run.ExecutionKey) &&
                          run.Factors.Select(static value => value.VariableCode)
                              .ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
            .Select(run => (IReadOnlyDictionary<string, double>)run.Factors
                .OrderBy(static value => value.VariableCode, StringComparer.Ordinal)
                .ToDictionary(
                    static value => value.VariableCode,
                    static value => value.Value,
                    StringComparer.Ordinal))
            .Distinct(DictionaryValueComparer.Instance)
            .ToArray();
        var optimizerTopK = mechanismKnowledge.RankingConstraints.Count == 0
            ? request.BatchSize
            : Math.Min(request.BatchSize * 4, 32);
        var call = new OptimizerSuggestionCall
        {
            Campaign = MechanismModelExperimentPolicy.Apply(
                MechanismKnowledgeExperimentPolicy.ApplyHardConstraints(
                    BuildCampaign(project, intent, hypothesis), mechanismKnowledge),
                mechanismModels),
            Observations = observations,
            PendingPoints = pendingPoints,
            TopK = optimizerTopK,
            Seed = request.Seed
        };
        var inputHash = Convert.ToHexStringLower(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                optimizerCall = call,
                request.ReplicatesPerCondition,
                mode,
                MethodReplayHash = methodAdmissionEvidence?.HistoricalReplayReportHash,
                OnlineReplayHash = onlineAdmissionEvidence?.HistoricalReplayReportHash,
                OnlineShadowHash = onlineAdmissionEvidence?.ShadowReportHash,
                OnlineRollbackHash = onlineAdmissionEvidence?.RollbackDrillRecordHash,
                MechanismClaims = mechanismKnowledge.Claims.Select(static value => new
                {
                    value.ClaimId,
                    value.Version,
                    value.ContentHash
                }),
                MechanismModels = mechanismModels.References
            })));
        var existing = experiments
            .Where(experiment => string.Equals(
                experiment.Optimization?.InputHash,
                inputHash,
                StringComparison.Ordinal))
            .OrderByDescending(static value => value.CreatedAt)
            .FirstOrDefault();
        if (existing is not null)
        {
            await CompleteExperimentSideEffectsAsync(
                existing, mechanismKnowledge, hypothesis, projectId, userId, ct).ConfigureAwait(false);
            return existing;
        }
        var response = await optimizerClient.SuggestAsync(call, ct).ConfigureAwait(false);
        if (methodAdmissionEvidence is not null &&
            !methodAdmissionEvidence.OptimizerModelVersions.Contains(
                response.ModelVersion,
                StringComparer.Ordinal))
        {
            throw new ProcessResearchRuleException(
                $"序贯优化已暂停：当前优化器模型 {response.ModelVersion} 未出现在通过审核的历史回放中。" +
                "请用当前模型重新运行并审核历史回放，或降级为响应面或适用 DOE。");
        }
        if (response.ObservationCount != observations.Length ||
            response.Suggestions.Count != optimizerTopK)
            throw new ProcessResearchRuleException("优化服务使用的数据快照或返回的实验数量不一致。");
        foreach (var suggestion in response.Suggestions)
        {
            ValidateSuggestion(project, response.ModelVersion, suggestion);
            MechanismKnowledgeExperimentPolicy.ValidateHardConstraints(suggestion, mechanismKnowledge);
        }
        var rankedSuggestions = MechanismKnowledgeExperimentPolicy.Rank(
                response.Suggestions, mechanismKnowledge, controls)
            .Take(request.BatchSize)
            .ToArray();
        EnsureExperimentConditionsAreDistinguishable(
            rankedSuggestions,
            observations,
            controls);

        var experimentId = CreateDeterministicOptimizationId(projectId, inputHash);
        var runPlan = new List<ExperimentRunPlan>();
        var predictions = new List<OptimizationRunPrediction>();
        var sequence = 1;
        for (var replicate = 0; replicate < request.ReplicatesPerCondition; replicate++)
        {

            for (var position = 0; position < rankedSuggestions.Length; position++)
            {
                var index = (position + replicate) % rankedSuggestions.Length;
                var suggestion = rankedSuggestions[index];
                if (mode is ResearchOptimizationModes.Controlled)
                    ValidateControlledSuggestionInObservedEnvelope(suggestion, observations);
                var executionKey = $"bo-{experimentId:N}"[..15] +
                             $"-{index + 1:D2}-r{replicate + 1:D2}";
                runPlan.Add(new ExperimentRunPlan
                {
                    ExecutionKey = executionKey,
                    Sequence = sequence++,
                    BlockKey = $"block-{replicate + 1:D2}",
                    ReplicateKey = $"condition-{index + 1:D2}",
                    Factors = suggestion.RecommendedParameters.Select(pair =>
                        new ResearchVariableSetting
                        {
                            VariableCode = pair.Key,
                            Value = pair.Value,
                            Unit = controls[pair.Key].Unit
                        }).ToArray()
                });
                predictions.Add(MapPrediction(executionKey, suggestion));
            }
        }

        var generatedExperiment = new ResearchExperiment
        {
            ExperimentId = experimentId,
            HypothesisId = hypothesis?.HypothesisId,
            Name = mode == ResearchOptimizationModes.Shadow
                ? $"影子优化建议 {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}"
                : mode == ResearchOptimizationModes.Controlled
                ? $"受控在线建议 {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}"
                : intent == ResearchOptimizationIntents.ValidateHypothesis
                ? $"假设验证实验 {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}"
                : $"智能优化实验 {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm}",
            DesignMethod = ResearchDesignMethods.BayesianOptimization,
            BlockingKeys = runPlan.Select(static value => value.BlockKey!)
                .Distinct(StringComparer.Ordinal).ToArray(),
            ReplicateKeys = runPlan.Select(static value => value.ReplicateKey!)
                .Distinct(StringComparer.Ordinal).ToArray(),
            RunPlan = runPlan,
            ObjectiveCodes = project.Objectives.Select(static value => value.Code).ToArray(),
            StopRule = mode == ResearchOptimizationModes.Shadow
                ? "只记录旁路建议和工程师实际选择，不批准、不下发、不改变现场实验顺序。"
                : mode == ResearchOptimizationModes.Controlled
                ? "一次只执行一条经工程师确认的建议；任一安全约束、数据失效、设置偏差或现场异常立即停止。"
                : intent == ResearchOptimizationIntents.ValidateHypothesis
                ? "获得足以支持、推翻或保留该假设的置信区间，或工程师因安全边界终止。"
                : "达到项目目标并完成重复性确认，或工程师因安全边界终止。",
            RollbackPlan = mode == ResearchOptimizationModes.Shadow
                ? "影子建议不下发设备，因此不触发现场回退。"
                : mode == ResearchOptimizationModes.Controlled
                ? "停止后不生成下一条建议，恢复上一组经现场确认的安全参数，并由工程师复核数据与设备状态。"
                : "任何安全约束触发时立即停止，并恢复上一组已批准工艺参数。",
            Optimization = new ResearchOptimizationMetadata
            {
                ModelVersion = response.ModelVersion,
                InputHash = inputHash,
                ObservationCount = observations.Length,
                AutoAssembledObservationCount = validAuto.Length,
                PendingExperimentCount = pendingPoints.Length,
                ProcessFeatureCount = CommonProcessFeatureCount(observations),
                FeatureSetId = project.OptimizationFeatures.FeatureSetId,
                MechanismKnowledgeSnapshotHash = mechanismKnowledgeSnapshotHash,
                MechanismModelSnapshotHash = mechanismModels.SnapshotHash,
                MechanismModels = mechanismModels.References,
                FeatureSetVersion = project.OptimizationFeatures.Version,
                DerivedFeatureCount = call.Campaign.DerivedFeatures.Count,
                Intent = intent,
                Mode = mode,
                HypothesisId = hypothesis?.HypothesisId,
                DistinctConditionCount = rankedSuggestions.Length,
                ReplicatesPerCondition = request.ReplicatesPerCondition,
                BlockCount = request.ReplicatesPerCondition,
                RunPredictions = predictions,
                MethodAdmission = methodAdmissionEvidence,
                OnlineAdmission = onlineAdmissionEvidence,
                GeneratedAt = DateTimeOffset.UtcNow
            }
        };
        try
        {
            var saved = await experimentCommands.CreateExperimentAsync(
                projectId,
                generatedExperiment,
                userId,
                ct).ConfigureAwait(false);
            await CompleteExperimentSideEffectsAsync(
                saved, mechanismKnowledge, hypothesis, projectId, userId, ct).ConfigureAwait(false);
            return saved;
        }
        catch (ProcessResearchRuleException)
        {

            var concurrent = await store.GetExperimentAsync(experimentId, ct).ConfigureAwait(false);
            if (concurrent?.Optimization?.InputHash is { } concurrentInputHash &&
                string.Equals(concurrentInputHash, inputHash, StringComparison.Ordinal))
            {
                await CompleteExperimentSideEffectsAsync(
                    concurrent, mechanismKnowledge, hypothesis, projectId, userId, ct).ConfigureAwait(false);
                return concurrent;
            }
            throw;
        }
    }

    private async Task<ResearchMethodAdmissionEvidence> RequireMethodAdmissionAsync(
        Guid projectId,
        string mechanismKnowledgeSnapshotHash,
        string mechanismModelSnapshotHash,
        CancellationToken ct)
    {
        var assessment = await AssessMethodAdmissionAsync(
                projectId,
                mechanismKnowledgeSnapshotHash,
                mechanismModelSnapshotHash,
                ct)
            .ConfigureAwait(false);
        if (!assessment.Eligible)
        {
            throw new ProcessResearchRuleException(
                "序贯优化已暂停：" + string.Join("；", assessment.Failures) +
                "。请降级为正则化响应面或适用 DOE，不得继续生成优化建议。");
        }

        return new ResearchMethodAdmissionEvidence
        {
            ValidationPolicyVersion = assessment.ValidationPolicyVersion,
            HistoricalReplayReportId = assessment.HistoricalReplayReportId!.Value,
            HistoricalReplayReportHash = assessment.HistoricalReplayReportHash!,
            BaselineMethods = assessment.BaselineMethods,
            OptimizerModelVersions = assessment.OptimizerModelVersions,
            MechanismKnowledgeSnapshotHash = assessment.MechanismKnowledgeSnapshotHash,
            MechanismModelSnapshotHash = assessment.MechanismModelSnapshotHash,
            AssessedAt = assessment.AssessedAt
        };
    }

    private async Task<ResearchMethodAdmissionAssessment> AssessMethodAdmissionAsync(
        Guid projectId,
        string mechanismKnowledgeSnapshotHash,
        string mechanismModelSnapshotHash,
        CancellationToken ct)
    {
        var latest = (await store.ListHistoricalReplayReportsAsync(projectId, ct)
                .ConfigureAwait(false))
            .Where(value => string.Equals(
                    value.ValidationPolicyVersion,
                    ValidationThresholds.PolicyVersion,
                    StringComparison.Ordinal) &&
                string.Equals(
                    value.MechanismKnowledgeSnapshotHash,
                    mechanismKnowledgeSnapshotHash,
                    StringComparison.Ordinal) &&
                string.Equals(
                    value.MechanismModelSnapshotHash,
                    mechanismModelSnapshotHash,
                    StringComparison.Ordinal))
            .OrderByDescending(static value => value.GeneratedAt)
            .ThenByDescending(static value => value.ReportId)
            .FirstOrDefault();
        var failures = new List<string>();
        if (latest is null)
            failures.Add("缺少与当前策略、机理知识和模型快照一致的历史回放");
        else if (latest.Status != ResearchHistoricalReplayStatuses.Reviewed)
            failures.Add("当前快照的最新历史回放尚未完成独立审核");
        else if (!latest.GatePassed)
            failures.Add(latest.GateFailures.Count == 0
                ? "历史回放没有通过方法准入门槛。"
                : string.Join("；", latest.GateFailures));
        if (latest is not null && latest.OptimizerModelVersions.Count == 0)
            failures.Add("最新历史回放没有冻结可核对的优化器模型版本");

        return new ResearchMethodAdmissionAssessment
        {
            ValidationPolicyVersion = ValidationThresholds.PolicyVersion,
            Eligible = failures.Count == 0,
            Failures = failures,
            HistoricalReplayReportId = latest?.ReportId,
            HistoricalReplayReportHash = latest?.ReportHash,
            BaselineMethods = latest?.BaselineMethods ?? [],
            OptimizerModelVersions = latest?.OptimizerModelVersions ?? [],
            MechanismKnowledgeSnapshotHash = mechanismKnowledgeSnapshotHash,
            MechanismModelSnapshotHash = mechanismModelSnapshotHash,
            AssessedAt = DateTimeOffset.UtcNow
        };
    }

    private Task SaveKnowledgeUsagesAsync(
        Guid recommendationId,
        AppliedMechanismKnowledge mechanismKnowledge,
        CancellationToken ct)
    {
        if (mechanismKnowledgeStore is null || mechanismKnowledge.Claims.Count == 0)
            return Task.CompletedTask;
        var usages = mechanismKnowledge.Claims.SelectMany(value => UsageTypes(value)
            .Select(usageType => new MechanismClaimUsage
            {
                RecommendationId = recommendationId,
                ClaimId = value.ClaimId,
                ClaimVersion = value.Version,
                UsageType = usageType,
                ContentHash = value.ContentHash
            })).ToArray();
        return mechanismKnowledgeStore.SaveUsagesAsync(usages, ct);
    }

    private Task SaveRecipeKnowledgeUsagesAsync(
        Guid recommendationId,
        AppliedMechanismKnowledge mechanismKnowledge,
        CancellationToken ct)
    {
        if (mechanismKnowledgeStore is null || mechanismKnowledge.Claims.Count == 0)
            return Task.CompletedTask;
        var usages = mechanismKnowledge.Claims.SelectMany(value => UsageTypes(value)
            .Select(usageType => new MechanismClaimUsage
            {
                RecommendationId = recommendationId,
                ClaimId = value.ClaimId,
                ClaimVersion = value.Version,
                UsageType = usageType,
                ContentHash = value.ContentHash
            })).ToArray();
        return mechanismKnowledgeStore.SaveRecipeRecommendationUsagesAsync(usages, ct);
    }

    private static IReadOnlyList<string> UsageTypes(MechanismClaimVersion claim)
    {
        var values = claim.Constraints.Select(static constraint => constraint.Severity == "hard"
                ? "hard-constraint"
                : "candidate-ranking")
            .ToList();
        if (claim.ForbiddenCombinations.Count > 0) values.Add("forbidden-combination");
        if (values.Count == 0) values.Add("knowledge-context");
        return values.Distinct(StringComparer.Ordinal).ToArray();
    }

    private async Task CompleteExperimentSideEffectsAsync(
        ResearchExperiment experiment,
        AppliedMechanismKnowledge mechanismKnowledge,
        ResearchHypothesis? hypothesis,
        Guid projectId,
        string userId,
        CancellationToken ct)
    {
        await SaveKnowledgeUsagesAsync(experiment.ExperimentId, mechanismKnowledge, ct)
            .ConfigureAwait(false);
        if (hypothesis is null) return;
        var current = await store.GetHypothesisAsync(hypothesis.HypothesisId, ct).ConfigureAwait(false);
        if (current?.Status == ResearchHypothesisStatuses.Proposed)
            await workflow.SaveHypothesisAsync(
                projectId,
                current with { Status = ResearchHypothesisStatuses.Selected },
                userId,
                ct).ConfigureAwait(false);
    }

    internal static OptimizerCampaignInput BuildCampaign(
        ResearchProject project,
        string intent,
        ResearchHypothesis? hypothesis)
    {
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToArray();
        if (controls.Length == 0 ||
            controls.Any(static value => value.LowerLimit is null || value.UpperLimit is null))
            throw new ProcessResearchRuleException("优化要求全部可控变量都定义上下界。");
        return new OptimizerCampaignInput
        {
            Name = project.Name,
            FeatureSetId = project.OptimizationFeatures.FeatureSetId,
            FeatureSetVersion = project.OptimizationFeatures.Version,
            DerivedFeatures = project.OptimizationFeatures.DerivedFeatures.Select(value =>
                new OptimizerDerivedFeatureInput
                {
                    Name = value.Name,
                    Operator = value.Operator,
                    Inputs = value.Inputs,
                    NormalizationOffset = value.NormalizationOffset,
                    NormalizationScale = value.NormalizationScale,
                    Epsilon = value.Epsilon
                }).ToArray(),
            DecisionIntent = intent,
            HypothesisVariables = hypothesis?.VariableCodes
                .Intersect(controls.Select(static value => value.Code), StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)
                .ToArray() ?? [],
            Variables = controls.Select(value => new OptimizerVariableInput(
                value.Code,
                value.LowerLimit!.Value,
                value.UpperLimit!.Value,
                value.Unit)).ToArray(),
            Objectives = project.Objectives.Select(MapObjective).ToArray(),
            Constraints = project.Constraints.Select(value =>
            {
                if (!controls.Any(control =>
                        control.Code == value.VariableCode))
                    throw new ProcessResearchRuleException(
                        $"优化约束 {value.Code} 必须引用可控变量。");
                return new OptimizerConstraintInput
                {
                    Variable = value.VariableCode,
                    Operator = value.Operator,
                    Limit = value.Limit,
                    SafetyCritical = value.SafetyCritical
                };
            }).ToArray(),
            OutcomeConstraints = project.OutcomeConstraints.Select(value =>
                new OptimizerOutcomeConstraintInput
                {
                    Name = value.Code,
                    Operator = value.Operator,
                    Limit = value.Limit,
                    Unit = value.Unit,
                    SafetyCritical = value.SafetyCritical,
                    MinimumProbability = value.MinimumProbability
                }).ToArray(),
            Context = project.Context
        };
    }

    private static string NormalizeIntent(string? value)
    {
        var intent = string.IsNullOrWhiteSpace(value)
            ? ResearchOptimizationIntents.ReachSpecification
            : value.Trim().ToLowerInvariant();
        if (!ResearchOptimizationIntents.IsValid(intent))
            throw new ProcessResearchRuleException("优化意图无效。");
        return intent;
    }

    private static string NormalizeMode(string? value)
    {
        var mode = string.IsNullOrWhiteSpace(value)
            ? ResearchOptimizationModes.Experiment
            : value.Trim().ToLowerInvariant();
        if (!ResearchOptimizationModes.IsValid(mode))
            throw new ProcessResearchRuleException("优化模式无效。");
        return mode;
    }

    private static OptimizationRunPrediction MapPrediction(
        string executionKey,
        OptimizerSuggestionOutput suggestion)
        => new()
        {
            ExecutionKey = executionKey,
            Objectives = suggestion.Predictions.ToDictionary(
                static pair => pair.Key,
                static pair => new OptimizationMetricPrediction
                {
                    Mean = pair.Value.Mean,
                    StandardDeviation = pair.Value.StandardDeviation,
                    Lower95 = pair.Value.Lower95,
                    Upper95 = pair.Value.Upper95,
                    Unit = pair.Value.Unit
                },
                StringComparer.Ordinal),
            Constraints = suggestion.ConstraintPredictions.ToDictionary(
                static pair => pair.Key,
                static pair => new OptimizationMetricPrediction
                {
                    Mean = pair.Value.Mean,
                    StandardDeviation = pair.Value.StandardDeviation,
                    Lower95 = pair.Value.Lower95,
                    Upper95 = pair.Value.Upper95,
                    Unit = pair.Value.Unit
                },
                StringComparer.Ordinal),
            FeasibilityProbability = suggestion.FeasibilityProbability,
            AcquisitionValue = suggestion.AcquisitionValue,
            ColdStart = suggestion.ColdStart,
            Rationale = suggestion.Rationale
        };

    private static OptimizerObjectiveInput MapObjective(ResearchObjective objective)
    {
        OptimizerObjectiveInput mapped = objective.Direction switch
        {
            "minimize" => new()
            {
                Name = objective.Code,
                Kind = "le",
                Threshold = objective.UpperLimit ?? objective.Target,
                Weight = objective.Weight,
                Unit = objective.Unit
            },
            "maximize" => new()
            {
                Name = objective.Code,
                Kind = "ge",
                Threshold = objective.LowerLimit ?? objective.Target,
                Weight = objective.Weight,
                Unit = objective.Unit
            },
            "range" when objective.LowerLimit is { } lower && objective.UpperLimit is { } upper =>
                new()
                {
                    Name = objective.Code,
                    Kind = "range",
                    Lower = lower,
                    Upper = upper,
                    Weight = objective.Weight,
                    Unit = objective.Unit
                },
            "target" when objective.LowerLimit is { } lower && objective.UpperLimit is { } upper =>
                new()
                {
                    Name = objective.Code,
                    Kind = "target",
                    Target = objective.Target,
                    Tol = Math.Min(objective.Target - lower, upper - objective.Target),
                    Weight = objective.Weight,
                    Unit = objective.Unit
                },
            _ => throw new ProcessResearchRuleException($"目标 {objective.Code} 的方向或规格定义不支持优化。")
        };
        return objective.DataSource?.Trim()
            .StartsWith("inspection-outcome:", StringComparison.OrdinalIgnoreCase) == true
            ? mapped with { OutcomeLowerBound = 0, OutcomeUpperBound = 1 }
            : mapped;
    }

    private static void ValidateSuggestion(
        ResearchProject project,
        string modelVersion,
        OptimizerSuggestionOutput output)
    {
        var controls = project.Variables
            .Where(static value => value.Role == ResearchVariableRoles.Control)
            .ToDictionary(static value => value.Code, StringComparer.Ordinal);
        if (!string.Equals(modelVersion, output.ModelVersion, StringComparison.Ordinal) ||
            !output.RecommendedParameters.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(controls.Keys))
            throw new ProcessResearchRuleException("优化建议的模型版本或变量集合无效。");
        foreach (var (code, value) in output.RecommendedParameters)
        {
            var variable = controls[code];
            if (!double.IsFinite(value) ||
                value < variable.LowerLimit || value > variable.UpperLimit)
                throw new ProcessResearchRuleException($"优化变量 {code} 超出项目范围。");
        }
        if (!output.ColdStart &&
            !output.Predictions.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(project.Objectives.Select(static value => value.Code)))
            throw new ProcessResearchRuleException("优化建议没有覆盖全部目标预测。");
        if (!output.ColdStart &&
            !output.ConstraintPredictions.Keys.ToHashSet(StringComparer.Ordinal)
                .SetEquals(project.OutcomeConstraints.Select(static value => value.Code)))
            throw new ProcessResearchRuleException("优化建议没有覆盖全部结果约束预测。");
    }

    internal static void EnsureExperimentConditionsAreDistinguishable(
        IReadOnlyList<OptimizerSuggestionOutput> suggestions,
        IReadOnlyList<OptimizerObservationInput> observations,
        IReadOnlyDictionary<string, ResearchVariable> controls)
    {
        if (suggestions.Count < 2)
            return;
        var observedResolution = controls.Keys.ToDictionary(
            static code => code,
            code => observations
                .Where(value => value.Params.ContainsKey(code))
                .Select(value => value.Params[code])
                .Distinct()
                .Order()
                .Zip(
                    observations
                        .Where(value => value.Params.ContainsKey(code))
                        .Select(value => value.Params[code])
                        .Distinct()
                        .Order()
                        .Skip(1),
                    static (left, right) => right - left)
                .Where(static gap => gap > 0)
                .DefaultIfEmpty(0)
                .Min(),
            StringComparer.Ordinal);
        if (observedResolution.Values.All(static value => value <= 0))
            return;
        for (var left = 0; left < suggestions.Count; left++)
            for (var right = left + 1; right < suggestions.Count; right++)
            {
                var distinguishable = controls.Keys.Any(code =>
                    observedResolution[code] > 0 &&
                    Math.Abs(
                        suggestions[left].RecommendedParameters[code] -
                        suggestions[right].RecommendedParameters[code]) + 1e-12 >= observedResolution[code]);
                if (!distinguishable)
                {
                    var resolution = string.Join("、", observedResolution
                        .Where(static pair => pair.Value > 0)
                        .Select(pair => $"{controls[pair.Key].Name} {pair.Value:G6} {controls[pair.Key].Unit}"));
                    throw new ProcessResearchRuleException(
                        $"优化服务返回的候选条件低于历史数据可区分分辨率（{resolution}），不能伪装成两个实验条件。");
                }
            }
    }

    private static int CommonProcessFeatureCount(
        IReadOnlyList<OptimizerObservationInput> observations)
    {
        if (observations.Count == 0)
            return 0;
        var common = observations[0].ProcessFeatures.Keys.ToHashSet(StringComparer.Ordinal);
        foreach (var observation in observations.Skip(1))
            common.IntersectWith(observation.ProcessFeatures.Keys);
        return common.Count;
    }

    private static Guid CreateDeterministicOptimizationId(Guid projectId, string inputHash)
    {
        var bytes = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            ProjectId = projectId,
            InputHash = inputHash
        }));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private sealed class DictionaryValueComparer :
        IEqualityComparer<IReadOnlyDictionary<string, double>>
    {
        public static DictionaryValueComparer Instance { get; } = new();

        public bool Equals(
            IReadOnlyDictionary<string, double>? left,
            IReadOnlyDictionary<string, double>? right)
            => ReferenceEquals(left, right) ||
               left is not null && right is not null &&
               left.Count == right.Count &&
               left.All(pair => right.TryGetValue(pair.Key, out var value) &&
                                value.Equals(pair.Value));

        public int GetHashCode(IReadOnlyDictionary<string, double> value)
        {
            var hash = new HashCode();
            foreach (var pair in value.OrderBy(static item => item.Key, StringComparer.Ordinal))
            {
                hash.Add(pair.Key, StringComparer.Ordinal);
                hash.Add(pair.Value);
            }
            return hash.ToHashCode();
        }
    }

    private static void ValidateControlledSuggestionInObservedEnvelope(
        OptimizerSuggestionOutput suggestion,
        IReadOnlyList<OptimizerObservationInput> observations)
    {
        if (observations.Count == 0)
            throw new ProcessResearchRuleException("受控在线必须有可重放的真实历史观察。");
        foreach (var (code, value) in suggestion.RecommendedParameters)
        {
            var observed = observations
                .Where(item => item.Params.ContainsKey(code))
                .Select(item => item.Params[code])
                .ToArray();
            if (observed.Length == 0 || value < observed.Min() || value > observed.Max())
                throw new ProcessResearchRuleException(
                    $"受控在线建议 {code}={value:R} 超出历史实测参数包络，必须退回影子模式验证。");
        }
    }
}
