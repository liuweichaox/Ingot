using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Edge;
using Ingot.Platform.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class EdgeRegistryAcquisitionStatusTests
{
    [Fact]
    public void Heartbeat_ShouldPersistEdgeInitiatedAcquisitionStatus()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ingot-edge-registry-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "platform.db");
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["Platform:DatabasePath"] = path }).Build();
            var report = new EdgeAcquisitionRuntimeStatus(
                true,
                "running",
                DateTimeOffset.UtcNow,
                AcquisitionConfigurationSources.Platform,
                "desired-hash",
                "applied-hash",
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                42,
                12,
                1000,
                "processSpecification-a@1",
                null,
                [],
                [
                    new AcquisitionDeploymentApplicationStatus(
                        "profile-a",
                        2,
                        "deployment-hash",
                        2,
                        "deployment-hash",
                        AcquisitionApplicationStates.Applied,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        null)
                ]);

            var first = new EdgeRegistry(configuration);
            first.Heartbeat(
                "EDGE-001",
                null,
                null,
                report,
                DateTimeOffset.UtcNow,
                new EdgeDeliveryRuntimeStatus
                {
                    State = "synchronized",
                    PendingEventCount = 3,
                    LastAcknowledgedSequence = 41,
                    RecoveryCount = 2,
                    LastRecoveryDurationMs = 1250
                });

            var restored = new EdgeRegistry(configuration).Find("EDGE-001");
            Assert.NotNull(restored?.Acquisition);
            Assert.Equal("desired-hash", restored.Acquisition.DesiredConfigurationSetHash);
            Assert.Equal(42, restored.Acquisition.SamplesCollected);
            Assert.Equal(2, Assert.Single(restored.Acquisition.Deployments).AppliedVersion);
            Assert.NotNull(restored.Delivery);
            Assert.Equal(3, restored.Delivery.PendingEventCount);
            Assert.Equal(41, restored.Delivery.LastAcknowledgedSequence);
            Assert.Equal(2, restored.Delivery.RecoveryCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
