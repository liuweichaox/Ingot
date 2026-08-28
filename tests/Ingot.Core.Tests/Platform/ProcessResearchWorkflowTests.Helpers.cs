// 提供流程测试共享的场景构建方法。
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
    protected static async Task SeedOnlineAdmissionEvidenceAsync(
        MemoryStore store,
        ResearchProject project)
    {
        var currentProject = (await store.GetProjectAsync(project.ProjectId))!;
        var mechanismHash = MechanismKnowledgeExperimentPolicy.SnapshotHash(
            new AppliedMechanismKnowledge([], [], [], []));
        await store.CreateHistoricalReplayReportAsync(new ResearchHistoricalReplayReport
        {
            ReportId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ValidationPolicyVersion = ValidationThresholds.PolicyVersion,
            Status = ResearchHistoricalReplayStatuses.Reviewed,
            MechanismKnowledgeSnapshotHash = mechanismHash,
            MechanismModelSnapshotHash = "none",
            DatasetSnapshotHash = new string('1', 64),
            UniqueConditionCount = 5,
            SourceRunCount = 5,
            Budget = 5,
            SeedCount = 30,
            InitialObservationCount = 3,
            Optimizer = new ResearchReplayMethodSummary { SuccessRate = 1, Runs = 30 },
            Random = new ResearchReplayMethodSummary { SuccessRate = 0.5, Runs = 30 },
            PredictionIntervalCoverage = 1,
            PredictionIntervalChecks = 5,
            OptimizerModelVersions = ["botorch-controlled-test"],
            EnginePolicy = "production-equivalent:sequential-suggest",
            EvidenceKind = "real-history-candidate-pool",
            Limitations = "candidate pool only",
            GatePassed = true,
            ReportHash = new string('2', 64),
            GeneratedBy = "engineer-a",
            GeneratedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            ReviewedBy = "engineer-b",
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        var drill = new ResearchRollbackDrill
        {
            DrillId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = currentProject.Revision,
            Name = "安全约束触发回退演练",
            Scenario = "模拟质量安全约束触发",
            StopTrigger = "检测到安全结果超限",
            RollbackTarget = "恢复上一组现场确认的安全参数",
            ExpectedActions = ["停止下一条建议", "恢复安全参数", "保留证据"],
            ObservedActions = ["停止下一条建议", "恢复安全参数", "保留证据"],
            Passed = true,
            EvidenceReference = "drill-log:test",
            EvidenceContentHash = new string('6', 64),
            RecordHash = new string('7', 64),
            ConductedBy = "engineer-a",
            ConductedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            RecordedAt = DateTimeOffset.UtcNow.AddMinutes(-3)
        };
        await store.CreateRollbackDrillAsync(drill);
        await store.ReviewRollbackDrillAsync(drill with
        {
            Status = ResearchRollbackDrillStatuses.Reviewed,
            ReviewedBy = "engineer-b",
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-2)
        });
        for (var index = 0; index < 5; index++)
        {
            var factors = Run($"shadow-{index}", index + 1, 500 + index * 5, 8 + index).Factors;
            var shadowExperimentId = Guid.CreateVersion7();
            await store.SaveExperimentAsync(new ResearchExperiment
            {
                ExperimentId = shadowExperimentId,
                ProjectId = project.ProjectId,
                Name = $"机理快照影子实验 {index + 1}",
                DesignMethod = ResearchDesignMethods.BayesianOptimization,
                Status = ResearchExperimentStatuses.Planned,
                RunPlan = [Run($"shadow-source-{index}", 1, 500 + index * 5, 8 + index)],
                ObjectiveCodes = ["form-error"],
                StopRule = "只执行影子评估。",
                RollbackPlan = "不下发设备。",
                Optimization = new ResearchOptimizationMetadata
                {
                    ModelVersion = "botorch-test",
                    InputHash = new string('3', 64),
                    MechanismKnowledgeSnapshotHash = mechanismHash,
                    Mode = ResearchOptimizationModes.Shadow,
                    RunPredictions = [Prediction($"shadow-source-{index}", 0.3)]
                },
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
            await store.CreateShadowRecommendationAsync(new ResearchShadowRecommendation
            {
                RecommendationId = Guid.CreateVersion7(),
                ProjectId = project.ProjectId,
                ExperimentId = shadowExperimentId,
                SuggestionExecutionKey = $"shadow-suggestion-{index}",
                ActualExecutionKey = $"shadow-actual-{index}",
                Decision = ResearchShadowDecisionStatuses.Accepted,
                ModelVersion = "botorch-test",
                ModelInputHash = new string('3', 64),
                ProjectRevision = project.Revision,
                SuggestedFactors = factors,
                EngineerSelectedFactors = factors,
                Prediction = Prediction($"shadow-suggestion-{index}", 0.3),
                Applicability = new ResearchShadowApplicabilityAssessment
                {
                    Status = ResearchApplicabilityStatuses.InDomain,
                    HistoricalObservationCount = 5,
                    Summary = "inside measured envelope"
                },
                ContextSnapshot = new Dictionary<string, string> { ["equipment_id"] = "press-01" },
                DecisionSnapshotHash = new string('4', 64),
                DecidedBy = "engineer-b",
                DecidedAt = DateTimeOffset.UtcNow.AddDays(-1),
                Outcome = new ResearchShadowOutcome
                {
                    ActualExecutionKey = $"shadow-actual-{index}",
                    ActualFactors = factors,
                    Outcomes = new Dictionary<string, double> { ["form-error"] = 0.3 },
                    ActualContextSnapshot = new Dictionary<string, string>
                    { ["equipment_id"] = "press-01" },
                    ValidForOptimization = true,
                    SourceContentHash = new string('5', 64),
                    CapturedAt = DateTimeOffset.UtcNow
                }
            });
        }
    }

    protected static async Task<ResearchHistoricalReplayReport> SeedMethodAdmissionEvidenceAsync(
        MemoryStore store,
        ResearchProject project,
        bool gatePassed = true,
        string? gateFailure = null)
    {
        var mechanismHash = MechanismKnowledgeExperimentPolicy.SnapshotHash(
            new AppliedMechanismKnowledge([], [], [], []));
        var report = new ResearchHistoricalReplayReport
        {
            ReportId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ValidationPolicyVersion = ValidationThresholds.PolicyVersion,
            MechanismKnowledgeSnapshotHash = mechanismHash,
            MechanismModelSnapshotHash = "none",
            Status = ResearchHistoricalReplayStatuses.Reviewed,
            DatasetSnapshotHash = new string('1', 64),
            UniqueConditionCount = 5,
            SourceRunCount = 5,
            Budget = 5,
            SeedCount = 30,
            InitialObservationCount = 3,
            Optimizer = new ResearchReplayMethodSummary { SuccessRate = 1, Runs = 30 },
            Random = new ResearchReplayMethodSummary { SuccessRate = 0.5, Runs = 30 },
            ResponseSurface = new ResearchReplayMethodSummary { SuccessRate = 0.9, Runs = 30 },
            BaselineMethods =
            [
                "historical-engineer-order",
                "seeded-random-order",
                "quadratic-response-surface"
            ],
            OptimizerModelVersions = ["botorch-qlogbo-test"],
            PredictionIntervalCoverage = 1,
            PredictionIntervalChecks = 5,
            EnginePolicy = "production-equivalent:sequential-suggest",
            EvidenceKind = "real-history-candidate-pool",
            Limitations = "candidate pool only",
            GatePassed = gatePassed,
            GateFailures = gateFailure is null ? [] : [gateFailure],
            ReportHash = new string(gatePassed ? '2' : '3', 64),
            GeneratedBy = "engineer-a",
            GeneratedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            ReviewedBy = "engineer-b",
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        return await store.CreateHistoricalReplayReportAsync(report);
    }

    protected static Task<ResearchExperiment> CreateOptimizationExperimentAsync(
        ProcessResearchWorkflow workflow,
        Guid projectId)
        => workflow.ExperimentCommands.CreateExperimentAsync(
            projectId,
            new ResearchExperiment
            {
                Name = "冻结的影子建议",
                DesignMethod = ResearchDesignMethods.BayesianOptimization,
                RunPlan =
                [
                    Run("shadow-run-01", 1, 515, 11),
                    Run("shadow-run-02", 2, 525, 13)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "影子评估不下发设备。",
                RollbackPlan = "影子评估不改变现场参数。",
                Optimization = new ResearchOptimizationMetadata
                {
                    ModelVersion = "botorch-test",
                    InputHash = new string('b', 64),
                    Mode = ResearchOptimizationModes.Shadow,
                    DistinctConditionCount = 2,
                    RunPredictions =
                    [
                        Prediction("shadow-run-01", 0.25),
                        Prediction("shadow-run-02", 0.22)
                    ]
                }
            },
            "engineer-a");

    protected static OptimizationRunPrediction Prediction(string executionKey, double mean)
        => new()
        {
            ExecutionKey = executionKey,
            Objectives = new Dictionary<string, OptimizationMetricPrediction>
            {
                ["form-error"] = new()
                {
                    Mean = mean,
                    StandardDeviation = 0.05,
                    Lower95 = mean - 0.1,
                    Upper95 = mean + 0.1,
                    Unit = "um"
                }
            },
            FeasibilityProbability = 0.98,
            Rationale = "测试影子建议"
        };

    protected static async Task CompleteIndependentValidationAsync(
        ProcessResearchWorkflow workflow,
        ResearchOperatingRegion window)
    {
        var experiment = await workflow.CreateOperatingRegionValidationExperimentAsync(
            window.OperatingRegionId,
            "engineer-a");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        var observations = experiment.RunPlan.Select((run, index) =>
            new ResearchRunObservation
            {
                ExecutionKey = run.ExecutionKey,
                ActualFactors = run.Factors,
                Outcomes = new Dictionary<string, double>
                {
                    ["form-error"] = 0.28 + index * 0.01
                },
                SourceContentHash = new string("abc"[index], 64)
            }).ToArray();
        var result = await workflow.RecordMaterializedExperimentResultAsync(
            experiment.ExperimentId,
            new ResearchExperimentResult
            {
                DatasetSnapshotId = $"validation:{experiment.ExperimentId:N}",
                Metrics =
                [
                    new ExperimentMetricResult
                    {
                        ObjectiveCode = "form-error",
                        BaselineValue = 0.4,
                        ObservedValue = 0.29,
                        EffectValue = -0.11,
                        LowerConfidenceBound = -0.15,
                        UpperConfidenceBound = -0.07,
                        Unit = "um",
                        BaselineSampleCount = 3,
                        ExperimentSampleCount = 3,
                        ComputationMethod = "independent-validation-test"
                    }
                ],
                RunObservations = observations,
                RunCount = 3,
                ReplicateCount = 3,
                DistinctBlockCount = 3,
                DistinctMaterialLotCount = 1,
                DistinctEquipmentCount = 1,
                SafetyPassed = true,
                CalculatedFromSource = true
            },
            "engineer-a");
        await workflow.AttachOperatingRegionValidationResultAsync(
            window.OperatingRegionId,
            experiment,
            result,
            "system-result-materialization");
    }

    protected static async Task SeedControlledOnlineValidationAsync(
        MemoryStore store,
        ResearchOperatingRegion window)
    {
        var experimentId = Guid.CreateVersion7();
        var resultId = Guid.CreateVersion7();
        var factors = window.Variables.Select(variable => new ResearchVariableSetting
        {
            VariableCode = variable.VariableCode,
            Value = variable.LowerBound,
            Unit = variable.Unit
        }).ToArray();
        var observations = Enumerable.Range(1, 3).Select(index => new ResearchRunObservation
        {
            ExecutionKey = $"controlled-validation-{index}",
            ActualFactors = factors,
            Outcomes = new Dictionary<string, double> { ["form-error"] = 0.25 + index * 0.01 },
            SourceContentHash = new string((char)('d' + index), 64)
        }).ToArray();
        await store.SaveExperimentAsync(new ResearchExperiment
        {
            ExperimentId = experimentId,
            ProjectId = window.ProjectId,
            Name = "受控在线操作域验证",
            ExecutionCategory = ResearchExperimentExecutionCategories.ControlledOnline,
            Status = ResearchExperimentStatuses.Completed,
            Factors = factors,
            RunPlan = observations.Select((observation, index) => new ExperimentRunPlan
            {
                ExecutionKey = observation.ExecutionKey,
                Sequence = index + 1,
                Factors = factors
            }).ToArray(),
            ObjectiveCodes = window.ObjectiveCodes,
            ResultIds = [resultId],
            Optimization = new ResearchOptimizationMetadata
            {
                ModelVersion = "controlled-validation-test",
                InputHash = new string('a', 64),
                Mode = ResearchOptimizationModes.Controlled
            },
            ControlledDecision = new ResearchControlledDecision
            {
                Decision = ResearchControlledDecisionStatuses.Accepted,
                SuggestedFactors = factors,
                ApprovedFactors = factors,
                DecisionSnapshotHash = new string('b', 64),
                DecidedBy = "engineer-b",
                DecidedAt = DateTimeOffset.UtcNow
            },
            StopRule = "任一安全约束失败立即停止。",
            RollbackPlan = "恢复已验证实验室设置。",
            CreatedBy = "engineer-a",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = resultId,
            ProjectId = window.ProjectId,
            ExperimentId = experimentId,
            DatasetSnapshotId = $"controlled-validation:{experimentId:N}",
            AnalysisRunId = Guid.CreateVersion7(),
            AnalysisHash = new string('c', 64),
            RunObservations = observations,
            RunCount = observations.Length,
            ReplicateCount = observations.Length,
            DistinctBlockCount = 2,
            SafetyPassed = true,
            CalculatedFromSource = true,
            RecordedBy = "system-result-materialization",
            RecordedAt = DateTimeOffset.UtcNow
        });
    }

    protected static ProcessResearchWorkflow CreateWorkflow(
        IProcessResearchStore store,
        ResearchOnlineAdmissionService? onlineAdmission = null,
        IProcessConfigurationStore? processConfigurations = null,
        IMechanismKnowledgeStore? mechanismKnowledgeStore = null,
        ResearchExperimentValidationService? experimentValidation = null)
    {
        var experimentCommands = new ResearchExperimentCommands(
            new ResearchExperimentCommandStoreAdapter(store),
            onlineAdmission,
            experimentValidation,
            mechanismKnowledgeStore is null
                ? null
                : new ResearchExperimentKnowledgeGate(store, mechanismKnowledgeStore));
        return new ProcessResearchWorkflow(
            store,
            experimentCommands,
            processConfigurations,
            mechanismKnowledgeStore);
    }

    protected static ExperimentRunPlan Run(
        string key,
        int sequence,
        double temperature,
        double force)
        => new()
        {
            ExecutionKey = key,
            Sequence = sequence,
            Factors =
            [
                new ResearchVariableSetting
                {
                    VariableCode = "holding-temperature",
                    Value = temperature,
                    Unit = "Cel"
                },
                new ResearchVariableSetting
                {
                    VariableCode = "press-force",
                    Value = force,
                    Unit = "kN"
                }
            ]
        };

    protected static OptimizerSuggestionOutput Suggestion(
        double temperature,
        double force,
        double acquisition)
        => new()
        {
            RecommendedParameters = new Dictionary<string, double>
            {
                ["holding-temperature"] = temperature,
                ["press-force"] = force
            },
            AcquisitionValue = acquisition
        };

    protected static ResearchRunObservation NaturalObservation(
        string executionKey,
        double temperature,
        double force,
        double formError)
        => new()
        {
            ExecutionKey = executionKey,
            ActualFactors = Run(executionKey, 1, temperature, force).Factors,
            Outcomes = new Dictionary<string, double>
            {
                ["form-error"] = formError
            },
            SourceContentHash = Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(executionKey)))
        };

    protected static ResearchProject ProjectDraft()
        => new()
        {
            Code = "optical-molding-window",
            Name = "光学模压工艺操作域研发",
            ProcessName = "光学玻璃精密模压",
            SiteCode = "SITE-001",
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
            ],
            OptimizationFeatures = new ResearchOptimizationFeatureSet
            {
                FeatureSetId = "declared-test-features",
                Version = 1,
                DerivedFeatures =
                [
                    new ResearchDerivedFeature
                    {
                        Name = "temperature-force-ratio",
                        Operator = ResearchDerivedFeatureOperators.Ratio,
                        Inputs = ["holding-temperature", "press-force"],
                        NormalizationScale = 100
                    }
                ]
            }
        };

    protected static async Task FreezeAndReviewStageZeroAsync(
        MemoryStore store,
        ResearchProject project,
        string frozenBy = "engineer-a")
    {
        var service = new ResearchValidationPreregistrationService(store);
        var frozen = await service.FreezeAsync(
            project.ProjectId,
            ValidPreregistrationRequest(),
            frozenBy);
        await service.ReviewAsync(frozen.PreregistrationId, "stage-zero-reviewer");
    }

    protected static ResearchValidationPreregistrationRequest ValidPreregistrationRequest()
    {
        var completedAt = DateTimeOffset.UtcNow.AddDays(-1);
        return new ResearchValidationPreregistrationRequest
        {
            DataScope = "同产品、同设备的已完成真实运行",
            DataFrom = completedAt.AddDays(-30),
            DataTo = completedAt,
            InclusionMethod = "按运行身份、实际参数、过程轨迹和检验唯一关联纳入",
            InclusionRules = ["运行边界完整", "实际参数与检验唯一关联"],
            ExclusionRules = ["运行身份冲突", "关键过程数据缺失"],
            MatchingRules = ["同产品并按设备和材料批次分层"],
            BaselineMethods = ["工程师当前流程", "历史工程师顺序"],
            PrimaryMetrics = ["从异常到首个可执行假设的时间"],
            GuardrailMetrics = ["运行—检验唯一关联率", "安全边界违规数"],
            StopConditions = ["数据链无法稳定关联"],
            FalsificationConditions = ["系统流程不快于工程师当前流程"],
            EngineerWorkflowBaselines =
            [
                new ResearchWorkflowBaseline
                {
                    Name = "工程师当前找数与分析流程",
                    StartedAt = completedAt.AddMinutes(-45),
                    CompletedAt = completedAt,
                    Steps =
                    [
                        new ResearchWorkflowBaselineStep
                            { Sequence = 1, Name = "查找运行和检验记录", Minutes = 25 },
                        new ResearchWorkflowBaselineStep
                            { Sequence = 2, Name = "建立比较并形成假设", Minutes = 20 }
                    ]
                }
            ]
        };
    }

}
