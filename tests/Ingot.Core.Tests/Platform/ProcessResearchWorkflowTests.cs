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
                DesignMethod = "factorial",
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
                Evidence =
                [
                    new EvidenceReference
                    {
                        Kind = "experiment-result",
                        ReferenceId = experiment.ExperimentId.ToString(),
                        Summary = "重复实验满足面形误差目标和全部安全约束。"
                    }
                ],
                Confidence = 0.9,
                Applicability = "材料批次 A，设备 PRESS-01。"
            },
            "engineer-a");
        window = await workflow.ValidateProcessWindowAsync(window.WindowId, "engineer-b");

        project = await workflow.ChangeProjectStatusAsync(
            project.ProjectId,
            ResearchProjectStatuses.Validating,
            "engineer-a");
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
        private readonly Dictionary<Guid, ResearchProcessWindow> _windows = [];
        private readonly Dictionary<Guid, ResearchKnowledgeClaim> _claims = [];

        public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult(_projects.GetValueOrDefault(projectId));

        public Task<ResearchProject?> GetProjectByCodeAsync(
            string code,
            CancellationToken ct = default)
            => Task.FromResult(_projects.Values.SingleOrDefault(
                value => string.Equals(value.Code, code, StringComparison.Ordinal)));

        public Task<IReadOnlyList<ResearchProject>> ListProjectsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchProject>>(_projects.Values.ToArray());

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
    }
}
