using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresAcquisitionProfileStoreTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ConcurrentPublication_ShouldLeaveExactlyOnePublishedVersion()
    {
        await using var store = new PostgresAcquisitionProfileStore(postgres.Configuration);
        await store.InitializeAsync();
        var profileId = $"concurrent-{Guid.NewGuid():N}";
        var first = Profile(profileId, 1);
        var second = Profile(profileId, 2);

        await Task.WhenAll(
            store.PublishExclusiveAsync(first),
            store.PublishExclusiveAsync(second));

        var all = (await store.ListAsync()).Where(item => item.ProfileId == profileId).ToArray();
        var published = Assert.Single(all, item => item.Status == ConfigurationStatuses.Published);
        var retired = Assert.Single(all, item => item.Status == ConfigurationStatuses.Retired);
        Assert.NotEqual(published.Version, retired.Version);
        Assert.Equal(published.Version, (await store.GetAsync(profileId, published.Version))?.Version);
        Assert.Equal(ConfigurationStatuses.Retired, (await store.GetAsync(profileId, retired.Version))?.Status);
    }

    private static AcquisitionProfile Profile(string profileId, int version)
        => new()
        {
            ProfileId = profileId,
            Version = version,
            Name = $"Profile {version}",
            Status = ConfigurationStatuses.Published,
            EdgeId = "EDGE-001",
            DataModelId = "model-a",
            Source = $"connector/http/{profileId}",
            SubjectId = "MACHINE-01",
            UpdatedAt = DateTimeOffset.UtcNow
        };
}
