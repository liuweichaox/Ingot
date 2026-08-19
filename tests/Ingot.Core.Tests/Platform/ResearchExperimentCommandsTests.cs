using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchExperimentCommandsTests
{
    [Fact]
    public async Task CreateExperiment_RejectsSingleConditionWithoutInfrastructure()
    {
        var project = Project();
        var store = new MemoryCommandStore(project);
        var commands = new ResearchExperimentCommands(store);

        var error = await Assert.ThrowsAsync<ResearchExperimentCommandException>(() =>
            commands.CreateExperimentAsync(
                project.ProjectId,
                Experiment([Run("run-1", 500)]),
                "engineer-a"));

        Assert.Contains("至少包含两个运行条件", error.Message, StringComparison.Ordinal);
        Assert.Empty(store.Experiments);
        Assert.Empty(store.AuditEntries);
    }

    [Fact]
    public async Task CreateExperiment_PersistsExperimentAndAuditThroughOnePortCall()
    {
        var project = Project();
        var store = new MemoryCommandStore(project);
        var commands = new ResearchExperimentCommands(store);

        var saved = await commands.CreateExperimentAsync(
            project.ProjectId,
            Experiment([Run("run-1", 500), Run("run-2", 520)]),
            "engineer-a");

        Assert.Equal(ResearchExperimentStatuses.Planned, saved.Status);
        Assert.Equal(project.Revision, saved.ProjectRevision);
        Assert.Equal("engineer-a", saved.CreatedBy);
        Assert.Single(store.Experiments);
        var audit = Assert.Single(store.AuditEntries);
        Assert.Equal(saved.ExperimentId.ToString(), audit.ResourceId);
        Assert.Equal("planned", audit.Action);
    }

    private static ResearchProject Project()
        => new()
        {
            ProjectId = Guid.CreateVersion7(),
            Code = "application-slice",
            Name = "应用层实验命令测试",
            ProcessName = "通用工艺",
            Status = ResearchProjectStatuses.Active,
            Revision = 3,
            Objectives =
            [
                new ResearchObjective
                {
                    Code = "quality",
                    Name = "质量指标",
                    Unit = "score",
                    Direction = "maximize",
                    Target = 1
                }
            ],
            Variables =
            [
                new ResearchVariable
                {
                    Code = "temperature",
                    Name = "温度",
                    Role = ResearchVariableRoles.Control,
                    Unit = "Cel",
                    LowerLimit = 480,
                    UpperLimit = 550
                }
            ]
        };

    private static ResearchExperiment Experiment(IReadOnlyList<ExperimentRunPlan> runs)
        => new()
        {
            Name = "温度验证实验",
            DesignMethod = ResearchDesignMethods.FullFactorial,
            RunPlan = runs,
            ObjectiveCodes = ["quality"],
            StopRule = "质量或安全边界触发时停止。",
            RollbackPlan = "恢复到已验证的基线设置。"
        };

    private static ExperimentRunPlan Run(string key, double temperature)
        => new()
        {
            ExecutionKey = key,
            Factors =
            [
                new ExperimentFactorSetting
                {
                    VariableCode = "temperature",
                    Value = temperature,
                    Unit = "Cel"
                }
            ]
        };

    private sealed class MemoryCommandStore(ResearchProject project) : IResearchExperimentCommandStore
    {
        public List<ResearchExperiment> Experiments { get; } = [];
        public List<ResearchAuditEntry> AuditEntries { get; } = [];

        public Task<ResearchProject?> GetProjectAsync(Guid projectId, CancellationToken ct = default)
            => Task.FromResult<ResearchProject?>(projectId == project.ProjectId ? project : null);

        public Task<ResearchHypothesis?> GetHypothesisAsync(
            Guid hypothesisId,
            CancellationToken ct = default)
            => Task.FromResult<ResearchHypothesis?>(null);

        public Task<ResearchExperiment?> GetExperimentAsync(
            Guid experimentId,
            CancellationToken ct = default)
            => Task.FromResult(Experiments.SingleOrDefault(value => value.ExperimentId == experimentId));

        public Task<IReadOnlyList<ResearchExperiment>> ListExperimentsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchExperiment>>(
                Experiments.Where(value => value.ProjectId == projectId).ToArray());

        public Task<IReadOnlyList<ResearchExperimentResult>> ListExperimentResultsAsync(
            Guid projectId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ResearchExperimentResult>>([]);

        public Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
            Guid operatingRegionId,
            CancellationToken ct = default)
            => Task.FromResult<ResearchOperatingRegion?>(null);

        public Task<ResearchExperiment> SaveExperimentTransactionAsync(
            ResearchExperiment updatedExperiment,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
        {
            Experiments.Add(updatedExperiment);
            AuditEntries.Add(audit);
            return Task.FromResult(updatedExperiment);
        }

        public Task<ResearchExperiment> SaveControlledDecisionTransactionAsync(
            ResearchExperiment updatedExperiment,
            ResearchAuditEntry audit,
            CancellationToken ct = default)
            => SaveExperimentTransactionAsync(updatedExperiment, audit, ct);
    }
}
