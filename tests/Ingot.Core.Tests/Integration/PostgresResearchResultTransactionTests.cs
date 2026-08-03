using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresResearchResultTransactionTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task MissingExperimentUpdate_ShouldRollbackResultAndAudit()
    {
        await postgres.EnsureSchemaAsync();
        await using var store = new PostgresProcessResearchStore(postgres.Configuration);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"transaction-{Guid.NewGuid():N}",
            Name = "Transaction Test",
            ProcessName = "Test Process",
            OwnerUserId = "engineer",
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };
        var experiment = new ResearchExperiment
        {
            ExperimentId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Name = "Experiment",
            StopRule = "stop",
            RollbackPlan = "rollback",
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveProjectAsync(project);
        await store.SaveExperimentAsync(experiment);

        var result = new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = experiment.ExperimentId,
            DatasetSnapshotId = "snapshot-a",
            AnalysisRunId = Guid.CreateVersion7(),
            AnalysisHash = new string('a', 64),
            SafetyPassed = true,
            RecordedBy = "engineer",
            RecordedAt = now
        };
        var nonexistentUpdate = experiment with
        {
            ExperimentId = Guid.CreateVersion7(),
            ResultIds = [result.ResultId],
            UpdatedAt = now.AddSeconds(1)
        };
        var audit = new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "experiment-result",
            ResourceId = result.ResultId.ToString(),
            Action = "recorded",
            UserId = "engineer",
            CreatedAt = now
        };

        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.SaveExperimentResultTransactionAsync(result, nonexistentUpdate, audit));

        Assert.Null(await store.GetExperimentResultAsync(result.ResultId));
        Assert.DoesNotContain(
            await store.ListAuditEntriesAsync(project.ProjectId),
            item => item.EntryId == audit.EntryId);
        Assert.Empty((await store.GetExperimentAsync(experiment.ExperimentId))!.ResultIds);
    }
}
