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

public sealed class ProcessResearchWorkflowTests
{
    [Fact]
    public async Task ExecutionEvidenceService_RejectsInvalidComparisonBeforeReadingExecutions()
    {
        var store = new MemoryStore();
        var commands = new ResearchExperimentCommands(new ResearchExperimentCommandStoreAdapter(store));
        var comparisons = new RejectingExecutionComparisonService();
        var service = new ResearchExecutionEvidenceService(
            store,
            new ProcessResearchWorkflow(store, commands),
            commands,
            comparisons);

        await Assert.ThrowsAsync<ProcessResearchRuleException>(() => service.ProposeHypothesesAsync(
            Guid.CreateVersion7(),
            new ResearchHypothesisFromExecutionComparisonRequest
            {
                BaselineProcessExecutionId = "run-a",
                ProcessExecutionIds = ["run-a"],
                MaximumHypotheses = 3
            },
            "engineer-a",
            CancellationToken.None));

        Assert.Equal(0, comparisons.CallCount);
    }

    [Fact]
    public async Task ExecutionEvidenceService_RejectsExploratoryComparisonBeforeCreatingHypotheses()
    {
        var store = new MemoryStore();
        var commands = new ResearchExperimentCommands(new ResearchExperimentCommandStoreAdapter(store));
        var project = await new ProcessResearchWorkflow(store, commands)
            .CreateProjectAsync(ProjectDraft(), "engineer-a");
        var comparisons = new FixedExecutionComparisonService(
            Comparison("exploratory", -0.2, "exploratory"));
        var service = new ResearchExecutionEvidenceService(
            store,
            new ProcessResearchWorkflow(store, commands),
            commands,
            comparisons);

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.ProposeHypothesesAsync(
                project.ProjectId,
                new ResearchHypothesisFromExecutionComparisonRequest
                {
                    BaselineProcessExecutionId = "run-a",
                    ProcessExecutionIds = ["run-a", "run-b"],
                    MaximumHypotheses = 3
                },
                "engineer-a",
                CancellationToken.None));

