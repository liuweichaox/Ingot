// 验证 PostgresIngestionTaskStore 的真实基础设施集成、失败和恢复行为。

using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Acquisition;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresIngestionTaskStoreTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ConcurrentPublication_ShouldLeaveExactlyOnePublishedVersion()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresIngestionTaskStore(postgres.DataSource);
        await EnsureDataModelAsync();
        await store.InitializeAsync();
        var taskId = $"concurrent-{Guid.NewGuid():N}";
        var first = Profile(taskId, 1);
        var second = Profile(taskId, 2);

        await Task.WhenAll(
            store.PublishExclusiveAsync(first),
            store.PublishExclusiveAsync(second));

        var all = (await store.ListAsync()).Where(item => item.TaskId == taskId).ToArray();
        var published = Assert.Single(all, item => item.Status == ConfigurationStatuses.Published);
        var retired = Assert.Single(all, item => item.Status == ConfigurationStatuses.Retired);
        Assert.NotEqual(published.Version, retired.Version);
        Assert.Equal(published.Version, (await store.GetAsync(taskId, published.Version))?.Version);
        Assert.Equal(ConfigurationStatuses.Retired, (await store.GetAsync(taskId, retired.Version))?.Status);
    }

    [LinuxDockerFact]
    public async Task PublishedVersionCannotBeOverwrittenOrDeleted()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresIngestionTaskStore(postgres.DataSource);
        await EnsureDataModelAsync();
        var taskId = $"immutable-{Guid.NewGuid():N}";
        var published = Profile(taskId, 1) with { Name = "first" };
        await store.PublishExclusiveAsync(published);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.PublishExclusiveAsync(published with { Name = "replacement" }));
        Assert.False(await store.DeleteAsync(taskId, 1));

        var stored = await store.GetAsync(taskId, 1);
        Assert.Equal("first", stored!.Name);
        Assert.Equal(ConfigurationStatuses.Published, stored.Status);
    }

    [LinuxDockerFact]
    public async Task FindVersionMetadataAsync_ShouldBatchAndRedactOutsideSiteRecords()
    {
        await postgres.EnsureSchemaAsync();
        await EnsureDataModelAsync();
        var store = new PostgresIngestionTaskStore(postgres.DataSource);
        var taskId = $"metadata-{Guid.NewGuid():N}";
        var authorized = Profile($"{taskId}-authorized", 1) with
        {
            Status = ConfigurationStatuses.Draft,
            EdgeId = "EDGE-SITE-A"
        };
        var sameSiteOtherEdge = Profile($"{taskId}-mismatch", 1) with
        {
            Status = ConfigurationStatuses.Draft,
            EdgeId = "EDGE-SITE-A-OTHER"
        };
        var otherSite = Profile($"{taskId}-other-site", 1) with
        {
            Status = ConfigurationStatuses.Draft,
            EdgeId = "EDGE-SITE-B"
        };
        await store.UpsertAsync(authorized);
        await store.UpsertAsync(sameSiteOtherEdge);
        await store.UpsertAsync(otherSite);

        var found = await store.FindVersionMetadataAsync(
        [
            new(authorized.TaskId, authorized.Version),
            new(sameSiteOtherEdge.TaskId, sameSiteOtherEdge.Version),
            new(otherSite.TaskId, otherSite.Version),
            new($"{taskId}-missing", 7)
        ],
        ["EDGE-SITE-A", "EDGE-SITE-A-OTHER"]);

        var authorizedResult = Assert.Single(found, item => item.TaskId == authorized.TaskId);
        Assert.Equal("EDGE-SITE-A", authorizedResult.AuthorizedEdgeId);
        Assert.Equal(ConfigurationStatuses.Draft, authorizedResult.AuthorizedStatus);
        Assert.False(authorizedResult.ExistsOutsideAuthorizedSite);

        var mismatchCandidate = Assert.Single(found, item => item.TaskId == sameSiteOtherEdge.TaskId);
        Assert.Equal("EDGE-SITE-A-OTHER", mismatchCandidate.AuthorizedEdgeId);
        Assert.Equal(ConfigurationStatuses.Draft, mismatchCandidate.AuthorizedStatus);
        Assert.False(mismatchCandidate.ExistsOutsideAuthorizedSite);

        var outsideResult = Assert.Single(found, item => item.TaskId == otherSite.TaskId);
        Assert.Null(outsideResult.AuthorizedEdgeId);
        Assert.Null(outsideResult.AuthorizedStatus);
        Assert.True(outsideResult.ExistsOutsideAuthorizedSite);

        var missingResult = Assert.Single(found, item => item.TaskId == $"{taskId}-missing");
        Assert.Null(missingResult.AuthorizedEdgeId);
        Assert.Null(missingResult.AuthorizedStatus);
        Assert.False(missingResult.ExistsOutsideAuthorizedSite);

        var serialized = System.Text.Json.JsonSerializer.Serialize(found);
        Assert.DoesNotContain("EDGE-SITE-B", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("baseUrl", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("headersText", serialized, StringComparison.OrdinalIgnoreCase);
    }

    private static IngestionTask Profile(string taskId, int version)
        => new()
        {
            TaskId = taskId,
            Version = version,
            Name = $"Profile {version}",
            Status = ConfigurationStatuses.Published,
            EdgeId = "EDGE-001",
            DataModelId = "model-a",
            Source = $"connector/http/{taskId}",
            SubjectId = "MACHINE-01",
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private async Task EnsureDataModelAsync()
    {
        var configurations = new PostgresProcessConfigurationStore(postgres.DataSource);
        await configurations.TryUpsertDataModelAsync(new ProcessDataModel
        {
            ModelId = "model-a",
            Version = 1,
            Name = "Task store model",
            Status = ConfigurationStatuses.Published,
            Acquisition = new AcquisitionModel(),
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }
}
