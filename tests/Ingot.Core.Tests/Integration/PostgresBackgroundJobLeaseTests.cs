using System.Text;
using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresBackgroundJobLeaseTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task KnowledgeExtractionLease_ShouldExcludeRetryAndDeadLetter()
    {
        await postgres.EnsureSchemaAsync();
        var root = Path.Combine(Path.GetTempPath(), $"ingot-job-lease-{Guid.NewGuid():N}");
        try
        {
            var projectId = await InsertProjectAsync();
            var store = new PostgresResearchAssetStore(
                postgres.DataSource,
                Options.Create(new ProcessKnowledgeOptions { RootPath = root }));
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("pressure causes temperature change"));
            var source = await store.AddKnowledgeSourceAsync(
                content,
                "lease test",
                "document",
                "lease-test.txt",
                "text/plain",
                new Dictionary<string, string> { ["research-project-id"] = projectId.ToString() },
                "tester");

            var first = await store.ClaimKnowledgeExtractionAsync(TimeSpan.FromMinutes(5));
            Assert.NotNull(first);
            Assert.Null(await store.ClaimKnowledgeExtractionAsync(TimeSpan.FromMinutes(5)));
            Assert.True(await store.RenewKnowledgeExtractionLeaseAsync(source.SourceId, first!.LeaseId));
            Assert.Equal(
                KnowledgeExtractionFailureDisposition.RetryScheduled,
                await store.FailKnowledgeExtractionAsync(
                    source.SourceId, first.LeaseId, "temporary", true, 2, TimeSpan.Zero));

            var second = await store.ClaimKnowledgeExtractionAsync(TimeSpan.FromMinutes(5));
            Assert.NotNull(second);
            Assert.Equal(2, second!.AttemptCount);
            Assert.Equal(
                KnowledgeExtractionFailureDisposition.DeadLettered,
                await store.FailKnowledgeExtractionAsync(
                    source.SourceId, second.LeaseId, "still failing", true, 2, TimeSpan.Zero));
            Assert.Null(await store.ClaimKnowledgeExtractionAsync(TimeSpan.FromMinutes(5)));

            await using var status = postgres.DataSource.CreateCommand(
                "SELECT status FROM knowledge_extraction_jobs WHERE source_id=@id;");
            status.Parameters.AddWithValue("id", source.SourceId);
            Assert.Equal("dead-letter", await status.ExecuteScalarAsync());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [LinuxDockerFact]
    public async Task BackfillAndRecomputeClaims_ShouldBeExclusive()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessExecutionAnalysisMaterializationStore(
            postgres.DataSource,
            NullLogger<PostgresProcessExecutionAnalysisMaterializationStore>.Instance);
        var job = new ProcessExecutionAnalysisBackfillJob
        {
            JobId = Guid.CreateVersion7(),
            Status = "queued",
            CreatedBy = "tester",
            CreatedAt = DateTimeOffset.UtcNow
        };
        await store.AddBackfillJobAsync(job);

        var first = await store.ClaimBackfillJobAsync(TimeSpan.FromMinutes(5));
        Assert.NotNull(first);
        Assert.Null(await store.ClaimBackfillJobAsync(TimeSpan.FromMinutes(5)));
        Assert.True(await store.SaveClaimedBackfillJobAsync(
            job with { Status = "completed", CompletedAt = DateTimeOffset.UtcNow },
            first!.LeaseId,
            true));

        var executionId = $"lease-{Guid.NewGuid():N}";
        await using (var insert = postgres.DataSource.CreateCommand(
                         """
                         INSERT INTO execution_analysis_recompute_jobs(
                           execution_id,invalidated_source_max_ingest_id,reason,status)
                         VALUES(@id,1,'test','queued');
                         """))
        {
            insert.Parameters.AddWithValue("id", executionId);
            await insert.ExecuteNonQueryAsync();
        }
        var recompute = await store.ClaimRecomputeAsync(TimeSpan.FromMinutes(5));
        Assert.NotNull(recompute);
        Assert.Null(await store.ClaimRecomputeAsync(TimeSpan.FromMinutes(5)));
        Assert.True(await store.RetryRecomputeAsync(
            executionId, recompute!.LeaseId, TimeSpan.Zero, "test", 8));
        var retried = await store.ClaimRecomputeAsync(TimeSpan.FromMinutes(5));
        Assert.NotNull(retried);
        Assert.True(await store.CompleteRecomputeAsync(executionId, retried!.LeaseId));
    }

    private async Task<Guid> InsertProjectAsync()
    {
        var projectId = Guid.CreateVersion7();
        await using var command = postgres.DataSource.CreateCommand(
            """
            INSERT INTO process_research_projects(project_id,code,status,revision,payload,created_at,updated_at)
            VALUES(@id,@code,'draft',1,'{}'::jsonb,now(),now());
            """);
        command.Parameters.AddWithValue("id", projectId);
        command.Parameters.AddWithValue("code", $"lease-{projectId:N}");
        await command.ExecuteNonQueryAsync();
        return projectId;
    }
}
