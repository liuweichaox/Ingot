// 覆盖实验结果的源数据物化与审计。
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

public sealed class ProcessResearchWorkflowResultTests : ProcessResearchWorkflowTestBase
{
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
        ResearchRunObservation Observation(
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

        ResearchRunObservation Observation(string executionKey, double outcome, char hash)
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
        ResearchRunObservation Observation(string executionKey, double outcome, char hash)
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

}
