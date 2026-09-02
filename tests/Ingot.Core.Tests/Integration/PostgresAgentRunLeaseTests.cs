using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Infrastructure.AgentRuns;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresAgentRunLeaseTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task FencingLease_RejectsStaleWorkerSnapshotAndEventWrites()
    {
        await postgres.EnsureSchemaAsync();
        var storeA = new PostgresAgentRunStore(postgres.DataSource);
        var storeB = new PostgresAgentRunStore(postgres.DataSource);
        var queued = QueuedRun();
        await storeA.CreateAsync(queued);

        var first = Assert.IsType<ClaimedAgentRun>(
            await storeA.ClaimNextAsync("worker-a", TimeSpan.FromMinutes(5)));
        await using (var expire = postgres.DataSource.CreateCommand(
                         "UPDATE agent_runs SET lease_expires_at = now() - interval '1 second' WHERE run_id = @id;"))
        {
            expire.Parameters.AddWithValue("id", queued.RunId);
            await expire.ExecuteNonQueryAsync();
        }
        var second = Assert.IsType<ClaimedAgentRun>(
            await storeB.ClaimNextAsync("worker-b", TimeSpan.FromMinutes(5)));

        Assert.True(second.Lease.Generation > first.Lease.Generation);
        var staleTerminal = first.Run with
        {
            Status = AgentRunStatuses.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Error = "stale worker must not commit"
        };
        Assert.False(await storeA.UpdateLeasedAsync(staleTerminal, first.Lease));
        Assert.Null(await storeA.AppendLeasedEventAsync(
            queued.RunId, first.Lease, AgentStreamEventTypes.RunCompleted, new { stale = true }));

        var currentTerminal = second.Run with
        {
            Status = AgentRunStatuses.Completed,
            CompletedAt = DateTimeOffset.UtcNow
        };
        Assert.True(await storeB.UpdateLeasedAsync(currentTerminal, second.Lease));
        await storeB.ReleaseLeaseAsync(second.Lease);

        var saved = await storeA.GetAsync(queued.RunId);
        Assert.Equal(AgentRunStatuses.Completed, saved!.Status);
        Assert.Null(saved.Error);
        Assert.Empty(await storeA.ReadEventsAsync(queued.RunId, 0, 20));
    }

    private static AgentRunSnapshot QueuedRun() => new()
    {
        RunId = Guid.CreateVersion7().ToString(),
        ConversationId = Guid.CreateVersion7().ToString(),
        UserId = "lease-test",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Question = "lease fencing test",
        Mode = "quick",
        Status = AgentRunStatuses.Queued,
        ModelProvider = "test",
        Model = "test",
        PromptVersion = "test",
        ToolsetVersion = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        Usage = new AgentUsageSummary(),
        AccessScope = new AgentRunAccessScopeSnapshot { SiteIds = ["SITE-001"] }
    };
}
