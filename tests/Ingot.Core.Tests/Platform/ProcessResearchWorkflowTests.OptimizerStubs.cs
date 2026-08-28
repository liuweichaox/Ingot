// 提供流程测试使用的优化器和数据源桩实现。
using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public abstract partial class ProcessResearchWorkflowTestBase
{
    protected sealed class StubOptimizerClient : IProcessOptimizerClient
    {
        public Task<OptimizerSuggestionResponse> SuggestAsync(
            OptimizerSuggestionCall request,
            CancellationToken ct = default)
        {
            Assert.Single(request.Observations);
            Assert.Equal("declared-test-features", request.Campaign.FeatureSetId);
            var feature = Assert.Single(request.Campaign.DerivedFeatures);
            Assert.Equal("ratio", feature.Operator);
            Assert.Equal(
                ["holding-temperature", "press-force"],
                feature.Inputs);
            var suggestions = new[]
            {
                Suggest(515, 11, 0.20),
                Suggest(525, 13, 0.18)
            };
            return Task.FromResult(new OptimizerSuggestionResponse
            {
                ModelVersion = "botorch-qlogbo-test",
                ObservationCount = request.Observations.Count,
                Suggestions = suggestions
            });
        }

        public Task<OptimizerDesignResponse> DesignAsync(OptimizerDesignCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProcessDiagnosisResponse> DiagnoseAsync(ProcessDiagnosisCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<JsonElement> ReplayHistoryAsync(OptimizerHistoricalReplayCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        private static OptimizerSuggestionOutput Suggest(
            double temperature,
            double force,
            double predictedFormError)
            => new()
            {
                RecommendedParameters = new Dictionary<string, double>
                {
                    ["holding-temperature"] = temperature,
                    ["press-force"] = force
                },
                Predictions = new Dictionary<string, OptimizerObjectivePrediction>
                {
                    ["form-error"] = new()
                    {
                        Mean = predictedFormError,
                        StandardDeviation = 0.08,
                        Lower95 = predictedFormError - 0.16,
                        Upper95 = predictedFormError + 0.16,
                        Unit = "um"
                    }
                },
                FeasibilityProbability = 0.7,
                AcquisitionValue = 0.2,
                ModelVersion = "botorch-qlogbo-test",
                Rationale = "测试优化建议"
            };
    }

    protected sealed class EmptyObservationAssembler : IResearchObservationAssembler
    {
        public Task<ResearchObservationAssembly> AssembleProductionRunsAsync(
            ResearchProject project,
            CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly([], 0));

        public Task<ResearchObservationAssembly> AssembleAsync(
            ResearchProject project,
            IReadOnlyList<ResearchExperiment> experiments,
            CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly([], 0));
    }

    protected sealed class MutableNaturalObservationAssembler(
        IReadOnlyList<ResearchRunObservation> observations) : IResearchObservationAssembler
    {
        public IReadOnlyList<ResearchRunObservation> Values { get; set; } = observations;

        public Task<ResearchObservationAssembly> AssembleProductionRunsAsync(
            ResearchProject project,
            CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly(Values, Values.Count));

        public Task<ResearchObservationAssembly> AssembleAsync(
            ResearchProject project,
            IReadOnlyList<ResearchExperiment> experiments,
            CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly([], 0));
    }

    protected sealed class RecipeOptimizerClient : IProcessOptimizerClient
    {
        public Task<OptimizerSuggestionResponse> SuggestAsync(
            OptimizerSuggestionCall request,
            CancellationToken ct = default)
        {
            Assert.Equal(1, request.TopK);
            var temperature = request.Observations.Count == 3 ? 515d : 522d;
            var force = request.Observations.Count == 3 ? 11d : 12.5d;
            return Task.FromResult(new OptimizerSuggestionResponse
            {
                ModelVersion = "natural-recipe-test",
                ObservationCount = request.Observations.Count,
                Suggestions =
                [
                    new OptimizerSuggestionOutput
                    {
                        RecommendedParameters = new Dictionary<string, double>
                        {
                            ["holding-temperature"] = temperature,
                            ["press-force"] = force
                        },
                        Predictions = new Dictionary<string, OptimizerObjectivePrediction>
                        {
                            ["form-error"] = new()
                            {
                                Mean = 0.3,
                                StandardDeviation = 0.04,
                                Lower95 = 0.22,
                                Upper95 = 0.38,
                                Unit = "um"
                            }
                        },
                        FeasibilityProbability = 0.96,
                        AcquisitionValue = 0.12,
                        ModelVersion = "natural-recipe-test",
                        Rationale = "基于真实生产运行的下一配方"
                    }
                ]
            });
        }

        public Task<OptimizerDesignResponse> DesignAsync(
            OptimizerDesignCall request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProcessDiagnosisResponse> DiagnoseAsync(
            ProcessDiagnosisCall request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<JsonElement> ReplayHistoryAsync(
            OptimizerHistoricalReplayCall request,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    protected sealed class ControlledOptimizerClient : IProcessOptimizerClient
    {
        public Task<OptimizerSuggestionResponse> SuggestAsync(
            OptimizerSuggestionCall request,
            CancellationToken ct = default)
        {
            Assert.Equal(1, request.TopK);
            Assert.Equal(5, request.Observations.Count);
            return Task.FromResult(new OptimizerSuggestionResponse
            {
                ModelVersion = "botorch-controlled-test",
                ObservationCount = request.Observations.Count,
                Suggestions =
                [
                    new OptimizerSuggestionOutput
                    {
                        RecommendedParameters = new Dictionary<string, double>
                        {
                            ["holding-temperature"] = 520,
                            ["press-force"] = 10
                        },
                        Predictions = new Dictionary<string, OptimizerObjectivePrediction>
                        {
                            ["form-error"] = new()
                            {
                                Mean = 0.3,
                                StandardDeviation = 0.05,
                                Lower95 = 0.2,
                                Upper95 = 0.4,
                                Unit = "um"
                            }
                        },
                        FeasibilityProbability = 0.95,
                        AcquisitionValue = 0.1,
                        ModelVersion = "botorch-controlled-test",
                        Rationale = "inside measured envelope"
                    }
                ]
            });
        }

        public Task<OptimizerDesignResponse> DesignAsync(OptimizerDesignCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProcessDiagnosisResponse> DiagnoseAsync(ProcessDiagnosisCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<JsonElement> ReplayHistoryAsync(OptimizerHistoricalReplayCall request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    protected sealed class StubReplayOptimizerClient : IProcessOptimizerClient
    {
        public OptimizerHistoricalReplayCall? LastCall { get; private set; }
        public List<OptimizerHistoricalReplayCall> Calls { get; } = [];
        public Func<string, string>? TransformJson { get; init; }

        public Task<OptimizerSuggestionResponse> SuggestAsync(
            OptimizerSuggestionCall request,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<OptimizerDesignResponse> DesignAsync(OptimizerDesignCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProcessDiagnosisResponse> DiagnoseAsync(ProcessDiagnosisCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<JsonElement> ReplayHistoryAsync(
            OptimizerHistoricalReplayCall request,
            CancellationToken ct = default)
        {
            LastCall = request;
            Calls.Add(request);
            using var document = JsonDocument.Parse(
                """
                {
                  "original_order_trials": 5,
                  "optimizer": {"success_rate": 1.0, "median_trials": 4.0, "mean_trials": 4.0, "runs": 2},
                  "random": {"success_rate": 0.5, "median_trials": 5.0, "mean_trials": 5.0, "runs": 2},
                  "response_surface": {"success_rate": 1.0, "median_trials": 4.0, "mean_trials": 4.0, "runs": 2},
                  "baseline_methods": ["historical-engineer-order", "seeded-random-order", "quadratic-response-surface"],
                  "raw_optimizer": [4, 4],
                  "raw_random": [5, null],
                  "selected_history_indices": [[0,1,2,3],[0,1,2,3]],
                  "step_traces": [
                    [
                      {"step":1,"kind":"preregistered-initial-observation","visible_observation_indices_before":[],"revealed_history_index":0},
                      {"step":2,"kind":"preregistered-initial-observation","visible_observation_indices_before":[0],"revealed_history_index":1},
                      {"step":3,"kind":"preregistered-initial-observation","visible_observation_indices_before":[0,1],"revealed_history_index":2},
                      {"step":4,"kind":"optimizer-selection","visible_observation_indices_before":[0,1,2],"candidate_history_indices":[3,4],"revealed_history_index":3,"model_version":"botorch-qlogbo-test"}
                    ],
                    [
                      {"step":1,"kind":"preregistered-initial-observation","visible_observation_indices_before":[],"revealed_history_index":0},
                      {"step":2,"kind":"preregistered-initial-observation","visible_observation_indices_before":[0],"revealed_history_index":1},
                      {"step":3,"kind":"preregistered-initial-observation","visible_observation_indices_before":[0,1],"revealed_history_index":2},
                      {"step":4,"kind":"optimizer-selection","visible_observation_indices_before":[0,1,2],"candidate_history_indices":[3,4],"revealed_history_index":3,"model_version":"botorch-qlogbo-test"}
                    ]
                  ],
                  "calibration": [
                    {"prediction_interval_checks":2,"prediction_interval_covered":2,"prediction_interval_coverage":1.0,"safety_violations":0},
                    {"prediction_interval_checks":2,"prediction_interval_covered":2,"prediction_interval_coverage":1.0,"safety_violations":0}
                  ],
                  "safety_violations": {"original_order":0,"optimizer":[0,0],"random":[0,0],"response_surface":[0,0]},
                  "budget": 5,
                  "initial_observation_count": 3,
                  "engine_policy": "production-equivalent: sequential below 3 observations, BoTorch at 3 or more",
                  "evidence_kind": "historical-pool-ranking",
                  "limitations": "Ranks only observed processSpecifications; does not prove online savings.",
                  "state_persisted": false
                }
                """);
            var json = document.RootElement.GetRawText();
            using var transformed = JsonDocument.Parse(TransformJson?.Invoke(json) ?? json);
            return Task.FromResult(transformed.RootElement.Clone());
        }
    }

    protected sealed class ReplayMechanismKnowledgeStore(MechanismClaimVersion claim)
        : IMechanismKnowledgeStore
    {
        public Task<IReadOnlyList<MechanismClaimVersion>> ListClaimsAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MechanismClaimVersion>>(projectId == claim.ProjectId ? [claim] : []);
        public Task<IReadOnlyList<MechanismClaimConflict>> ListConflictsAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<MechanismClaimConflict>>([]);
        public Task<MechanismClaimVersion?> GetClaimAsync(Guid claimId, int? version = null, CancellationToken ct = default)
            => Task.FromResult<MechanismClaimVersion?>(claimId == claim.ClaimId ? claim : null);
        public Task<MechanismClaimVersion> SaveDraftAsync(MechanismClaimVersion value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvidenceExistsAsync(Guid projectId, MechanismClaimEvidence evidence, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MechanismClaimVersion> AddReviewAsync(MechanismClaimReview review, string targetStatus, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MechanismClaimConflict> AddConflictAsync(MechanismClaimConflict value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MechanismClaimConflict?> GetConflictAsync(Guid conflictId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MechanismClaimConflict> ResolveConflictAsync(MechanismClaimConflict value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveUsagesAsync(IReadOnlyList<MechanismClaimUsage> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveRecipeRecommendationUsagesAsync(IReadOnlyList<MechanismClaimUsage> values, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<MechanismClaimUsage>> ListUsagesAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> LifecycleEvidenceUsedAsync(Guid claimId, string referenceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> LifecycleActorUsedAsync(Guid claimId, string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MechanismClaimVersion> TransitionAsync(MechanismClaimLifecycleDecision decision, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExperimentResultValidatesClaimAsync(Guid projectId, MechanismClaimVersion value, Guid validationHypothesisId, MechanismClaimEvidence evidence, string evaluationOutcome = "supports", CancellationToken ct = default) => throw new NotSupportedException();
    }

    protected sealed class StubShadowObservationAssembler(ResearchRunObservation? observation)
        : IResearchObservationAssembler
    {
        public string? RequestedExecutionKey { get; private set; }

        public Task<ResearchObservationAssembly> AssembleProductionRunsAsync(
            ResearchProject project,
            CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly(
                observation is null ? [] : [observation],
                observation is null ? 0 : 1));

        public Task<ResearchObservationAssembly> AssembleAsync(
            ResearchProject project,
            IReadOnlyList<ResearchExperiment> experiments,
            CancellationToken ct = default)
        {
            RequestedExecutionKey = Assert.Single(Assert.Single(experiments).RunPlan).ExecutionKey;
            return Task.FromResult(new ResearchObservationAssembly(
                observation is null ? [] : [observation], 1));
        }
    }

    protected sealed class ScenarioOnlyConfigurationStore(ScenarioPackage scenario) : IProcessConfigurationStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScenarioPackage> UpsertScenarioPackageAsync(ScenarioPackage value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ScenarioPackage>> ListScenarioPackagesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ScenarioPackage>>([scenario]);
        public Task<ScenarioPackage?> GetScenarioPackageAsync(
            string packageId,
            int version,
            CancellationToken ct = default)
            => Task.FromResult<ScenarioPackage?>(
                packageId == scenario.PackageId && version == scenario.Version ? scenario : null);
        public Task<bool> DeleteScenarioPackageAsync(string packageId, int version, CancellationToken ct = default)
            => Task.FromResult(false);
    }

}