        Assert.Contains("探索性证据", error.Message, StringComparison.Ordinal);
        Assert.Empty(await store.ListHypothesesAsync(project.ProjectId));
    }

    [Fact]
    public async Task ExecutionEvidenceService_CreatesHypothesisOnlyFromStableRankedComparison()
    {
        var store = new MemoryStore();
        var commands = new ResearchExperimentCommands(new ResearchExperimentCommandStoreAdapter(store));
        var workflow = new ProcessResearchWorkflow(store, commands);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        var service = new ResearchExecutionEvidenceService(
            store,
            workflow,
            commands,
            new FixedExecutionComparisonService(
                Comparison("candidate-ranking", 0.4, "stable")));

        var created = await service.ProposeHypothesesAsync(
            project.ProjectId,
            new ResearchHypothesisFromExecutionComparisonRequest
            {
                BaselineProcessExecutionId = "run-a",
                ProcessExecutionIds = ["run-a", "run-b"],
                MaximumHypotheses = 3
            },
            "engineer-a",
            CancellationToken.None);

        var hypothesis = Assert.Single(created);
        Assert.Contains("观察性关联", hypothesis.Rationale, StringComparison.Ordinal);
        Assert.Equal(["holding-temperature"], hypothesis.VariableCodes);
    }
    [Fact]
    public void UnitConverter_ConvertsKnownIndustrialAliases_AndRejectsUnknownDimensions()
    {
        Assert.True(ProcessUnitConverter.TryConvert(10, "bar", "MPa", out var mpa));
        Assert.Equal(1, mpa, 8);
        Assert.True(ProcessUnitConverter.TryConvert(1, "kgf/cm²", "bar", out var bar));
        Assert.Equal(0.980665, bar, 6);
        Assert.True(ProcessUnitConverter.TryConvert(25, "℃", "K", out var kelvin));
        Assert.Equal(298.15, kelvin, 8);
        Assert.False(ProcessUnitConverter.TryConvert(1, "HRC", "MPa", out _));
    }

    [Fact]
    public async Task Hypothesis_PreservesCausalTemporalInteractionFailureAndFalsificationStructure()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");

        var saved = await workflow.SaveHypothesisAsync(project.ProjectId, new ResearchHypothesis
        {
            Statement = "保压温度通过材料流动状态影响压力响应。",
            Rationale = "来源于过程曲线和工程机理。",
            VariableCodes = ["holding-temperature", "press-force"],
            CausalChain = [new ResearchHypothesisCausalLink { FromVariableCode = "holding-temperature", ToVariableCode = "press-force", Mechanism = "温度改变材料黏度和压力传递。", Direction = "decrease" }],
            TemporalFeatures = [new ResearchHypothesisTemporalFeature { VariableCode = "press-force", FeatureCode = "pressure-rise", PhaseCode = "holding", DelayMilliseconds = 500, WindowMilliseconds = 3000 }],
            Interactions = [new ResearchHypothesisInteraction { VariableCodes = ["holding-temperature", "press-force"], Description = "温度改变压力效应强度。" }],
            FailureConditions = [new ResearchHypothesisFailureCondition { Condition = "温度超过材料稳定边界", ObservableSignal = "颜色或挥发物异常", RequiredResponse = "停止实验并恢复基线" }],
            FalsificationConditions = ["升高温度后压力响应及质量结果均没有方向性变化。"],
            Confidence = 0.4
        }, "engineer-a");

        Assert.Single(saved.CausalChain);
        Assert.Equal(500, Assert.Single(saved.TemporalFeatures).DelayMilliseconds);
        Assert.Equal(2, Assert.Single(saved.Interactions).VariableCodes.Count);
        Assert.Single(saved.FailureConditions);
        Assert.Single(saved.FalsificationConditions);
    }

    [Fact]
    public void Api_ExposesOnlySourceMaterializationForExperimentResults()
    {
        var postRoutes = typeof(ResearchProjectsController)
            .GetMethods()
            .SelectMany(static method => method.CustomAttributes)
            .Where(static attribute => attribute.AttributeType.Name == "HttpPostAttribute")
            .SelectMany(static attribute => attribute.ConstructorArguments)
            .Select(static argument => argument.Value as string)
            .Where(static value => value is not null)
            .ToArray();

        Assert.Contains(
            "experiments/{experimentId:guid}/materialize-result",
            postRoutes);
        Assert.Contains(
            "experiments/{experimentId:guid}/runs/{suggestionExecutionKey}/shadow-decision",
            postRoutes);
        Assert.Contains(
            "shadow-recommendations/{recommendationId:guid}/materialize-outcome",
            postRoutes);
        Assert.DoesNotContain(
            "experiments/{experimentId:guid}/results",
            postRoutes);
    }

    [Fact]
    public async Task ProjectActivation_RequiresCurrentIndependentlyReviewedStageZeroPreregistration()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");

        var missing = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ChangeProjectStatusAsync(
                project.ProjectId, ResearchProjectStatuses.Active, "engineer-a"));
        Assert.Contains("阶段 0", missing.Message, StringComparison.Ordinal);

        var service = new ResearchValidationPreregistrationService(
            store,
            new StubReliabilityBaselineService());
        var frozen = await service.FreezeAsync(
            project.ProjectId, ValidPreregistrationRequest(), "engineer-a");
        Assert.Equal(64, frozen.ContentHash.Length);
        Assert.Equal(45, frozen.Plan.EngineerWorkflowBaselines.Single().TotalMinutes);
        Assert.Equal(12, frozen.ReliabilityBaseline.AnalyzedRunCount);
        Assert.Equal(0.75, frozen.ReliabilityBaseline.Rates.Single().Rate);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.ReviewAsync(frozen.PreregistrationId, "engineer-a"));
        var reviewed = await service.ReviewAsync(frozen.PreregistrationId, "engineer-b");
        Assert.Equal(ResearchValidationPreregistrationStatuses.Reviewed, reviewed.Status);
        Assert.True((await service.AssessAsync(project.ProjectId)).Eligible);

        var replacement = await service.FreezeAsync(
            project.ProjectId,
            ValidPreregistrationRequest() with { DataScope = "收窄到同设备、同材料批次" },
            "engineer-a");
        var awaitingCurrentReview = await service.AssessAsync(project.ProjectId);
        Assert.False(awaitingCurrentReview.Eligible);
        Assert.Contains("v2", Assert.Single(awaitingCurrentReview.Failures), StringComparison.Ordinal);
        await service.ReviewAsync(replacement.PreregistrationId, "engineer-b");
        Assert.True((await service.AssessAsync(project.ProjectId)).Eligible);

        var active = await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        Assert.Equal(ResearchProjectStatuses.Active, active.Status);
        Assert.True((await service.AssessAsync(project.ProjectId)).Eligible);
    }

    [Fact]
    public async Task ActivatingProject_ShouldFreezePublishedScenarioContextPolicy()
    {
        var scenario = ResearchContextAdmissionEvaluatorTests.OpticalScenario();
        var store = new MemoryStore();
        var workflow = CreateWorkflow(
            store,
            processConfigurations: new ScenarioOnlyConfigurationStore(scenario));
        var draft = await workflow.CreateProjectAsync(
            ProjectDraft() with
            {
                Context = new Dictionary<string, string>
                {
                    [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] =
                        $"{scenario.PackageId}:{scenario.Version}",
                    [ResearchContextAdmissionEvaluator.PolicyHashContextKey] = "client-supplied-value"
                }
            },
            "engineer-a");
        Assert.False(draft.Context.ContainsKey(ResearchContextAdmissionEvaluator.PolicyHashContextKey));
        await FreezeAndReviewStageZeroAsync(store, draft);

        var active = await workflow.ChangeProjectStatusAsync(
            draft.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");

        Assert.Equal(
            ResearchContextAdmissionEvaluator.ComputePolicyHash(scenario),
            active.Context[ResearchContextAdmissionEvaluator.PolicyHashContextKey]);
        var changedContext = new Dictionary<string, string>(active.Context, StringComparer.Ordinal)
        {
            [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] = "another-package:1"
        };
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.UpdateProjectAsync(
                active.ProjectId,
                active with { Context = changedContext },
                "engineer-a"));
    }

    [Fact]
    public async Task ActivatingProject_ShouldRejectMutableDraftScenarioPackage()
    {
        var scenario = ResearchContextAdmissionEvaluatorTests.OpticalScenario() with
        {
            Status = ConfigurationStatuses.Draft
        };
        var store = new MemoryStore();
        var workflow = CreateWorkflow(
            store,
            processConfigurations: new ScenarioOnlyConfigurationStore(scenario));
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with
            {
                Context = new Dictionary<string, string>
                {
                    [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] =
                        $"{scenario.PackageId}:{scenario.Version}"
                }
            },
            "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ChangeProjectStatusAsync(
                project.ProjectId,
                ResearchProjectStatuses.Active,
                "engineer-a"));
        Assert.Contains("已发布", error.Message);
    }

    [Fact]
    public async Task GeneralProjectUpdate_ShouldPreserveServerManagedMembers()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { MemberUserIds = ["engineer-a", "engineer-b"] },
            "engineer-a");

        var updated = await workflow.UpdateProjectAsync(
            project.ProjectId,
            project with { Name = "renamed", MemberUserIds = ["attacker"] },
            "engineer-b");

        Assert.Equal(["engineer-a", "engineer-b"], updated.MemberUserIds);
        Assert.DoesNotContain("attacker", updated.MemberUserIds);
    }

    [Fact]
    public async Task MemberManagement_ShouldRequireOwnerOrAdministratorAndRevision()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { MemberUserIds = ["engineer-a", "engineer-b"] },
            "engineer-a");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            workflow.UpdateProjectMembersAsync(
                project.ProjectId,
                project.Revision,
                ["engineer-b", "engineer-c"],
                "engineer-b",
                false));

        var saved = await workflow.UpdateProjectMembersAsync(
            project.ProjectId,
            project.Revision,
            ["engineer-c"],
            "engineer-a",
            false);
        Assert.Equal(["engineer-c", "engineer-a"], saved.MemberUserIds);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.UpdateProjectMembersAsync(
                project.ProjectId,
                project.Revision,
                ["engineer-d"],
                "admin",
                true));
    }

    [Fact]
    public async Task ResearchProject_CompletesOnlyAfterValidatedOperatingRegion()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");

        var hypothesis = await workflow.SaveHypothesisAsync(
            project.ProjectId,
            new ResearchHypothesis
            {
                Statement = "保压温度和压力共同影响面形误差。",
                Rationale = "历史过程执行、物理机理和专家经验均指向该交互关系。",
                VariableCodes = ["holding-temperature", "press-force"],
                ValidationOutcomeCode = "form-error",
                ExpectedEffectDirection = ResearchHypothesisEffectDirections.Decrease,
                MinimumEffect = 0.2,
                FalsificationConditions = ["重复受控实验未观察到预期方向和最小效应时推翻该假设。"],
                Confidence = 0.6
            },
            "engineer-a");

        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
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
                RollbackPlan = "恢复已验证基线工艺规范。"
            },
            "engineer-a");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        var result = await workflow.RecordMaterializedExperimentResultAsync(
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
                        ExecutionKey = "low-low",
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
                        ExecutionKey = "high-high",
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
                // 调用方传入的汇总计数不可信，工作流必须从计划和观察重新计算。
                RunCount = 99,
                ReplicateCount = 99,
                DistinctBlockCount = 99,
                DistinctMaterialLotCount = 99,
                DistinctEquipmentCount = 99,
                SafetyPassed = true,
                CalculatedFromSource = true
            },
            "engineer-a");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Completed,
            "engineer-a");

        var window = await workflow.SaveOperatingRegionAsync(
            project.ProjectId,
            new ResearchOperatingRegion
            {
                Name = "稳定成形窗口",
                Variables =
                [
                    new OperatingRegionVariable
                    {
                        VariableCode = "holding-temperature",
                        LowerBound = 520,
                        UpperBound = 520,
                        Unit = "Cel"
                    },
                    new OperatingRegionVariable
                    {
                        VariableCode = "press-force",
                        LowerBound = 12,
                        UpperBound = 12,
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
        var prematureValidation = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ValidateOperatingRegionAsync(window.OperatingRegionId, "engineer-b"));
        Assert.Contains("独立验证实验", prematureValidation.Message);
        await CompleteIndependentValidationAsync(workflow, window);
        window = await workflow.ValidateOperatingRegionAsync(window.OperatingRegionId, "engineer-b");
        Assert.Equal(OperatingRegionValidationLevels.Laboratory, window.ValidationLevel);
        var prematureRelease = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ReleaseOperatingRegionAsync(window.OperatingRegionId, "engineer-c"));
        Assert.Contains("受控在线运行", prematureRelease.Message);
        await SeedControlledOnlineValidationAsync(store, window);
        window = await workflow.ReleaseOperatingRegionAsync(window.OperatingRegionId, "engineer-c");
        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Completed,
            "engineer-a");

        Assert.Equal(OperatingRegionStatuses.Validated, window.Status);
        Assert.Equal(OperatingRegionValidationLevels.Production, window.ValidationLevel);
        Assert.Equal(ResearchProjectStatuses.Completed, project.Status);
        Assert.Equal(2, result.RunObservations.Count);
        Assert.Equal(2, result.RunCount);
        Assert.Equal(1, result.ReplicateCount);
        Assert.Equal(1, result.DistinctBlockCount);
        Assert.Equal(0, result.DistinctMaterialLotCount);
        Assert.Equal(0, result.DistinctEquipmentCount);
        var workspace = await workflow.GetWorkspaceAsync(project.ProjectId);
        Assert.Single(workspace.Hypotheses);
        Assert.Equal(ResearchHypothesisStatuses.Supported, workspace.Hypotheses[0].Status);
        Assert.Single(workspace.Hypotheses[0].ValidationEvidence);
        Assert.Equal(3, workspace.Experiments.Count);
        Assert.Single(workspace.OperatingRegions);
    }

    [Fact]
    public async Task Experiment_CreatorCannotApproveOwnPlan()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");
        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
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
                RollbackPlan = "恢复基线工艺规范。"
            },
            "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ExperimentCommands.ChangeExperimentStatusAsync(
                experiment.ExperimentId,
                ResearchExperimentStatuses.Approved,
                "engineer-a"));

        Assert.Contains("创建人和批准人必须分离", error.Message);
    }

    [Fact]
    public async Task Experiment_CannotCompleteWithoutCalculatedResult()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");
        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
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
                RollbackPlan = "恢复基线工艺规范。"
            },
            "engineer-a");
        await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.ExperimentCommands.ChangeExperimentStatusAsync(
                experiment.ExperimentId,
                ResearchExperimentStatuses.Completed,
                "engineer-a"));

        Assert.Contains("必须记录由源数据计算得到的结果", error.Message);
    }

    [Fact]
    public void OptimizerRejectsConditionsBelowObservedProcessResolution()
    {
        var controls = new Dictionary<string, ResearchVariable>(StringComparer.Ordinal)
        {
            ["vacuum-position"] = new()
            {
                Code = "vacuum-position",
                Name = "真空位置",
                Role = ResearchVariableRoles.Control,
                Unit = "mm",
                LowerLimit = 24.25,
                UpperLimit = 25.352
            }
        };
        var observations = new[] { 24.25, 25.25, 25.352 }
            .Select(value => new OptimizerObservationInput
            {
                Params = new Dictionary<string, double> { ["vacuum-position"] = value }
            })
            .ToArray();
        var suggestions = new[] { 25.35040, 25.35071 }
            .Select(value => new OptimizerSuggestionOutput
            {
                RecommendedParameters = new Dictionary<string, double>
                {
                    ["vacuum-position"] = value
                }
            })
            .ToArray();

        var error = Assert.Throws<ProcessResearchRuleException>(() =>
            ResearchExperimentOptimizer.EnsureExperimentConditionsAreDistinguishable(
                suggestions,
                observations,
                controls));

        Assert.Contains("不能伪装成两个实验条件", error.Message);
    }

    [Fact]
    public void MechanismKnowledge_NarrowsHardBoundsAndRanksOnlyApplicableActiveClaims()
    {
        var projectId = Guid.CreateVersion7();
        var project = ProjectDraft() with
        {
            ProjectId = projectId,
            Context = new Dictionary<string, string> { ["material-grade"] = "A" }
        };
        var claim = new MechanismClaimVersion
        {
            ClaimId = Guid.CreateVersion7(),
            ProjectId = projectId,
            Status = MechanismClaimStatuses.Active,
            Name = "安全工艺窗",
            MechanismType = "constraint",
            Statement = "材料 A 仅在收窄工艺窗内稳定。",
            FalsificationCondition = "重复实验显示窗外仍稳定。",
            Applicability =
            [
                new MechanismClaimApplicability
                    { DimensionCode = "material-grade", DimensionValue = "A" }
            ],
            Constraints =
            [
                new MechanismClaimConstraint
                {
                    VariableCode = "holding-temperature", ConstraintKind = "safe-range",
                    Minimum = 500, Maximum = 530, Unit = "Cel", Severity = "hard"
                },
                new MechanismClaimConstraint
                {
                    VariableCode = "press-force", ConstraintKind = "preferred-range",
                    Minimum = 10, Maximum = 12, Unit = "kN", Severity = "soft"
                }
            ],
            CreatedBy = "engineer-a",
            ContentHash = new string('a', 64)
        };

        var selected = MechanismKnowledgeExperimentPolicy.Select(project, [claim], []);
        var campaign = MechanismKnowledgeExperimentPolicy.ApplyHardConstraints(
            ResearchExperimentOptimizer.BuildCampaign(
                project, ResearchOptimizationIntents.ReachSpecification, null),
            selected);
        Assert.Contains(campaign.Constraints, value =>
            value.Variable == "holding-temperature" && value.Operator == ">=" && value.Limit == 500);
        Assert.Contains(campaign.Constraints, value =>
            value.Variable == "holding-temperature" && value.Operator == "<=" && value.Limit == 530);

        var ranked = MechanismKnowledgeExperimentPolicy.Rank(
            [Suggestion(520, 18, 0.9), Suggestion(520, 11, 0.1)],
            selected,
            project.Variables.Where(value => value.Role == ResearchVariableRoles.Control)
                .ToDictionary(value => value.Code, StringComparer.Ordinal));
        Assert.Equal(18, ranked[0].RecommendedParameters["press-force"]);

        var error = Assert.Throws<ProcessResearchRuleException>(() =>
            MechanismKnowledgeExperimentPolicy.ValidateHardConstraints(
                Suggestion(540, 11, 0.5), selected));
        Assert.Contains("holding-temperature", error.Message);
    }

    [Fact]
    public async Task MechanismKnowledgeSnapshotChange_BlocksExistingExperiment()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        var emptySnapshot = MechanismKnowledgeExperimentPolicy.SnapshotHash(
            new AppliedMechanismKnowledge([], [], [], []));
        var claim = new MechanismClaimVersion
        {
            ClaimId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Version = 1,
            Status = MechanismClaimStatuses.Active,
            Name = "新激活的保压窗口",
            MechanismType = "constraint",
            Statement = "该知识在实验计划生成后才激活。",
            FalsificationCondition = "独立实验不支持该窗口。",
            Applicability =
            [
                new MechanismClaimApplicability
                    { DimensionCode = "project-code", DimensionValue = project.Code }
            ],
            ContentHash = new string('a', 64)
        };
        var knowledgeStore = new ReplayMechanismKnowledgeStore(claim);
        var gate = new ResearchExperimentKnowledgeGate(store, knowledgeStore);
        var experiment = new ResearchExperiment
        {
            ExperimentId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Name = "基于旧知识快照的实验",
            Status = ResearchExperimentStatuses.Planned,
            StopRule = "触发安全约束时停止。",
            RollbackPlan = "恢复已发布工艺规范。",
            RunPlan = [Run("snapshot-change", 1, 515, 10)],
            Optimization = new ResearchOptimizationMetadata
            {
                ModelVersion = "snapshot-test",
                InputHash = new string('b', 64),
                MechanismKnowledgeSnapshotHash = emptySnapshot,
                MechanismModelSnapshotHash = "none"
            }
        };

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => gate.ValidateAsync(experiment));

        Assert.Contains("机理知识已发生变化", error.Message);

        var currentKnowledge = MechanismKnowledgeExperimentPolicy.Select(project, [claim], []);
        await gate.ValidateAsync(experiment with
        {
            Optimization = experiment.Optimization with
            {
                MechanismKnowledgeSnapshotHash =
                    MechanismKnowledgeExperimentPolicy.SnapshotHash(currentKnowledge)
            }
        });
    }

    [Fact]
    public async Task ResultMaterializer_PersistsCompleteProcessExecutionObservationsAsFormalResult()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        var historical = await workflow.ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "历史观察",
                DesignMethod = ResearchDesignMethods.HistoricalObservation,
                RunPlan =
                [
                    Run("history-execution-1", 1, 490, 8),
                    Run("history-execution-2", 2, 500, 9),
                    Run("history-execution-unrelated", 3, 540, 18)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "只读取历史证据。",
                RollbackPlan = "不写入设备。"
            },
            "engineer-a");
        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "自动回灌实验",
                RunPlan =
                [
                    Run("auto-execution-1", 1, 510, 10),
                    Run("auto-execution-2", 2, 530, 14)
                ],
                BaselineExecutionKeys = ["history-execution-1", "history-execution-2"],
                ObjectiveCodes = ["form-error"],
                StopRule = "完成全部运行。",
                RollbackPlan = "恢复基线。"
            },
            "engineer-a");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        ExperimentRunObservation Observation(
            string executionKey,
            double temperature,
            double force,
            double outcome,
            char hash)
            => new()
            {
                ExecutionKey = executionKey,
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
                    ["mold-temperature.execution.average"] = temperature
                },
                Outcomes = new Dictionary<string, double> { ["form-error"] = outcome },
                SourceContentHash = new string(hash, 64)
            };
        var assembly = new ResearchObservationAssembly(
        [
            Observation("history-execution-1", 490, 8, 0.9, 'b'),
            Observation("history-execution-2", 500, 9, 0.7, 'c'),
            Observation("history-execution-unrelated", 540, 18, 9.9, 'f'),
            Observation("auto-execution-1", 510, 10, 0.52, 'd'),
            Observation("auto-execution-2", 530, 14, 0.37, 'e')
        ], 5);
        var materializer = new ResearchExperimentResultMaterializer(workflow);

        var results = await materializer.MaterializeCompletedAsync(
            project,
            [historical, experiment],
            [],
            assembly,
            "system-execution-materializer");

        var result = Assert.Single(results);
        Assert.Equal(2, result.RunObservations.Count);
        Assert.True(result.CalculatedFromSource);
        Assert.StartsWith("process-execution-observation-snapshot:", result.DatasetSnapshotId);
        var metric = Assert.Single(result.Metrics);
        Assert.Equal(0.8d, metric.BaselineValue, 6);
        Assert.Equal(0.445d, metric.ObservedValue, 6);
        Assert.Equal(-0.355d, metric.EffectValue, 6);
        Assert.Equal(
            metric.EffectValue,
            (metric.LowerConfidenceBound!.Value + metric.UpperConfidenceBound!.Value) / 2,
            6);
        Assert.Equal("two-sample-welch-effect-95ci-v2", metric.ComputationMethod);
        var savedExperiment = await store.GetExperimentAsync(experiment.ExperimentId);
        Assert.Contains(result.ResultId, savedExperiment!.ResultIds);
        Assert.Equal(ResearchExperimentStatuses.Completed, savedExperiment.Status);
        Assert.Equal(
            ResearchExperimentExecutionStates.Completed,
            savedExperiment.Execution?.State);
        Assert.Contains(
            await store.ListAuditEntriesAsync(project.ProjectId),
            value => value.ResourceType == "experiment-result" &&
                     value.ResourceId == result.ResultId.ToString());
    }

    [Fact]
    public async Task ResultMaterializer_WithoutExplicitBaselineDoesNotInventConfidenceInterval()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with
            {
                Objectives =
                [
                    ProjectDraft().Objectives[0] with { Baseline = 0.8 }
                ]
            },
            "engineer-a");
        var historical = await workflow.ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "不相关历史观察",
                DesignMethod = ResearchDesignMethods.HistoricalObservation,
                RunPlan =
                [
                    Run("unrelated-1", 1, 490, 8),
                    Run("unrelated-2", 2, 500, 9)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "只读取历史证据。",
                RollbackPlan = "不写入设备。"
            },
            "engineer-a");
        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "未声明对照的实验",
                RunPlan =
                [
                    Run("current-1", 1, 510, 10),
                    Run("current-2", 2, 530, 14)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "完成全部运行。",
                RollbackPlan = "恢复基线。"
            },
            "engineer-a");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");

        ExperimentRunObservation Observation(string executionKey, double outcome, char hash)
            => new()
            {
                ExecutionKey = executionKey,
                ActualFactors = executionKey.EndsWith('1')
                    ? Run(executionKey, 1, 510, 10).Factors
                    : Run(executionKey, 2, 530, 14).Factors,
                Outcomes = new Dictionary<string, double> { ["form-error"] = outcome },
                SourceContentHash = new string(hash, 64)
            };
        var materializer = new ResearchExperimentResultMaterializer(workflow);

        var results = await materializer.MaterializeCompletedAsync(
            project,
            [historical, experiment],
            [],
            new ResearchObservationAssembly(
            [
                Observation("unrelated-1", 7.0, 'a'),
                Observation("unrelated-2", 9.0, 'b'),
                Observation("current-1", 0.5, 'c'),
                Observation("current-2", 0.3, 'd')
            ], 4),
            "system-execution-materializer");

        var metric = Assert.Single(Assert.Single(results).Metrics);
        Assert.Equal(0, metric.BaselineSampleCount);
        Assert.Equal(0.8, metric.BaselineValue, 6);
        Assert.Null(metric.LowerConfidenceBound);
        Assert.Null(metric.UpperConfidenceBound);
        Assert.Equal("descriptive-effect-no-independent-control-v2", metric.ComputationMethod);
    }

    [Fact]
    public async Task MaterializedResultAudit_UsesComputedSafetyOutcome()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var draft = ProjectDraft();
        var project = await workflow.CreateProjectAsync(
            draft with
            {
                OutcomeConstraints =
                [
                    new ResearchOutcomeConstraint
                    {
                        Code = "form-error-safety",
                        Description = "面形误差安全上限",
                        OutcomeCode = "form-error",
                        Operator = "<=",
                        Limit = 0.5,
                        Unit = "um"
                    }
                ]
            },
            "engineer-a");
        var experiment = await workflow.ExperimentCommands.CreateExperimentAsync(
            project.ProjectId,
            new ResearchExperiment
            {
                Name = "安全审计验证",
                RunPlan =
                [
                    Run("safe-run", 1, 510, 10),
                    Run("unsafe-run", 2, 530, 14)
                ],
                ObjectiveCodes = ["form-error"],
                StopRule = "安全约束触发时停止。",
                RollbackPlan = "恢复基线。"
            },
            "engineer-a");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        ExperimentRunObservation Observation(string executionKey, double outcome, char hash)
            => new()
            {
                ExecutionKey = executionKey,
                ActualFactors = experiment.RunPlan.Single(value => value.ExecutionKey == executionKey).Factors,
                Outcomes = new Dictionary<string, double> { ["form-error"] = outcome },
                ConstraintOutcomes = new Dictionary<string, double>
                {
                    ["form-error-safety"] = outcome
                },
                SourceContentHash = new string(hash, 64)
            };

        var result = await workflow.RecordMaterializedExperimentResultAsync(
            experiment.ExperimentId,
            new ResearchExperimentResult
            {
                DatasetSnapshotId = "computed-safety-audit",
                Metrics =
                [
                    new ExperimentMetricResult
                    {
                        ObjectiveCode = "form-error",
                        BaselineValue = 0.4,
                        ObservedValue = 0.55,
                        EffectValue = 0.15,
                        Unit = "um",
                        BaselineSampleCount = 0,
                        ExperimentSampleCount = 2,
                        ComputationMethod = "descriptive-effect-no-independent-control-v2"
                    }
                ],
                RunObservations =
                [
                    Observation("safe-run", 0.4, 'a'),
                    Observation("unsafe-run", 0.7, 'b')
                ],
                SafetyPassed = true,
                CalculatedFromSource = true
            },
            "system-execution-materializer");

        Assert.False(result.SafetyPassed);
        Assert.Contains(
            await store.ListAuditEntriesAsync(project.ProjectId),
            value => value.ResourceType == "experiment-result" &&
                     value.ResourceId == result.ResultId.ToString() &&
                     value.ToStatus == "failed");
    }

    [Fact]
    public async Task Optimizer_CreatesAnOrdinaryExperimentFromPerRunObservations()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = Guid.CreateVersion7(),
            DatasetSnapshotId = "fx3u-execution-snapshot",
            RunObservations =
            [
                new ExperimentRunObservation
                {
                    ExecutionKey = "fx3u-execution-1",
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
                },
                new ExperimentRunObservation
                {
                    ExecutionKey = "fx3u-execution-context-missing",
                    ActualFactors =
                    [
                        new ExperimentFactorSetting
                        {
                            VariableCode = "holding-temperature", Value = 520, Unit = "Cel"
                        },
                        new ExperimentFactorSetting
                        {
                            VariableCode = "press-force", Value = 12, Unit = "kN"
                        }
                    ],
                    Outcomes = new Dictionary<string, double> { ["form-error"] = 0.7 },
                    ValidForOptimization = false,
                    ExclusionReason = "缺少分析必需上下文：tooling_assembly_id",
                    SourceContentHash = new string('d', 64)
                }
            ]
        });
        var optimizer = new ResearchExperimentOptimizer(
            store,
            new StubOptimizerClient(),
            new EmptyObservationAssembler(),
            new ResearchExperimentResultMaterializer(workflow),
            workflow.ExperimentCommands,
            workflow);

        var experiment = await optimizer.CreateNextExperimentAsync(
            project.ProjectId,
            new ResearchOptimizationRequest
            {
                BatchSize = 2,
                ReplicatesPerCondition = 2,
                Seed = 7
            },
            "engineer-a");
        var repeated = await optimizer.CreateNextExperimentAsync(
            project.ProjectId,
            new ResearchOptimizationRequest
            {
                BatchSize = 2,
                ReplicatesPerCondition = 2,
                Seed = 7
            },
            "engineer-a");

        Assert.Equal(ResearchDesignMethods.BayesianOptimization, experiment.DesignMethod);
        Assert.Equal(experiment.ExperimentId, repeated.ExperimentId);
        Assert.Equal(ResearchExperimentStatuses.Planned, experiment.Status);
        Assert.Equal(4, experiment.RunPlan.Count);
        Assert.Equal(2, experiment.RunPlan.Select(static value => value.BlockKey).Distinct().Count());
        Assert.All(
            experiment.RunPlan.GroupBy(static value => value.ReplicateKey),
            static group => Assert.Equal(2, group.Count()));
        Assert.Equal(4, experiment.Execution?.Commands.Count);
        Assert.NotNull(experiment.Optimization);
        Assert.Equal(1, experiment.Optimization.ObservationCount);
        Assert.Equal("botorch-qlogbo-test", experiment.Optimization.ModelVersion);
        Assert.Equal("declared-test-features", experiment.Optimization.FeatureSetId);
        Assert.Equal(1, experiment.Optimization.DerivedFeatureCount);

        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Approved,
            "engineer-b");
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId,
            ResearchExperimentStatuses.Running,
            "engineer-a");
        var assembly = new ResearchObservationAssembly(
            experiment.RunPlan.Select((run, index) => new ExperimentRunObservation
            {
                ExecutionKey = run.ExecutionKey,
                ActualFactors = run.Factors,
                Outcomes = new Dictionary<string, double>
                {
                    ["form-error"] = index % 2 == 0 ? 0.22 : 0.24
                },
                SourceContentHash = new string("defa"[index], 64)
            }).ToArray(),
            experiment.RunPlan.Count);
        var operatingRegionMaterializer = new ResearchOperatingRegionMaterializer(store, workflow);
        var resultMaterializer = new ResearchExperimentResultMaterializer(
            workflow,
            operatingRegionMaterializer,
            store);
        var results = await resultMaterializer.MaterializeCompletedAsync(
            project,
            [experiment],
            await store.ListExperimentResultsAsync(project.ProjectId),
            assembly,
            "system-research-automation");
        var result = Assert.Single(results);
        Assert.Equal(2, result.ReplicateCount);
        Assert.Equal(2, result.DistinctBlockCount);
        var candidate = Assert.Single(await store.ListOperatingRegionsAsync(project.ProjectId));
        Assert.Equal(OperatingRegionStatuses.Candidate, candidate.Status);
        Assert.Equal(OperatingRegionValidationLevels.Evidence, candidate.ValidationLevel);
        Assert.All(candidate.Variables, static variable =>
            Assert.Equal(variable.LowerBound, variable.UpperBound));

        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Active,
            "engineer-a");
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Validating,
            "engineer-a");
        await CompleteIndependentValidationAsync(workflow, candidate);
        var validated = await workflow.ValidateOperatingRegionAsync(
            candidate.OperatingRegionId,
            "engineer-b");
        Assert.Equal(OperatingRegionValidationLevels.Laboratory, validated.ValidationLevel);
    }

    [Fact]
    public async Task Project_RejectsHiddenOrInvalidDerivedFeatureLogic()
    {
        var workflow = CreateWorkflow(new MemoryStore());
        var invalid = ProjectDraft() with
        {
            Code = "invalid-derived-feature",
            OptimizationFeatures = new ResearchOptimizationFeatureSet
            {
                FeatureSetId = "invalid-feature-set",
                DerivedFeatures =
                [
                    new ResearchDerivedFeature
                    {
                        Name = "hidden-domain-rule",
                        Operator = ResearchDerivedFeatureOperators.Identity,
                        Inputs = ["variable-guessed-from-name"]
                    }
                ]
            }
        };

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => workflow.CreateProjectAsync(invalid, "engineer-a"));

        Assert.Contains("未知或尚未定义", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransferAssessment_ComparesColdStartAndDetectsNegativeTransfer()
    {
        var store = new MemoryStore();
        var now = DateTimeOffset.UtcNow;
        var source = TransferProject("source", "material-a", now);
        var target = TransferProject("target", "material-b", now) with
        {
            ProjectId = Guid.CreateVersion7(),
            Code = "target-transfer"
        };
        await store.SaveProjectAsync(source);
        await store.SaveProjectAsync(target);
        var window = new ResearchOperatingRegion
        {
            OperatingRegionId = Guid.CreateVersion7(),
            ProjectId = source.ProjectId,
            Name = "Released source window",
            Status = OperatingRegionStatuses.Validated,
            ValidationLevel = OperatingRegionValidationLevels.Production,
            Variables =
            [
                new OperatingRegionVariable
                {
                    VariableCode = "temperature",
                    LowerBound = 500,
                    UpperBound = 540,
                    Unit = "Cel"
                }
            ],
            ObjectiveCodes = ["error"],
            Confidence = 0.9,
            ConfidenceMethod = ResearchConfidenceMethods.Frequentist,
            AnalysisHash = new string('a', 64),
            Applicability = "same process, measured target context",
            CreatedBy = "engineer-a",
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveOperatingRegionAsync(window);
        var coldStart = TransferResult(target.ProjectId, 0.8, true, 'b', now);
        var transferred = TransferResult(target.ProjectId, 0.3, true, 'c', now);
        await store.SaveExperimentResultAsync(coldStart);
        await store.SaveExperimentResultAsync(transferred);

        var service = new ResearchTransferAssessmentService(store);
        var beneficial = await service.AssessAsync(
            target.ProjectId,
            new ResearchTransferAssessmentRequest
            {
                SourceOperatingRegionId = window.OperatingRegionId,
                TransferResultId = transferred.ResultId,
                ColdStartResultId = coldStart.ResultId
            },
            "engineer-a");

        Assert.Equal(ResearchTransferOutcomes.Beneficial, beneficial.Outcome);
        Assert.True(beneficial.EvidenceSufficient);
        Assert.True(beneficial.SchemaCompatible);
        Assert.True(beneficial.RelativeGain > 0.05);
        Assert.Contains(beneficial.ContextDifferences, item => item.Field == "material");
        await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => service.ReviewAsync(beneficial.AssessmentId, "engineer-a"));
        var reviewed = await service.ReviewAsync(beneficial.AssessmentId, "engineer-b");
        Assert.Equal(ResearchTransferAssessmentStatuses.Reviewed, reviewed.Status);

        var workflow = CreateWorkflow(store);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.SaveKnowledgeClaimAsync(
                target.ProjectId,
                new ResearchKnowledgeClaim
                {
                    TransferAssessmentId = reviewed.AssessmentId,
                    Statement = "源窗口可迁移到目标材料条件。",
                    Applicability = "当前生产线和已验证材料条件。"
                },
                "engineer-a"));
        var secondTransferred = TransferResult(target.ProjectId, 0.25, true, 'e', now);
        await store.SaveExperimentResultAsync(secondTransferred);
        var secondAssessment = await service.AssessAsync(
            target.ProjectId,
            new ResearchTransferAssessmentRequest
            {
                SourceOperatingRegionId = window.OperatingRegionId,
                TransferResultId = secondTransferred.ResultId,
                ColdStartResultId = coldStart.ResultId
            },
            "engineer-a");
        secondAssessment = await service.ReviewAsync(secondAssessment.AssessmentId, "engineer-b");
        var claim = await workflow.SaveKnowledgeClaimAsync(
            target.ProjectId,
            new ResearchKnowledgeClaim
            {
                TransferAssessmentId = secondAssessment.AssessmentId,
                Statement = "源窗口在目标材料条件下相对从零建立有重复收益。",
                Applicability = "当前生产线和已验证材料条件。"
            },
            "engineer-a");
        Assert.Contains(claim.Evidence,
            item => item.Kind == EvidenceKinds.TransferAssessment &&
                    item.ReferenceId == secondAssessment.AssessmentId.ToString());

        var regressed = TransferResult(target.ProjectId, 1.1, true, 'd', now);
        await store.SaveExperimentResultAsync(regressed);
        var negative = await service.AssessAsync(
            target.ProjectId,
            new ResearchTransferAssessmentRequest
            {
                SourceOperatingRegionId = window.OperatingRegionId,
                TransferResultId = regressed.ResultId,
                ColdStartResultId = coldStart.ResultId
            },
            "engineer-a");
        Assert.Equal(ResearchTransferOutcomes.NegativeTransfer, negative.Outcome);
        Assert.True(negative.NegativeTransferDetected);
    }

    private static ResearchProject TransferProject(string code, string material, DateTimeOffset now)
        => new()
        {
            ProjectId = Guid.CreateVersion7(),
            Code = code,
            Name = code,
            ProcessName = "precision forming",
            MaterialName = material,
            SiteCode = "plant-a",
            Status = ResearchProjectStatuses.Active,
            Objectives =
            [
                new ResearchObjective
                {
                    Code = "error",
                    Name = "Error",
                    Unit = "um",
                    Direction = "minimize",
                    Baseline = 0.8,
                    Target = 0.2,
                    UpperLimit = 0.4
                }
            ],
            Variables =
            [
                new ResearchVariable
                {
                    Code = "temperature",
                    Name = "Temperature",
                    Role = ResearchVariableRoles.Control,
                    Unit = "Cel",
                    LowerLimit = 480,
                    UpperLimit = 560
                }
            ],
            OwnerUserId = "engineer-a",
            MemberUserIds = ["engineer-a", "engineer-b"],
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };

    private static ResearchExperimentResult TransferResult(
        Guid projectId,
        double observed,
        bool safetyPassed,
        char hashCharacter,
        DateTimeOffset now)
    {
        var observations = Enumerable.Range(1, 3).Select(index => new ExperimentRunObservation
        {
            ExecutionKey = $"transfer-{hashCharacter}-{index}",
            ActualFactors =
            [
                new ExperimentFactorSetting
                {
                    VariableCode = "temperature",
                    Value = 520,
                    Unit = "Cel"
                }
            ],
            Outcomes = new Dictionary<string, double> { ["error"] = observed },
            SourceContentHash = new string(hashCharacter, 64)
        }).ToArray();
        return new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = projectId,
            ExperimentId = Guid.CreateVersion7(),
            DatasetSnapshotId = $"snapshot-{hashCharacter}",
            AnalysisRunId = Guid.CreateVersion7(),
            AnalysisHash = new string(hashCharacter, 64),
            Metrics =
            [
                new ExperimentMetricResult
                {
                    ObjectiveCode = "error",
                    BaselineValue = 0.8,
                    ObservedValue = observed,
                    EffectValue = observed - 0.8,
                    Unit = "um",
                    BaselineSampleCount = 3,
                    ExperimentSampleCount = 3,
                    ComputationMethod = "source mean"
                }
            ],
            RunObservations = observations,
            RunCount = 3,
            ReplicateCount = 3,
            DistinctBlockCount = 2,
            SafetyPassed = safetyPassed,
            CalculatedFromSource = true,
            RecordedBy = "system",
            RecordedAt = now
        };
    }

    [Fact]
    public async Task ShadowRecommendation_PreregistersDecision_ThenFreezesSourceOutcome()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var experiment = await CreateOptimizationExperimentAsync(workflow, project.ProjectId);
        var dispatchError = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ExperimentCommands.ChangeExperimentStatusAsync(
                experiment.ExperimentId, ResearchExperimentStatuses.Approved, "engineer-b"));
        Assert.Contains("不能批准", dispatchError.Message, StringComparison.Ordinal);
        var assembler = new StubShadowObservationAssembler(new ExperimentRunObservation
        {
            ExecutionKey = "production-execution-001",
            Context = new Dictionary<string, string>
            {
                ["equipment_id"] = "press-01",
                ["material_lot_ref"] = "lot-b"
            },
            ActualFactors =
            [
                new ExperimentFactorSetting
                    { VariableCode = "holding-temperature", Value = 521, Unit = "Cel" },
                new ExperimentFactorSetting
                    { VariableCode = "press-force", Value = 12.2, Unit = "kN" }
            ],
            ProcessFeatures = new Dictionary<string, double> { ["temperature.average"] = 520.4 },
            Outcomes = new Dictionary<string, double> { ["form-error"] = 0.31 },
            SourceContentHash = new string('a', 64)
        });
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = experiment.ExperimentId,
            DatasetSnapshotId = "historical-applicability",
            RunObservations =
            [
                new ExperimentRunObservation
                {
                    ExecutionKey = "historical-1",
                    Context = new Dictionary<string, string> { ["equipment_id"] = "press-00" },
                    ActualFactors = experiment.RunPlan[0].Factors,
                    SourceContentHash = new string('1', 64)
                },
                new ExperimentRunObservation
                {
                    ExecutionKey = "historical-2",
                    Context = new Dictionary<string, string> { ["equipment_id"] = "press-00" },
                    ActualFactors = experiment.RunPlan[1].Factors,
                    SourceContentHash = new string('2', 64)
                }
            ]
        });
        var service = new ResearchShadowRecommendationService(store, assembler);

        var recorded = await service.RecordDecisionAsync(
            experiment.ExperimentId,
            experiment.RunPlan[0].ExecutionKey,
            new ResearchShadowDecisionRequest
            {
                Decision = ResearchShadowDecisionStatuses.Modified,
                ActualExecutionKey = "production-execution-001",
                EngineerSelectedFactors =
                [
                    new ExperimentFactorSetting
                        { VariableCode = "holding-temperature", Value = 520, Unit = "Cel" },
                    new ExperimentFactorSetting
                        { VariableCode = "press-force", Value = 12, Unit = "kN" }
                ],
                RejectionReason = "当前材料批次要求降低升温幅度。",
                UsefulnessRating = ResearchUsefulnessRatings.PartlyUseful,
                SiteLimitations = ["材料批次升温速率限制"],
                ContextSnapshot = new Dictionary<string, string>
                {
                    ["equipment_id"] = "press-01",
                    ["material_lot_ref"] = "lot-b"
                }
            },
            "engineer-b");

        Assert.Equal(64, recorded.DecisionSnapshotHash.Length);
        Assert.Equal("botorch-test", recorded.ModelVersion);
        Assert.Null(recorded.Outcome);
        Assert.Equal("optical-molding-window", recorded.ContextSnapshot["project_code"]);
        Assert.Equal(ResearchApplicabilityStatuses.ContextShift, recorded.Applicability.Status);
        Assert.Contains("equipment_id=press-01", recorded.Applicability.UnseenContextValues);
        var completed = await service.MaterializeOutcomeAsync(
            recorded.RecommendationId, "engineer-b");
        Assert.NotNull(completed.Outcome);
        Assert.Equal(6, completed.Outcome.SettingDeviationFromSuggestion["holding-temperature"]);
        Assert.Equal(1, completed.Outcome.SettingDeviationFromEngineerSelection["holding-temperature"]);
        Assert.Equal(0.31, completed.Outcome.Outcomes["form-error"]);
        Assert.Equal(new string('a', 64), completed.Outcome.SourceContentHash);
        Assert.Equal("production-execution-001", assembler.RequestedExecutionKey);
        var report = await service.BuildReportAsync(project.ProjectId);
        Assert.Equal(1, report.TotalRecommendations);
        Assert.Equal(1, report.ModifiedCount);
        Assert.Equal(1, report.PartlyUsefulCount);
        Assert.Equal(0, report.UnratedUsefulnessCount);
        Assert.Equal(1, report.CompletedOutcomeCount);
        Assert.Equal(1, report.ContextShiftCount);
        Assert.Equal(1, report.SettingDeviationCount);
        Assert.Equal(1, Assert.Single(report.Calibration).CoveredCount);
        Assert.Empty(report.SafetyEvents);
        Assert.False(report.StopRecommended);
        Assert.Equal(64, report.ReportHash.Length);

        var frozen = await service.MaterializeOutcomeAsync(
            recorded.RecommendationId, "engineer-c");
        Assert.Equal(completed.Outcome.CapturedAt, frozen.Outcome!.CapturedAt);
    }

    [Fact]
    public async Task ShadowRecommendation_RejectsDecisionWithoutContextOrConsistentFactors()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft(), "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var experiment = await CreateOptimizationExperimentAsync(workflow, project.ProjectId);
        var service = new ResearchShadowRecommendationService(
            store, new StubShadowObservationAssembler(null));
        var request = new ResearchShadowDecisionRequest
        {
            Decision = ResearchShadowDecisionStatuses.Accepted,
            ActualExecutionKey = "production-execution-002",
            EngineerSelectedFactors = experiment.RunPlan[0].Factors,
            ContextSnapshot = new Dictionary<string, string>()
        };

        var noContext = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.RecordDecisionAsync(
                experiment.ExperimentId, experiment.RunPlan[0].ExecutionKey,
                request, "engineer-b"));
        Assert.Contains("上下文", noContext.Message, StringComparison.Ordinal);

        var inconsistent = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.RecordDecisionAsync(
                experiment.ExperimentId,
                experiment.RunPlan[0].ExecutionKey,
                request with
                {
                    EngineerSelectedFactors =
                    [
                        new ExperimentFactorSetting
                            { VariableCode = "holding-temperature", Value = 520, Unit = "Cel" },
                        new ExperimentFactorSetting
                            { VariableCode = "press-force", Value = 12, Unit = "kN" }
                    ],
                    ContextSnapshot = new Dictionary<string, string> { ["equipment_id"] = "press-01" }
                },
                "engineer-b"));
        Assert.Contains("一致", inconsistent.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ShadowReport_StopsOnMeasuredSafetyViolation()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var draft = ProjectDraft() with
        {
            Code = "shadow-safety-report",
            OutcomeConstraints =
            [
                new ResearchOutcomeConstraint
                {
                    Code = "form-error-safety",
                    Description = "面形误差安全守门",
                    OutcomeCode = "form-error",
                    Operator = "<=",
                    Limit = 0.4,
                    Unit = "um",
                    SafetyCritical = true
                }
            ]
        };
        var project = await workflow.CreateProjectAsync(draft, "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var experiment = await CreateOptimizationExperimentAsync(workflow, project.ProjectId);
        var service = new ResearchShadowRecommendationService(
            store,
            new StubShadowObservationAssembler(new ExperimentRunObservation
            {
                ExecutionKey = "unsafe-run",
                ActualFactors = experiment.RunPlan[0].Factors,
                Outcomes = new Dictionary<string, double> { ["form-error"] = 0.52 },
                ConstraintOutcomes = new Dictionary<string, double>
                { ["form-error-safety"] = 0.52 },
                SourceContentHash = new string('e', 64)
            }));
        var decision = await service.RecordDecisionAsync(
            experiment.ExperimentId,
            experiment.RunPlan[0].ExecutionKey,
            new ResearchShadowDecisionRequest
            {
                Decision = ResearchShadowDecisionStatuses.Accepted,
                ActualExecutionKey = "unsafe-run",
                EngineerSelectedFactors = experiment.RunPlan[0].Factors,
                ContextSnapshot = new Dictionary<string, string> { ["equipment_id"] = "press-01" }
            },
            "engineer-b");
        await service.MaterializeOutcomeAsync(decision.RecommendationId, "engineer-b");

        var report = await service.BuildReportAsync(project.ProjectId);

        Assert.True(report.StopRecommended);
        Assert.Single(report.SafetyEvents);
        Assert.Contains(report.StopSignals, value =>
            value.Code == "safety-boundary-violation" && value.Severity == "stop");
    }

    [Fact]
    public async Task HistoricalReplay_FreezesCompleteRawEvidence_AndRequiresIndependentReview()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "historical-replay-proof" }, "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var observations = Enumerable.Range(0, 5).Select(index =>
            new ExperimentRunObservation
            {
                ExecutionKey = $"historical-{index + 1}",
                ActualFactors =
                [
                    new ExperimentFactorSetting
                    {
                        VariableCode = "holding-temperature",
                        Value = 500 + index * 5,
                        Unit = "Cel"
                    },
                    new ExperimentFactorSetting
                    {
                        VariableCode = "press-force",
                        Value = 8 + index,
                        Unit = "kN"
                    }
                ],
                ProcessFeatures = new Dictionary<string, double>
                { ["temperature.average"] = 500 + index * 5 },
                Outcomes = new Dictionary<string, double>
                { ["form-error"] = 0.8 - index * 0.12 },
                SourceContentHash = new string((char)('a' + index), 64)
            }).ToArray();
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = Guid.CreateVersion7(),
            DatasetSnapshotId = "five-real-conditions",
            RunObservations = observations,
            RecordedAt = DateTimeOffset.UtcNow
        });
        var optimizer = new StubReplayOptimizerClient();
        var mechanismStore = new ReplayMechanismKnowledgeStore(new MechanismClaimVersion
        {
            ClaimId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Version = 1,
            Status = MechanismClaimStatuses.Active,
            Name = "优选保压范围",
            MechanismType = "constraint",
            Statement = "优先在已验证保压范围内选择。",
            FalsificationCondition = "独立实验显示范围外更稳定。",
            Applicability = [new MechanismClaimApplicability { DimensionCode = "project-code", DimensionValue = project.Code }],
            Constraints = [new MechanismClaimConstraint { VariableCode = "holding-temperature", ConstraintKind = "preferred-range", Minimum = 505, Maximum = 520, Unit = "Cel", Severity = "soft" }],
            ContentHash = new string('f', 64)
        });
        var service = new ResearchHistoricalReplayService(store, optimizer, mechanismStore);

        var report = await service.RunAsync(
            project.ProjectId,
            new ResearchHistoricalReplayRequest
            {
                SeedCount = 2,
                Budget = 5,
                InitialObservationCount = 3
            },
            "engineer-a");

        Assert.True(report.GatePassed);
        Assert.Equal(5, report.UniqueConditionCount);
        Assert.Equal(5, report.SourceRunCount);
        Assert.Equal(1, report.PredictionIntervalCoverage);
        Assert.Equal(4, report.PredictionIntervalChecks);
        Assert.Equal(64, report.DatasetSnapshotHash.Length);
        Assert.Equal(64, report.PreregistrationHash.Length);
        Assert.Equal(64, report.ReportHash.Length);
        Assert.Equal(3, report.BaselineMethods.Count);
        Assert.NotNull(report.ResponseSurface);
        Assert.NotNull(report.MechanismComparison);
        Assert.Equal(0, report.MechanismComparison.SuccessRateDelta);
        Assert.Equal(2, optimizer.Calls.Count);
        Assert.Single(optimizer.Calls[0].SoftConstraints);
        Assert.Empty(optimizer.Calls[1].SoftConstraints);
        Assert.Equal(JsonValueKind.Array, report.RawResult.GetProperty("step_traces").ValueKind);
        Assert.Equal(5, optimizer.LastCall!.History.Count);
        Assert.All(optimizer.LastCall.History, value =>
            Assert.Contains("temperature.average", value.ProcessFeatures));
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.ReviewAsync(report.ReportId, "engineer-a"));

        var reviewed = await service.ReviewAsync(report.ReportId, "engineer-b");

        Assert.Equal(ResearchHistoricalReplayStatuses.Reviewed, reviewed.Status);
        Assert.Equal("engineer-b", reviewed.ReviewedBy);
        Assert.Equal(reviewed.ReviewedAt,
            (await service.ReviewAsync(report.ReportId, "engineer-c")).ReviewedAt);
    }

    [Theory]
    [InlineData("empty-traces")]
    [InlineData("current-row-visible")]
    [InlineData("selected-trace-mismatch")]
    public async Task HistoricalReplay_FailsGateForAdversarialOrIncompleteTrace(string mutation)
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = $"replay-adversarial-{mutation}" }, "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = Guid.CreateVersion7(),
            DatasetSnapshotId = mutation,
            RunObservations = Enumerable.Range(0, 5).Select(index => new ExperimentRunObservation
            {
                ExecutionKey = $"adversarial-{index}",
                ActualFactors =
                [
                    new ExperimentFactorSetting
                        { VariableCode = "holding-temperature", Value = 500 + index, Unit = "Cel" },
                    new ExperimentFactorSetting
                        { VariableCode = "press-force", Value = 8 + index, Unit = "kN" }
                ],
                ProcessFeatures = new Dictionary<string, double> { ["temperature.average"] = 500 + index },
                Outcomes = new Dictionary<string, double> { ["form-error"] = 0.8 - index * 0.1 },
                SourceContentHash = new string((char)('a' + index), 64)
            }).ToArray()
        });
        var optimizer = new StubReplayOptimizerClient
        {
            TransformJson = json => mutation switch
            {
                "empty-traces" => json.Replace(
                    "\"step_traces\": [",
                    "\"step_traces\": [], \"discarded_step_traces\": [",
                    StringComparison.Ordinal),
                "current-row-visible" => json.Replace(
                    "\"visible_observation_indices_before\":[],\"revealed_history_index\":0",
                    "\"visible_observation_indices_before\":[0],\"revealed_history_index\":0",
                    StringComparison.Ordinal),
                "selected-trace-mismatch" => json.Replace(
                    "\"selected_history_indices\": [[0,1,2,3],[0,1,2,3]]",
                    "\"selected_history_indices\": [[0,1,2,4],[0,1,2,3]]",
                    StringComparison.Ordinal),
                _ => json
            }
        };

        var report = await new ResearchHistoricalReplayService(store, optimizer).RunAsync(
            project.ProjectId,
            new ResearchHistoricalReplayRequest
            { SeedCount = 2, Budget = 5, InitialObservationCount = 3 },
            "engineer-a");

        Assert.False(report.GatePassed);
        Assert.Contains(report.GateFailures, value =>
            value.Contains("未来信息泄漏", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ControlledOnline_FailsClosedWithoutReviewedReplayAndShadowCalibration()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "controlled-gate-blocked" }, "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var shadow = new ResearchShadowRecommendationService(
            store, new EmptyObservationAssembler());
        var gate = new ResearchOnlineAdmissionService(
            store, shadow, new ResearchOnlineCampaignService(store));

        var evidence = await gate.AssessAsync(project.ProjectId);

        Assert.False(evidence.Eligible);
        Assert.Contains(evidence.Failures, value => value.Contains("历史回放", StringComparison.Ordinal));
        Assert.Contains(evidence.Failures, value => value.Contains("回退演练", StringComparison.Ordinal));
        Assert.Contains(evidence.Failures, value => value.Contains("有效影子结果", StringComparison.Ordinal));
        await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => gate.RequireAsync(project.ProjectId));
    }

    [Fact]
    public async Task RollbackDrill_FreezesEvidenceAndRequiresIndependentReview()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "rollback-drill-proof" }, "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        var service = new ResearchRollbackDrillService(store);

        var drill = await service.RecordAsync(
            project.ProjectId,
            new ResearchRollbackDrillRequest
            {
                Name = "优化器失效与安全回退",
                Scenario = "模拟优化器不可用并触发安全上限",
                StopTrigger = "安全上限触发或优化器无响应",
                RollbackTarget = "恢复上一组已确认安全参数",
                ExpectedActions = ["停止建议", "恢复安全参数", "保存日志"],
                ObservedActions = ["停止建议", "恢复安全参数", "保存日志"],
                Passed = true,
                EvidenceReference = "operation-run:drill-001",
                EvidenceContentHash = new string('a', 64),
                ConductedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            "engineer-a");

        Assert.Equal(64, drill.RecordHash.Length);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => service.ReviewAsync(drill.DrillId, "engineer-a"));
        var reviewed = await service.ReviewAsync(drill.DrillId, "engineer-b");
        Assert.Equal(ResearchRollbackDrillStatuses.Reviewed, reviewed.Status);
        Assert.Equal(reviewed.ReviewedAt,
            (await service.ReviewAsync(drill.DrillId, "engineer-c")).ReviewedAt);
    }

    [Fact]
    public async Task ControlledOnline_RequiresOneEngineerDecision_AndPreservesSuggestedAndApprovedValues()
    {
        var store = new MemoryStore();
        var bootstrapWorkflow = CreateWorkflow(store);
        var project = await bootstrapWorkflow.CreateProjectAsync(
            ProjectDraft() with { Code = "controlled-online-proof" }, "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await bootstrapWorkflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        await SeedOnlineAdmissionEvidenceAsync(store, project);
        await store.SaveExperimentResultAsync(new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = Guid.CreateVersion7(),
            DatasetSnapshotId = "controlled-history",
            SafetyPassed = true,
            CalculatedFromSource = true,
            RunObservations = Enumerable.Range(0, 5).Select(index =>
                new ExperimentRunObservation
                {
                    ExecutionKey = $"controlled-history-{index}",
                    ActualFactors = Run("unused", 1, 500 + index * 10, 8 + index).Factors,
                    ProcessFeatures = new Dictionary<string, double>
                    { ["temperature.average"] = 500 + index * 10 },
                    Outcomes = new Dictionary<string, double>
                    { ["form-error"] = 0.55 - index * 0.05 },
                    SourceContentHash = new string((char)('f' + index), 64)
                }).ToArray()
        });
        var shadow = new ResearchShadowRecommendationService(
            store, new EmptyObservationAssembler());
        var gate = new ResearchOnlineAdmissionService(
            store, shadow, new ResearchOnlineCampaignService(store));
        var workflow = CreateWorkflow(store, onlineAdmission: gate);
        var optimizer = new ResearchExperimentOptimizer(
            store,
            new ControlledOptimizerClient(),
            new EmptyObservationAssembler(),
            new ResearchExperimentResultMaterializer(workflow),
            workflow.ExperimentCommands,
            workflow,
            gate);

        var experiment = await optimizer.CreateNextExperimentAsync(
            project.ProjectId,
            new ResearchOptimizationRequest
            {
                Mode = ResearchOptimizationModes.Controlled,
                BatchSize = 1,
                ReplicatesPerCondition = 1,
                Seed = 19
            },
            "engineer-a");

        Assert.Single(experiment.RunPlan);
        Assert.True(experiment.Optimization!.OnlineAdmission!.Eligible);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ExperimentCommands.ChangeExperimentStatusAsync(
                experiment.ExperimentId, ResearchExperimentStatuses.Approved, "engineer-b"));

        var approved = Run("approved", 1, 522, 11).Factors;
        experiment = await workflow.ExperimentCommands.DecideControlledExperimentAsync(
            experiment.ExperimentId,
            new ResearchControlledDecisionRequest
            {
                Decision = ResearchControlledDecisionStatuses.Modified,
                ApprovedFactors = approved,
                Reason = "现场夹具热负荷限制"
            },
            "engineer-b");

        Assert.Equal(520, experiment.ControlledDecision!.SuggestedFactors
            .Single(value => value.VariableCode == "holding-temperature").Value);
        Assert.Equal(522, experiment.ControlledDecision.ApprovedFactors
            .Single(value => value.VariableCode == "holding-temperature").Value);
        Assert.Equal(64, experiment.ControlledDecision.DecisionSnapshotHash.Length);
        Assert.Equal(522, experiment.Execution!.Commands[0].RequestedFactors
            .Single(value => value.VariableCode == "holding-temperature").Value);
        experiment = await workflow.ExperimentCommands.ChangeExperimentStatusAsync(
            experiment.ExperimentId, ResearchExperimentStatuses.Approved, "engineer-b");
        Assert.Equal(ResearchExperimentExecutionStates.Ready, experiment.Execution!.State);
    }

    [Fact]
    public async Task OnlineCampaign_StopsNextSuggestionOnSystematicShadowShift()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "online-shift-stop" }, "engineer-a");
        await FreezeAndReviewStageZeroAsync(store, project);
        await workflow.ChangeProjectStatusAsync(
            project.ProjectId, ResearchProjectStatuses.Active, "engineer-a");
        await SeedOnlineAdmissionEvidenceAsync(store, project);
        for (var index = 0; index < 5; index++)
        {
            var executionKey = $"online-shift-{index}";
            var experiment = new ResearchExperiment
            {
                ExperimentId = Guid.CreateVersion7(),
                ProjectId = project.ProjectId,
                Name = $"Online {index}",
                Status = ResearchExperimentStatuses.Completed,
                StopRule = "stop",
                RollbackPlan = "rollback",
                RunPlan = [Run(executionKey, 1, 510 + index, 10)],
                Optimization = new ResearchOptimizationMetadata
                {
                    ModelVersion = "online-shift-test",
                    InputHash = new string((char)('a' + index), 64),
                    Mode = ResearchOptimizationModes.Controlled,
                    RunPredictions = [Prediction(executionKey, 0.3)]
                },
                ControlledDecision = new ResearchControlledDecision
                {
                    Decision = ResearchControlledDecisionStatuses.Accepted,
                    SuggestedFactors = Run(executionKey, 1, 510 + index, 10).Factors,
                    ApprovedFactors = Run(executionKey, 1, 510 + index, 10).Factors,
                    DecisionSnapshotHash = new string('f', 64),
                    DecidedBy = "engineer-b",
                    DecidedAt = DateTimeOffset.UtcNow
                },
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await store.SaveExperimentAsync(experiment);
            await store.SaveExperimentResultAsync(new ResearchExperimentResult
            {
                ResultId = Guid.CreateVersion7(),
                ProjectId = project.ProjectId,
                ExperimentId = experiment.ExperimentId,
                DatasetSnapshotId = $"online-shift-{index}",
                SafetyPassed = true,
                CalculatedFromSource = true,
                RunObservations =
                [
                    new ExperimentRunObservation
                    {
                        ExecutionKey = executionKey,
                        ActualFactors = experiment.RunPlan[0].Factors,
                        Outcomes = new Dictionary<string, double> { ["form-error"] = 1.3 },
                        ValidForOptimization = true,
                        SourceContentHash = new string((char)('0' + index), 64)
                    }
                ]
            });
        }
        var online = new ResearchOnlineCampaignService(store);
        var report = await online.BuildReportAsync(project.ProjectId);

        Assert.True(report.StopRecommended);
        Assert.True(Assert.Single(report.ShadowComparisons).SystematicShiftDetected);
        Assert.Contains(report.StopSignals, value =>
            value.Code == "shadow-online-systematic-shift" && value.Severity == "stop");
        var gate = new ResearchOnlineAdmissionService(
            store,
            new ResearchShadowRecommendationService(store, new EmptyObservationAssembler()),
            online);
        var admission = await gate.AssessAsync(project.ProjectId);
        Assert.False(admission.Eligible);
        Assert.Contains(admission.Failures, value => value.Contains("在线监控", StringComparison.Ordinal));
    }

    private static async Task SeedOnlineAdmissionEvidenceAsync(
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
            Status = ResearchHistoricalReplayStatuses.Reviewed,
            MechanismKnowledgeSnapshotHash = mechanismHash,
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

    private static Task<ResearchExperiment> CreateOptimizationExperimentAsync(
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

    private static OptimizationRunPrediction Prediction(string executionKey, double mean)
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

    private static async Task CompleteIndependentValidationAsync(
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
            new ExperimentRunObservation
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
            "system-research-automation");
    }

    private static async Task SeedControlledOnlineValidationAsync(
        MemoryStore store,
        ResearchOperatingRegion window)
    {
        var experimentId = Guid.CreateVersion7();
        var resultId = Guid.CreateVersion7();
        var factors = window.Variables.Select(variable => new ExperimentFactorSetting
        {
            VariableCode = variable.VariableCode,
            Value = variable.LowerBound,
            Unit = variable.Unit
        }).ToArray();
        var observations = Enumerable.Range(1, 3).Select(index => new ExperimentRunObservation
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
            RecordedBy = "system-research-automation",
            RecordedAt = DateTimeOffset.UtcNow
        });
    }

    private static ProcessResearchWorkflow CreateWorkflow(
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

    private static ExperimentRunPlan Run(
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

    private static OptimizerSuggestionOutput Suggestion(
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

    private static ResearchProject ProjectDraft()
        => new()
        {
            Code = "optical-molding-window",
            Name = "光学模压工艺操作域研发",
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

    private static async Task FreezeAndReviewStageZeroAsync(
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

    private static ResearchValidationPreregistrationRequest ValidPreregistrationRequest()
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

    private sealed class StubOptimizerClient : IProcessOptimizerClient
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

    private sealed class EmptyObservationAssembler : IResearchObservationAssembler
    {
        public Task<ResearchObservationAssembly> AssembleAsync(
            ResearchProject project,
            IReadOnlyList<ResearchExperiment> experiments,
            CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly([], 0));
    }

    private sealed class ControlledOptimizerClient : IProcessOptimizerClient
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

    private sealed class StubReplayOptimizerClient : IProcessOptimizerClient
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
                      {"step":4,"kind":"optimizer-selection","visible_observation_indices_before":[0,1,2],"candidate_history_indices":[3,4],"revealed_history_index":3}
                    ],
                    [
                      {"step":1,"kind":"preregistered-initial-observation","visible_observation_indices_before":[],"revealed_history_index":0},
                      {"step":2,"kind":"preregistered-initial-observation","visible_observation_indices_before":[0],"revealed_history_index":1},
                      {"step":3,"kind":"preregistered-initial-observation","visible_observation_indices_before":[0,1],"revealed_history_index":2},
                      {"step":4,"kind":"optimizer-selection","visible_observation_indices_before":[0,1,2],"candidate_history_indices":[3,4],"revealed_history_index":3}
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

    private sealed class ReplayMechanismKnowledgeStore(MechanismClaimVersion claim)
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
        public Task<IReadOnlyList<MechanismClaimUsage>> ListUsagesAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> LifecycleEvidenceUsedAsync(Guid claimId, string referenceId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> LifecycleActorUsedAsync(Guid claimId, string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MechanismClaimVersion> TransitionAsync(MechanismClaimLifecycleDecision decision, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExperimentResultValidatesClaimAsync(Guid projectId, MechanismClaimVersion value, Guid validationHypothesisId, MechanismClaimEvidence evidence, string evaluationOutcome = "supports", CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class StubShadowObservationAssembler(ExperimentRunObservation? observation)
        : IResearchObservationAssembler
    {
        public string? RequestedExecutionKey { get; private set; }

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

    private sealed class ScenarioOnlyConfigurationStore(ScenarioPackage scenario) : IProcessConfigurationStore
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

    private sealed class MemoryStore : IProcessResearchStore
    {
        private readonly Dictionary<Guid, ResearchProject> _projects = [];
        private readonly Dictionary<Guid, ResearchValidationPreregistration> _preregistrations = [];
        private readonly Dictionary<Guid, ResearchHypothesis> _hypotheses = [];
        private readonly Dictionary<Guid, ResearchExperiment> _experiments = [];
        private readonly Dictionary<Guid, ResearchShadowRecommendation> _shadowRecommendations = [];
        private readonly Dictionary<Guid, ResearchHistoricalReplayReport> _replayReports = [];
        private readonly Dictionary<Guid, ResearchRollbackDrill> _rollbackDrills = [];
        private readonly Dictionary<Guid, ResearchExperimentResult> _results = [];
        private readonly Dictionary<Guid, ResearchOperatingRegion> _windows = [];
        private readonly Dictionary<Guid, ResearchKnowledgeClaim> _claims = [];
        private readonly Dictionary<Guid, ResearchTransferAssessment> _transferAssessments = [];
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

        public Task<ResearchValidationPreregistration?> GetValidationPreregistrationAsync(
            Guid preregistrationId,
            CancellationToken ct = default)
            => Task.FromResult(_preregistrations.GetValueOrDefault(preregistrationId));

        public Task<IReadOnlyList<ResearchValidationPreregistration>> ListValidationPreregistrationsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchValidationPreregistration>>(
                _preregistrations.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchValidationPreregistration> CreateValidationPreregistrationAsync(
            ResearchValidationPreregistration value,
            CancellationToken ct = default)
        {
            _preregistrations.Add(value.PreregistrationId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchValidationPreregistration> ReviewValidationPreregistrationAsync(
            ResearchValidationPreregistration value,
            CancellationToken ct = default)
        {
            if (!_preregistrations.TryGetValue(value.PreregistrationId, out var current) ||
                current.Status != ResearchValidationPreregistrationStatuses.Frozen)
                throw new ProcessResearchRuleException("阶段 0 预注册不存在或已经复核。");
            _preregistrations[value.PreregistrationId] = value;
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

        public async Task<ResearchPage<ResearchExperiment>> ListExperimentsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListExperimentsAsync(projectId, ct)).Take(limit).ToArray() };

        public Task<ResearchExperiment> SaveExperimentAsync(
            ResearchExperiment value,
            CancellationToken ct = default)
        {
            _experiments[value.ExperimentId] = value;
            return Task.FromResult(value);
        }

        public async Task<ResearchExperiment> SaveExperimentTransactionAsync(
            ResearchExperiment updatedExperiment,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
        {
            var saved = await SaveExperimentAsync(updatedExperiment, ct);
            await AddAuditEntryAsync(audit, ct);
            return saved;
        }

        public Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
            ResearchExperiment updatedExperiment,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
            => SaveExperimentTransactionAsync(updatedExperiment, audit, ct);

        public Task<ResearchShadowRecommendation?> GetShadowRecommendationAsync(
            Guid recommendationId,
            CancellationToken ct = default)
            => Task.FromResult(_shadowRecommendations.GetValueOrDefault(recommendationId));

        public Task<ResearchShadowRecommendation?> GetShadowRecommendationBySuggestionAsync(
            Guid experimentId,
            string suggestionExecutionKey,
            CancellationToken ct = default)
            => Task.FromResult(_shadowRecommendations.Values.SingleOrDefault(value =>
                value.ExperimentId == experimentId &&
                value.SuggestionExecutionKey == suggestionExecutionKey));

        public Task<IReadOnlyList<ResearchShadowRecommendation>> ListShadowRecommendationsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchShadowRecommendation>>(
                _shadowRecommendations.Values.Where(value => value.ProjectId == projectId).ToArray());

        public async Task<ResearchPage<ResearchShadowRecommendation>> ListShadowRecommendationsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListShadowRecommendationsAsync(projectId, ct)).Take(limit).ToArray() };

        public Task<ResearchShadowRecommendation> CreateShadowRecommendationAsync(
            ResearchShadowRecommendation value,
            CancellationToken ct = default)
        {
            _shadowRecommendations.Add(value.RecommendationId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchShadowRecommendation> AttachShadowOutcomeAsync(
            ResearchShadowRecommendation value,
            CancellationToken ct = default)
        {
            if (!_shadowRecommendations.TryGetValue(value.RecommendationId, out var current) ||
                current.Outcome is not null)
                throw new ProcessResearchRuleException("影子建议不存在，或结果已经冻结。");
            _shadowRecommendations[value.RecommendationId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchHistoricalReplayReport?> GetHistoricalReplayReportAsync(
            Guid reportId,
            CancellationToken ct = default)
            => Task.FromResult(_replayReports.GetValueOrDefault(reportId));

        public Task<IReadOnlyList<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchHistoricalReplayReport>>(
                _replayReports.Values.Where(value => value.ProjectId == projectId).ToArray());

        public async Task<ResearchPage<ResearchHistoricalReplayReport>> ListHistoricalReplayReportsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListHistoricalReplayReportsAsync(projectId, ct)).Take(limit).ToArray() };

        public Task<ResearchHistoricalReplayReport> CreateHistoricalReplayReportAsync(
            ResearchHistoricalReplayReport value,
            CancellationToken ct = default)
        {
            var existing = _replayReports.Values.FirstOrDefault(item =>
                item.ProjectId == value.ProjectId &&
                item.DatasetSnapshotHash == value.DatasetSnapshotHash &&
                item.ReportHash == value.ReportHash);
            if (existing is not null)
                return Task.FromResult(existing);
            _replayReports.Add(value.ReportId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchHistoricalReplayReport> ReviewHistoricalReplayReportAsync(
            ResearchHistoricalReplayReport value,
            CancellationToken ct = default)
        {
            if (!_replayReports.TryGetValue(value.ReportId, out var current) ||
                current.Status != ResearchHistoricalReplayStatuses.Generated)
                throw new ProcessResearchRuleException("历史回放报告不存在或已经审核。");
            _replayReports[value.ReportId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchRollbackDrill?> GetRollbackDrillAsync(
            Guid drillId,
            CancellationToken ct = default)
            => Task.FromResult(_rollbackDrills.GetValueOrDefault(drillId));

        public Task<IReadOnlyList<ResearchRollbackDrill>> ListRollbackDrillsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchRollbackDrill>>(
                _rollbackDrills.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchRollbackDrill> CreateRollbackDrillAsync(
            ResearchRollbackDrill value,
            CancellationToken ct = default)
        {
            _rollbackDrills.Add(value.DrillId, value);
            return Task.FromResult(value);
        }

        public Task<ResearchRollbackDrill> ReviewRollbackDrillAsync(
            ResearchRollbackDrill value,
            CancellationToken ct = default)
        {
            if (!_rollbackDrills.TryGetValue(value.DrillId, out var current) ||
                current.Status != ResearchRollbackDrillStatuses.Recorded)
                throw new ProcessResearchRuleException("回退演练不存在或已经复核。");
            _rollbackDrills[value.DrillId] = value;
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

        public async Task<ResearchPage<ResearchExperimentResult>> ListExperimentResultsPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListExperimentResultsAsync(projectId, ct)).Take(limit).ToArray() };

        public Task<ResearchExperimentResult> SaveExperimentResultAsync(
            ResearchExperimentResult value,
            CancellationToken ct = default)
        {
            _results[value.ResultId] = value;
            return Task.FromResult(value);
        }

        public async Task<ResearchExperimentResult> SaveExperimentResultTransactionAsync(
            ResearchExperimentResult result,
            ResearchExperiment updatedExperiment,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
        {
            var saved = await SaveExperimentResultAsync(result, ct);
            await SaveExperimentAsync(updatedExperiment, ct);
            await AddAuditEntryAsync(audit, ct);
            return saved;
        }

        public Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
            Guid operatingRegionId,
            CancellationToken ct = default)
            => Task.FromResult(_windows.GetValueOrDefault(operatingRegionId));

        public Task<IReadOnlyList<ResearchOperatingRegion>> ListOperatingRegionsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchOperatingRegion>>(
                _windows.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchOperatingRegion> SaveOperatingRegionAsync(
            ResearchOperatingRegion value,
            CancellationToken ct = default)
        {
            _windows[value.OperatingRegionId] = value;
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

        public Task<ResearchTransferAssessment?> GetTransferAssessmentAsync(
            Guid assessmentId,
            CancellationToken ct = default)
            => Task.FromResult(_transferAssessments.GetValueOrDefault(assessmentId));

        public Task<IReadOnlyList<ResearchTransferAssessment>> ListTransferAssessmentsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchTransferAssessment>>(
                _transferAssessments.Values.Where(value => value.ProjectId == projectId).ToArray());

        public Task<ResearchTransferAssessment> CreateTransferAssessmentAsync(
            ResearchTransferAssessment value,
            CancellationToken ct = default)
        {
            var existing = _transferAssessments.Values.FirstOrDefault(item =>
                item.ProjectId == value.ProjectId && item.SourceOperatingRegionId == value.SourceOperatingRegionId &&
                item.RecordHash == value.RecordHash);
            if (existing is not null)
                return Task.FromResult(existing);
            _transferAssessments[value.AssessmentId] = value;
            return Task.FromResult(value);
        }

        public Task<ResearchTransferAssessment> ReviewTransferAssessmentAsync(
            ResearchTransferAssessment value,
            CancellationToken ct = default)
        {
            if (!_transferAssessments.TryGetValue(value.AssessmentId, out var current) ||
                current.Status != ResearchTransferAssessmentStatuses.Recorded)
                throw new ProcessResearchRuleException("迁移评估不存在或已经复核。");
            _transferAssessments[value.AssessmentId] = value;
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

        public async Task<ResearchPage<ResearchAuditEntry>> ListAuditEntriesPageAsync(
            Guid projectId,
            string? cursor,
            int limit,
            CancellationToken ct = default)
            => new() { Items = (await ListAuditEntriesAsync(projectId, ct)).Take(limit).ToArray() };
    }

    private sealed class StubReliabilityBaselineService : IDataReliabilityBaselineService
    {
        public Task<DataReliabilityBaseline> CalculateAsync(
            DataReliabilityBaselineQuery query,
            CancellationToken ct = default)
            => Task.FromResult(new DataReliabilityBaseline
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                From = query.From,
                To = query.To,
                EdgeId = query.EdgeId,
                EquipmentId = query.EquipmentId,
                MatchingCompletedRunCount = 12,
                AnalyzedRunCount = 12,
                Rates =
                [
                    new ReliabilityRate
                    {
                        Code = "analysis_admission",
                        Name = "正式分析准入率",
                        Numerator = 9,
                        Denominator = 12,
                        Rate = 0.75,
                        Definition = "测试快照"
                    }
                ]
            });
    }

    private sealed class RejectingExecutionComparisonService : IExecutionComparisonService
    {
        public int CallCount { get; private set; }

        public Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
            string executionId,
            CancellationToken ct = default,
            string? siteId = null)
        {
            CallCount++;
            return Task.FromResult<ExecutionComparisonRow?>(null);
        }

        public Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyDictionary<string, ExecutionComparisonRow>>(
                new Dictionary<string, ExecutionComparisonRow>());
        }

        public Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
            string executionId,
            int limit,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
        {
            CallCount++;
            return Task.FromResult<ExecutionComparisonResult?>(null);
        }

        public Task<ExecutionComparisonResult?> CompareSelectedAsync(
            string baselineProcessExecutionId,
            IReadOnlyList<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
        {
            CallCount++;
            return Task.FromResult<ExecutionComparisonResult?>(null);
        }
    }

    private sealed class FixedExecutionComparisonService(ExecutionComparisonResult result)
        : IExecutionComparisonService
    {
        public Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
            string executionId,
            CancellationToken ct = default,
            string? siteId = null)
            => Task.FromResult<ExecutionComparisonRow?>(
                executionId == result.Baseline.ExecutionId ? result.Baseline : null);

        public Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null)
            => Task.FromResult<IReadOnlyDictionary<string, ExecutionComparisonRow>>(
                new[] { result.Baseline }.Concat(result.HistoricalProcessExecutions)
                    .Where(row => executionIds.Contains(row.ExecutionId))
                    .ToDictionary(row => row.ExecutionId, StringComparer.Ordinal));

        public Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
            string executionId,
            int limit,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
            => Task.FromResult<ExecutionComparisonResult?>(result);

        public Task<ExecutionComparisonResult?> CompareSelectedAsync(
            string baselineProcessExecutionId,
            IReadOnlyList<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
            => Task.FromResult<ExecutionComparisonResult?>(result);
    }

    private static ExecutionComparisonResult Comparison(
        string readinessMode,
        double crossValidationScore,
        string candidateEvidenceLevel)
    {
        var row = new ExecutionComparisonRow
        {
            ExecutionId = "run-a",
            EquipmentId = "press-01",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow,
            ProductFamilyCode = "lens-a"
        };
        return new ExecutionComparisonResult
        {
            BaselineProcessExecutionId = row.ExecutionId,
            ProductFamilyCode = row.ProductFamilyCode,
            Baseline = row,
            HistoricalProcessExecutions = [row with { ExecutionId = "run-b" }],
            Acceptance = new ExecutionComparisonAcceptance { ProcessExecutionCount = 2 },
            Diagnosis = new ExecutionDiagnosisSummary
            {
                EvidenceLevel = candidateEvidenceLevel,
                CrossValidationScore = crossValidationScore,
                Readiness = new ExecutionAnalysisReadiness { Mode = readinessMode },
                Candidates =
                [
                    new ExecutionCauseCandidate
                    {
                        CandidateId = "control-parameter:holding-temperature",
                        SourceKind = ExecutionCauseSourceKinds.ProcessSpecificationParameter,
                        Actionability = ExecutionCauseActionability.Controllable,
                        VariableCode = "holding-temperature",
                        DataSource = "control-parameter:holding-temperature",
                        DisplayName = "保压温度",
                        MedianDifference = 2.5,
                        EvidenceLevel = candidateEvidenceLevel,
                        CandidateScore = 0.8
                    }
                ]
            }
        };
    }
}
