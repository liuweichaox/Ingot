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
        var workspace = await workflow.GetWorkspaceAsync(project.ProjectId);
        Assert.Single(workspace.Hypotheses);
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
