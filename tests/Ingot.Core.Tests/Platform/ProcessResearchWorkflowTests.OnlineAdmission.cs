// 覆盖历史回放、上线准入和在线停止策略。
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

public sealed class ProcessResearchWorkflowOnlineAdmissionTests : ProcessResearchWorkflowTestBase
{
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
            new ResearchRunObservation
            {
                ExecutionKey = $"historical-{index + 1}",
                ActualFactors =
                [
                    new ResearchVariableSetting
                    {
                        VariableCode = "holding-temperature",
                        Value = 500 + index * 5,
                        Unit = "Cel"
                    },
                    new ResearchVariableSetting
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
        Assert.Equal(["botorch-qlogbo-test"], report.OptimizerModelVersions);
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
            RunObservations = Enumerable.Range(0, 5).Select(index => new ResearchRunObservation
            {
                ExecutionKey = $"adversarial-{index}",
                ActualFactors =
                [
                    new ResearchVariableSetting
                        { VariableCode = "holding-temperature", Value = 500 + index, Unit = "Cel" },
                    new ResearchVariableSetting
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
                new ResearchRunObservation
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
        var optimizer = new ResearchOptimizationService(
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
        Assert.Contains("botorch-controlled-test",
            experiment.Optimization!.MethodAdmission!.OptimizerModelVersions);
        Assert.True(experiment.Optimization!.OnlineAdmission!.Eligible);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ExperimentCommands.ChangeExperimentStatusAsync(
                experiment.ExperimentId, ResearchExperimentStatuses.Approved, "engineer-b"));

        var unsafeApproved = Run("unsafe-approved", 1, 549, 11).Factors;
        var safetyError = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.ExperimentCommands.DecideControlledExperimentAsync(
                experiment.ExperimentId,
                new ResearchControlledDecisionRequest
                {
                    Decision = ResearchControlledDecisionStatuses.Modified,
                    ApprovedFactors = unsafeApproved,
                    Reason = "尝试越过安全上限"
                },
                "engineer-b"));
        Assert.Contains("temperature-safety", safetyError.Message);

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
                    new ResearchRunObservation
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

}
