using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Infrastructure.ResearchAssets;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
/// Turns the immutable observations already attached to experiment results into
/// the next ordinary experiment plan. The existing experiment approval and
/// execution state machine remains the only business workflow.
/// </summary>
public sealed class ResearchExperimentOptimizer(
    IProcessResearchStore store,
    IProcessOptimizerClient optimizerClient,
    IResearchObservationAssembler observationAssembler,
    ResearchExperimentResultMaterializer resultMaterializer,
    ResearchExperimentCommands experimentCommands,
    ProcessResearchWorkflow workflow,
    ResearchOnlineAdmissionService? onlineAdmission = null,
    IMechanismKnowledgeStore? mechanismKnowledgeStore = null)
{
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
            ? new AppliedMechanismKnowledge([], [], [])
            : MechanismKnowledgeExperimentPolicy.Select(
                project,
                await mechanismKnowledgeStore.ListClaimsAsync(projectId, ct).ConfigureAwait(false),
                await mechanismKnowledgeStore.ListConflictsAsync(projectId, ct).ConfigureAwait(false));
        var mechanismKnowledgeSnapshotHash =
            MechanismKnowledgeExperimentPolicy.SnapshotHash(mechanismKnowledge);
        var intent = NormalizeIntent(request.Intent);
        var mode = NormalizeMode(request.Mode);
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
            ? await observationAssembler.AssembleAsync(project, experiments, ct).ConfigureAwait(false)
            : new ResearchObservationAssembly([], 0);
        if (request.AutoAssembleObservations)
        {
            var materialized = await resultMaterializer.MaterializeCompletedAsync(
                project,
                experiments,
                experimentResults,
                assembled,
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
                experiment.Optimization?.Mode != ResearchOptimizationModes.Shadow)
            .Where(experiment => experiment.Optimization is null || string.Equals(
                experiment.Optimization.MechanismKnowledgeSnapshotHash,
                mechanismKnowledgeSnapshotHash,
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
            Campaign = MechanismKnowledgeExperimentPolicy.ApplyHardConstraints(
                BuildCampaign(project, intent, hypothesis), mechanismKnowledge),
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
                OnlineReplayHash = onlineAdmissionEvidence?.HistoricalReplayReportHash,
                OnlineShadowHash = onlineAdmissionEvidence?.ShadowReportHash,
                OnlineRollbackHash = onlineAdmissionEvidence?.RollbackDrillRecordHash,
                MechanismClaims = mechanismKnowledge.Claims.Select(static value => new
                {
                    value.ClaimId,
                    value.Version,
                    value.ContentHash
                })
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

        var experimentId = CreateDeterministicExperimentId(projectId, inputHash);
        var runPlan = new List<ExperimentRunPlan>();
        var predictions = new List<OptimizationRunPrediction>();
        var sequence = 1;
        for (var replicate = 0; replicate < request.ReplicatesPerCondition; replicate++)
        {
            // 让同一候选条件在不同区组中的顺序轮换，避免固定执行顺序与条件混杂。
            for (var position = 0; position < rankedSuggestions.Length; position++)
            {
                var index = (position + replicate) % rankedSuggestions.Length;
                var suggestion = rankedSuggestions[index];
                if (mode == ResearchOptimizationModes.Controlled)
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
                        new ExperimentFactorSetting
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
                FeatureSetVersion = project.OptimizationFeatures.Version,
                DerivedFeatureCount = project.OptimizationFeatures.DerivedFeatures.Count,
                Intent = intent,
                Mode = mode,
                HypothesisId = hypothesis?.HypothesisId,
                DistinctConditionCount = rankedSuggestions.Length,
                ReplicatesPerCondition = request.ReplicatesPerCondition,
                BlockCount = request.ReplicatesPerCondition,
                RunPredictions = predictions,
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
            // 两个并发请求会得到相同快照哈希和确定性 ID；后写者返回先写结果。
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

    private Task SaveKnowledgeUsagesAsync(
        Guid recommendationId,
        AppliedMechanismKnowledge mechanismKnowledge,
        CancellationToken ct)
    {
        if (mechanismKnowledgeStore is null || mechanismKnowledge.Claims.Count == 0)
            return Task.CompletedTask;
        var usages = mechanismKnowledge.Claims.SelectMany(value => (value.Constraints.Count == 0
                ? ["knowledge-context"]
                : value.Constraints.Select(static constraint => constraint.Severity == "hard"
                    ? "hard-constraint"
                    : "candidate-ranking").Distinct(StringComparer.Ordinal))
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
                Name = objective.Code, Kind = "le",
                Threshold = objective.UpperLimit ?? objective.Target,
                Weight = objective.Weight, Unit = objective.Unit
            },
            "maximize" => new()
            {
                Name = objective.Code, Kind = "ge",
                Threshold = objective.LowerLimit ?? objective.Target,
                Weight = objective.Weight, Unit = objective.Unit
            },
            "range" when objective.LowerLimit is { } lower && objective.UpperLimit is { } upper =>
                new()
                {
                    Name = objective.Code, Kind = "range", Lower = lower, Upper = upper,
                    Weight = objective.Weight, Unit = objective.Unit
                },
            "target" when objective.LowerLimit is { } lower && objective.UpperLimit is { } upper =>
                new()
                {
                    Name = objective.Code, Kind = "target", Target = objective.Target,
                    Tol = Math.Min(objective.Target - lower, upper - objective.Target),
                    Weight = objective.Weight, Unit = objective.Unit
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

    private static Guid CreateDeterministicExperimentId(Guid projectId, string inputHash)
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
