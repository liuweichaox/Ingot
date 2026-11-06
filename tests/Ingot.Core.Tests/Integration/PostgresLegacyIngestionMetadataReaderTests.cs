// Verifies archived ingestion metadata remains available only as site-scoped historical evidence.
using Ingot.Platform.Application.Acquisition;
using Ingot.Platform.Infrastructure.Acquisition;
using Npgsql;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresLegacyIngestionMetadataReaderTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task FindVersionMetadataAsync_ShouldScopeAndRedactArchivedTasks()
    {
        await postgres.EnsureSchemaAsync();
        var reader = new PostgresLegacyIngestionMetadataReader(postgres.DataSource);
        var suffix = Guid.NewGuid().ToString("N");
        var authorizedTask = $"legacy-authorized-{suffix}";
        var outsideTask = $"legacy-outside-{suffix}";
        await InsertLegacyTaskAsync(authorizedTask, "EDGE-SITE-A", "published");
        await InsertLegacyTaskAsync(outsideTask, "EDGE-SITE-B", "published");

        var result = await reader.FindVersionMetadataAsync(
        [
            new(authorizedTask, 1),
            new(outsideTask, 1),
            new($"legacy-missing-{suffix}", 1)
        ],
        ["EDGE-SITE-A"]);

        var authorized = Assert.Single(result, item => item.TaskId == authorizedTask);
        Assert.Equal("EDGE-SITE-A", authorized.AuthorizedEdgeId);
        Assert.Equal("published", authorized.AuthorizedStatus);
        Assert.False(authorized.ExistsOutsideAuthorizedSite);

        var outside = Assert.Single(result, item => item.TaskId == outsideTask);
        Assert.Null(outside.AuthorizedEdgeId);
        Assert.Null(outside.AuthorizedStatus);
        Assert.True(outside.ExistsOutsideAuthorizedSite);

        var missing = Assert.Single(result, item => item.TaskId == $"legacy-missing-{suffix}");
        Assert.Null(missing.AuthorizedEdgeId);
        Assert.False(missing.ExistsOutsideAuthorizedSite);
    }

    [LinuxDockerFact]
    public async Task FindCoverageMetadataAsync_ShouldReadOnlyRequiredMappingsFromArchive()
    {
        await postgres.EnsureSchemaAsync();
        var reader = new PostgresLegacyIngestionMetadataReader(postgres.DataSource);
        var suffix = Guid.NewGuid().ToString("N");
        var taskId = $"legacy-coverage-{suffix}";
        await InsertLegacyTaskAsync(taskId, "EDGE-SITE-A", "published");

        var result = await reader.FindCoverageMetadataAsync(
            [new IngestionTaskVersionReference(taskId, 1)],
            ["EDGE-SITE-A"]);

        var item = Assert.Single(result);
        Assert.Equal("EDGE-SITE-A", item.AuthorizedEdgeId);
        Assert.Equal("equipment", item.SubjectType);
        Assert.Equal("PRESS-01", item.SubjectId);
        Assert.Equal(["pressure", "temperature"], item.RequiredDataItemCodes);
        Assert.False(item.ExistsOutsideAuthorizedSite);
        Assert.False(item.RequiredDataItemCodesTruncated);
    }

    private async Task InsertLegacyTaskAsync(string taskId, string edgeId, string status)
    {
        await using var command = postgres.DataSource.CreateCommand(
            """
            INSERT INTO ingot_legacy.ingestion_tasks(task_id, version, edge_id, status, payload, updated_at)
            VALUES (@task_id, 1, @edge_id, @status, @payload::jsonb, now());
            """);
        command.Parameters.AddWithValue("task_id", taskId);
        command.Parameters.AddWithValue("edge_id", edgeId);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("payload", """
            {"subjectType":"equipment","subjectId":"PRESS-01","valueMappings":[
              {"dataItemCode":"temperature","required":true},
              {"dataItemCode":"pressure","required":true}]}
            """);
        await command.ExecuteNonQueryAsync();
    }
}
