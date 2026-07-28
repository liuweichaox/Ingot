using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessResearchWorkflowTests
{
    [Fact]
    public async Task ResearchProject_CompletesOnlyAfterValidatedProcessWindow()
    {
        var store = new MemoryStore();
        var workflow = new ProcessResearchWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");

        var hypothesis = await workflow.SaveHypothesisAsync(
            project.ProjectId,
            new ResearchHypothesis
            {
                Statement = "保压温度和压力共同影响面形误差。",
                Rationale = "历史周期、物理机理和专家经验均指向该交互关系。",
                VariableCodes = ["holding-temperature", "press-force"],
                ValidationOutcomeCode = "form-error",
                ExpectedEffectDirection = ResearchHypothesisEffectDirections.Decrease,
                MinimumEffect = 0.2,
                Confidence = 0.6
            },
            "engineer-a");

        var experiment = await workflow.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                HypothesisId = hypothesis.HypothesisId,
                Name = "保压温度与压力验证实验",
                DesignMethod = ResearchDesignMethods.FullFactorial,
                Factors =
                [
                    new ExperimentFactorSetting
                    {
                        VariableCode = "holding-temperature",
                        Value = 520,
                        Unit = "Cel"
                    },
                    new ExperimentFactorSetting
                    {
                        VariableCode = "press-force",
                        Value = 12,
                        Unit = "kN"
                    }
                ],
                RunPlan =
                [
                    Run("low-low", 1, 510, 10),
                    Run("high-high", 2, 530, 14)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "安全约束触发时停止。",
                RollbackPlan = "恢复已验证基线配方。"
            },
            "engineer-a");
        experiment = await workflow.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        var result = await workflow.RecordExperimentResultAsync(
            experiment.ExperimentId,
            new ResearchExperimentResult
            {
                DatasetSnapshotId = "snapshot-2026-07-25",
                Metrics =
                [
                    new ExperimentMetricResult
                    {
                        ObjectiveCode = "form-error",
                        BaselineValue = 0.8,
                        ObservedValue = 0.35,
                        EffectValue = -0.45,
                        LowerConfidenceBound = -0.55,
                        UpperConfidenceBound = -0.35,
                        Unit = "um",
                        BaselineSampleCount = 12,
                        ExperimentSampleCount = 12,
                        ComputationMethod = "bootstrap difference"
                    }
                ],
                RunObservations =
                [
                    new ExperimentRunObservation
                    {
                        RunKey = "cycle-001",
                        ActualFactors =
                        [
                            new ExperimentFactorSetting
                            {
                                VariableCode = "holding-temperature",
                                Value = 510,
                                Unit = "Cel"
                            },
                            new ExperimentFactorSetting
                            {
                                VariableCode = "press-force",
                                Value = 10,
                                Unit = "kN"
                            }
                        ],
                        ProcessFeatures = new Dictionary<string, double>
                        {
                            ["temperature-overshoot"] = 1.2,
                            ["force-impulse"] = 42.0
                        },
                        Outcomes = new Dictionary<string, double>
                        {
                            ["form-error"] = 0.43
                        },
                        SourceContentHash = new string('a', 64)
                    },
                    new ExperimentRunObservation
                    {
                        RunKey = "cycle-002",
                        ActualFactors =
                        [
                            new ExperimentFactorSetting
                            {
                                VariableCode = "holding-temperature",
                                Value = 530,
                                Unit = "Cel"
                            },
                            new ExperimentFactorSetting
                            {
                                VariableCode = "press-force",
                                Value = 14,
                                Unit = "kN"
                            }
                        ],
                        ProcessFeatures = new Dictionary<string, double>
                        {
                            ["temperature-overshoot"] = 0.8,
                            ["force-impulse"] = 48.0
                        },
                        Outcomes = new Dictionary<string, double>
                        {
                            ["form-error"] = 0.35
                        },
                        SourceContentHash = new string('b', 64)
                    }
                ],
                RunCount = 4,
                ReplicateCount = 2,
                DistinctMaterialLotCount = 2,
                DistinctEquipmentCount = 1,
                SafetyPassed = true,
                CalculatedFromSource = true
            },
            "engineer-a");
        experiment = await workflow.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Completed,
            "engineer-a");

        var window = await workflow.SaveProcessWindowAsync(
            project.ProjectId,
            new ResearchProcessWindow
            {
                Name = "稳定成形窗口",
                Variables =
                [
                    new ProcessWindowVariable
                    {
                        VariableCode = "holding-temperature",
                        LowerBound = 510,
                        UpperBound = 530,
                        Unit = "Cel"
                    },
                    new ProcessWindowVariable
                    {
                        VariableCode = "press-force",
                        LowerBound = 10,
                        UpperBound = 14,
                        Unit = "kN"
                    }
                ],
                ObjectiveCodes = ["form-error"],
                SupportingExperimentIds = [experiment.ExperimentId],
                SupportingResultIds = [result.ResultId],
                Confidence = 0.9,
                ConfidenceMethod = ResearchConfidenceMethods.Bootstrap,
                AnalysisRunId = result.AnalysisRunId,
                AnalysisHash = result.AnalysisHash,
                Applicability = "材料批次 A，设备 PRESS-01。"
            },
            "engineer-a");

        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Validating,
            "engineer-a");
        window = await workflow.ValidateProcessWindowAsync(window.WindowId, "engineer-b");
        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Completed,
            "engineer-a");

        Assert.Equal(ProcessWindowStatuses.Validated, window.Status);
        Assert.Equal(ResearchProjectStatuses.Completed, project.Status);
        Assert.Equal(2, result.RunObservations.Count);
        var workspace = await workflow.GetWorkspaceAsync(project.ProjectId);
        Assert.Single(workspace.Hypotheses);
        Assert.Equal(ResearchHypothesisStatuses.Supported, workspace.Hypotheses[0].Status);
        Assert.Single(workspace.Hypotheses[0].ValidationEvidence);
        Assert.Single(workspace.Experiments);
        Assert.Single(workspace.ProcessWindows);
    }

    [Fact]
    public async Task Experiment_CreatorCannotApproveOwnPlan()
    {
        var store = new MemoryStore();
        var workflow = new ProcessResearchWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");
        var experiment = await workflow.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "单因素探索",
                Factors =
                [
                    new ExperimentFactorSetting
                    {
                        VariableCode = "holding-temperature",
                        Value = 520,
                        Unit = "Cel"
                    }
                ],
                RunPlan =
                [
                    Run("low", 1, 500, 10),
                    Run("high", 2, 530, 10)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "安全约束触发时停止。",
                RollbackPlan = "恢复基线配方。"
            },
            "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ChangeExperimentStatusAsync(
                experiment.ExperimentId,
                ResearchExperimentStatuses.Approved,
                "engineer-a"));

        Assert.Contains("创建人和批准人必须分离", error.Message);
    }

    [Fact]
    public async Task Experiment_CannotCompleteWithoutCalculatedResult()
    {
        var store = new MemoryStore();
        var workflow = new ProcessResearchWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");
        var experiment = await workflow.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "结果门禁验证",
                RunPlan =
                [
                    Run("low", 1, 500, 10),
                    Run("high", 2, 530, 10)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "安全约束触发时停止。",
                RollbackPlan = "恢复基线配方。"
            },
            "engineer-a");
        await workflow.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        await workflow.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ChangeExperimentStatusAsync(
                experiment.ExperimentId,
                ResearchExperimentStatuses.Completed,
                "engineer-a"));

        Assert.Contains("必须记录由源数据计算得到的结果", error.Message);
    }

    [Fact]
    public async Task ResultMaterializer_PersistsCompleteCycleObservationsAsFormalResult()
    {
        var store = new MemoryStore();
        var workflow = new ProcessResearchWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        var experiment = await workflow.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "自动回灌实验",
                RunPlan =
                [
                    Run("auto-cycle-1", 1, 510, 10),
                    Run("auto-cycle-2", 2, 530, 14)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "完成全部运行。",
                RollbackPlan = "恢复基线。"
            },
            "engineer-a");
        experiment = await workflow.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        ExperimentRunObservation Observation(
            string runKey,
            double temperature,
            double force,
            double outcome,
            char hash)
            => new()
            {
                RunKey = runKey,
                ActualFactors =
                [
                    new ExperimentFactorSetting
                    {
                        VariableCode = "holding-temperature",
                        Value = temperature,
                        Unit = "Cel"
                    },
                    new ExperimentFactorSetting
                    {
                        VariableCode = "press-force",
                        Value = force,
                        Unit = "kN"
                    }
                ],
                ProcessFeatures = new Dictionary<string, double>
                {
                    ["mold-temperature.cycle.average"] = temperature
                },
                Outcomes = new Dictionary<string, double> { ["form-error"] = outcome },
                SourceContentHash = new string(hash, 64)
            };
        var assembly = new ResearchObservationAssembly(
        [
            Observation("auto-cycle-1", 510, 10, 0.52, 'd'),
            Observation("auto-cycle-2", 530, 14, 0.37, 'e')
        ], 2);
        var materializer = new ResearchExperimentResultMaterializer(workflow);

        var results = await materializer.MaterializeCompletedAsync(
            project,
            [experiment],
            [],
            assembly,
            "system-cycle-materializer");

        var result = Assert.Single(results);
        Assert.Equal(2, result.RunObservations.Count);
        Assert.True(result.CalculatedFromSource);
        Assert.StartsWith("cycle-observation-snapshot:", result.DatasetSnapshotId);
        var savedExperiment = await store.GetExperimentAsync(experiment.ExperimentId);
        Assert.Contains(result.ResultId, savedExperiment!.ResultIds);
        Assert.Contains(
            await store.ListAuditEntriesAsync(project.ProjectId),
            value => value.ResourceType == "experiment-result" &&
                     value.ResourceId == result.ResultId.ToString());
    }

    [Fact]
    public async Task Optimizer_CreatesAnOrdinaryExperimentFromPerRunObservations()
    {
        var store = new MemoryStore();
        var workflow = new ProcessResearchWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = Guid.CreateVersion7(),
            DatasetSnapshotId = "fx3u-cycle-snapshot",
            RunObservations =
            [
                new ExperimentRunObservation
                {
                    RunKey = "fx3u-cycle-1",
                    ActualFactors =
                    [
                        new ExperimentFactorSetting
                        {
                            VariableCode = "holding-temperature", Value = 510, Unit = "Cel"
                        },
                        new ExperimentFactorSetting
                        {
                            VariableCode = "press-force", Value = 10, Unit = "kN"
                        }
                    ],
                    Outcomes = new Dictionary<string, double> { ["form-error"] = 0.8 },
                    SourceContentHash = new string('c', 64)
                }
            ]
        });
        var optimizer = new ResearchExperimentOptimizer(
            store,
            new StubOptimizerClient(),
            new EmptyObservationAssembler(),
            new ResearchExperimentResultMaterializer(workflow),
            workflow);

        var experiment = await optimizer.CreateNextExperimentAsync(
            project.ProjectId,
            new ResearchOptimizationRequest { BatchSize = 2, Seed = 7 },
            "engineer-a");
        var repeated = await optimizer.CreateNextExperimentAsync(
            project.ProjectId,
            new ResearchOptimizationRequest { BatchSize = 2, Seed = 7 },
            "engineer-a");

        Assert.Equal(ResearchDesignMethods.BayesianOptimization, experiment.DesignMethod);
        Assert.Equal(experiment.ExperimentId, repeated.ExperimentId);
        Assert.Equal(ResearchExperimentStatuses.Planned, experiment.Status);
        Assert.Equal(2, experiment.RunPlan.Count);
        Assert.NotNull(experiment.Optimization);
        Assert.Equal(1, experiment.Optimization.ObservationCount);
        Assert.Equal("botorch-qlogbo-test", experiment.Optimization.ModelVersion);
    }

    private static ExperimentRunPlan Run(
        string key,
        int sequence,
        double temperature,
        double force)
        => new()
        {
            RunKey = key,
            Sequence = sequence,
            Factors =
            [
                new ExperimentFactorSetting
                {
                    VariableCode = "holding-temperature",
                    Value = temperature,
                    Unit = "Cel"
                },
                new ExperimentFactorSetting
                {
                    VariableCode = "press-force",
                    Value = force,
                    Unit = "kN"
                }
            ]
        };

    private static ResearchProject ProjectDraft()
        => new()
        {
            Code = "optical-molding-window",
            Name = "光学模压工艺窗口研发",
            ProcessName = "光学玻璃精密模压",
            Objectives =
            [
                new ResearchObjective
                {
                    Code = "form-error",
                    Name = "面形误差",
                    Unit = "um",
                    Direction = "minimize",
                    Target = 0.4
                }
            ],
            Variables =
            [
                new ResearchVariable
                {
                    Code = "holding-temperature",
                    Name = "保压温度",
                    Role = ResearchVariableRoles.Control,
                    Unit = "Cel",
                    LowerLimit = 480,
                    UpperLimit = 550
                },
                new ResearchVariable
                {
                    Code = "press-force",
                    Name = "模压力",
                    Role = ResearchVariableRoles.Control,
                    Unit = "kN",
                    LowerLimit = 5,
                    UpperLimit = 20
                }
            ],
            Constraints =
            [
                new ResearchConstraint
                {
                    Code = "temperature-safety",
                    Description = "保压温度安全上限",
                    VariableCode = "holding-temperature",
                    Operator = "<=",
                    Limit = 545,
                    Unit = "Cel",
                    SafetyCritical = true
                }
            ]
        };

    private sealed class StubOptimizerClient : IProcessOptimizerClient
    {
        public Task<OptimizerSuggestionResponse> SuggestAsync(
            OptimizerSuggestionCall request,
            CancellationToken ct = default)
        {
            Assert.Single(request.Observations);
            var suggestions = new[]
            {
                Suggest(515, 11, 0.55),
                Suggest(525, 13, 0.42)
            };
            return Task.FromResult(new OptimizerSuggestionResponse
            {
                ModelVersion = "botorch-qlogbo-test",
                ObservationCount = request.Observations.Count,
                Suggestions = suggestions
            });
        }

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

    private sealed class EmptyObservationAssembler : IResearchObservationAssembler
    {
        public Task<ResearchObservationAssembly> AssembleAsync(
            ResearchProject project,
            IReadOnlyList<ResearchExperiment> experiments,
            CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly([], 0));
    }

    private sealed class MemoryStore : IProcessResearchStore
    {
        private readonly Dictionary<Guid, ResearchProject> _projects = [];
        private readonly Dictionary<Guid, ResearchHypothesis> _hypotheses = [];
        private readonly Dictionary<Guid, ResearchExperiment> _experiments = [];
        private readonly Dictionary<Guid, ResearchExperimentResult> _results = [];
        private readonly Dictionary<Guid, ResearchProcessWindow> _windows = [];
        private readonly Dictionary<Guid, ResearchKnowledgeClaim> _claims = [];
        private readonly List<ResearchAuditEntry> _audit = [];

        public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(_projects.GetValueOrDefault(projectId));

        public Task<ResearchProject?> GetProjectByCodeAsync(
            string code,
            CancellationToken ct = default)
            => Task.FromResult(_projects.Values.SingleOrDefault(
                value => string.Equals(value.Code, code, StringComparison.Ordinal)));

        public Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(
            string userId,
            bool includeAll,
            int limit,
            int offset,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchProject>>(
                _projects.Values
                    .Where(value => includeAll || value.MemberUserIds.Contains(userId))
                    .Skip(offset)
                    .Take(limit)
                    .ToArray());

        public Task<ResearchProject> SaveProjectAsync(
            ResearchProject value,
            CancellationToken ct = default)
        {
            _projects[value.ProjectId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchHypothesis?> GetHypothesisAsync(
            Guid hypothesisId,
            CancellationToken ct = default)
            => Task.FromResult(_hypotheses.GetValueOrDefault(hypothesisId));

        public Task<IReadOnlyList<ResearchHypothesis>> ListHypothesesAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchHypothesis>>(
                _hypotheses.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchHypothesis> SaveHypothesisAsync(
            ResearchHypothesis value,
            CancellationToken ct = default)
        {
            _hypotheses[value.HypothesisId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchExperiment?> GetExperimentAsync(
            Guid experimentId,
            CancellationToken ct = default)
            => Task.FromResult(_experiments.GetValueOrDefault(experimentId));

        public Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchExperiment>>(
                _experiments.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchExperiment> SaveExperimentAsync(
            ResearchExperiment value,
            CancellationToken ct = default)
        {
            _experiments[value.ExperimentId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchExperimentResult?> GetExperimentResultAsync(
            Guid resultId,
            CancellationToken ct = default)
            => Task.FromResult(_results.GetValueOrDefault(resultId));

        public Task<IReadOnlyList<ResearchExperimentResult>> ListExperimentResultsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchExperimentResult>>(
                _results.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchExperimentResult> SaveExperimentResultAsync(
            ResearchExperimentResult value,
            CancellationToken ct = default)
        {
            _results[value.ResultId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchProcessWindow?> GetProcessWindowAsync(
            Guid windowId,
            CancellationToken ct = default)
            => Task.FromResult(_windows.GetValueOrDefault(windowId));

        public Task<IReadOnlyList<ResearchProcessWindow>> ListProcessWindowsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchProcessWindow>>(
                _windows.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchProcessWindow> SaveProcessWindowAsync(
            ResearchProcessWindow value,
            CancellationToken ct = default)
        {
            _windows[value.WindowId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchKnowledgeClaim?> GetKnowledgeClaimAsync(
            Guid claimId,
            CancellationToken ct = default)
            => Task.FromResult(_claims.GetValueOrDefault(claimId));

        public Task<IReadOnlyList<ResearchKnowledgeClaim>> ListKnowledgeClaimsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchKnowledgeClaim>>(
                _claims.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
            ResearchKnowledgeClaim value,
            CancellationToken ct = default)
        {
            _claims[value.ClaimId] = value;
            return Task.FromResult(value);
        }

        public Task AddAuditEntryAsync(
            ResearchAuditEntry value,
            CancellationToken ct = default)
        {
            _audit.Add(value);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ResearchAuditEntry>> ListAuditEntriesAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchAuditEntry>>(
                _audit.Where(value => value.ProjectId == projectId).ToArray());
    }
}
