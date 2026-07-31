using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Edge.ConnectorHost.Acquisition;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionDeploymentCacheTests
{
    [Fact]
    public async Task Cache_RestoresOnlyTheMatchingEdgeDeployment()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"ingot-acquisition-cache-{Guid.NewGuid():N}",
            "deployments.json");
        var cache = CreateCache(path);
        try
        {
            await cache.SaveAsync("EDGE-001", [Deployment("EDGE-001")]);

            var restored = await cache.LoadAsync("EDGE-001");
            var wrongEdge = await cache.LoadAsync("EDGE-002");

            Assert.Equal("profile-a", Assert.Single(restored!).Profile.ProfileId);
            Assert.Null(wrongEdge);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Cache_PreservesAnAuthoritativeEmptyDeploymentSet()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"ingot-acquisition-cache-{Guid.NewGuid():N}",
            "deployments.json");
        var cache = CreateCache(path);
        try
        {
            await cache.SaveAsync("EDGE-001", []);

            var restored = await cache.LoadAsync("EDGE-001");

            Assert.NotNull(restored);
            Assert.Empty(restored);
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Cache_DoesNotRewriteAnUnchangedDeploymentEveryPoll()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"ingot-acquisition-cache-{Guid.NewGuid():N}",
            "deployments.json");
        var cache = CreateCache(path);
        try
        {
            var deployments = new[] { Deployment("EDGE-001") };
            await cache.SaveAsync("EDGE-001", deployments);
            var firstBytes = await File.ReadAllBytesAsync(path);
            var firstWrite = File.GetLastWriteTimeUtc(path);
            await Task.Delay(20);

            await cache.SaveAsync("EDGE-001", deployments);

            Assert.Equal(firstBytes, await File.ReadAllBytesAsync(path));
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            var directory = Path.GetDirectoryName(path)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static JsonAcquisitionDeploymentCache CreateCache(string path)
        => new(
            Options.Create(new HttpPollingAcquisitionOptions
            {
                DeploymentCachePath = path
            }),
            NullLogger<JsonAcquisitionDeploymentCache>.Instance);

    private static AcquisitionDeployment Deployment(string edgeId)
        => new()
        {
            Profile = new AcquisitionProfile
            {
                ProfileId = "profile-a",
                Name = "Profile A",
                Status = ConfigurationStatuses.Published,
                EdgeId = edgeId,
                DataModelId = "model-a",
                Source = "connector/http-polling/profile-a",
                SubjectId = "MACHINE-01"
            },
            DataModel = new ProcessDataModel
            {
                ModelId = "model-a",
                Name = "Model A",
                Status = ConfigurationStatuses.Published
            }
        };
}
