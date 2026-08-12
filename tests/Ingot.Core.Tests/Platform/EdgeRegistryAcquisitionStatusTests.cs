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
                Enabled: true,
                State: "running",
                ReportedAt: DateTimeOffset.UtcNow,
                ConfigurationSource: AcquisitionConfigurationSources.Platform,
                DesiredConfigurationSetHash: "desired-hash",
                AppliedConfigurationSetHash: "applied-hash",
                LastAttemptAt: DateTimeOffset.UtcNow,
                LastReadSuccessAt: DateTimeOffset.UtcNow,
                LastValidSnapshotAt: DateTimeOffset.UtcNow,
                ReadSuccessCount: 45,
                ValidSnapshotCount: 42,
                EmittedEventCount: 84,
                DuplicateSuppressionCount: 3,
                InactiveSnapshotCount: 2,
                SourceIdentityStallCount: 0,
                LastReadDurationMs: 12,
                ObservedIntervalMs: 1000,
                ActiveProcessSpecification: "processSpecification-a@1",
                LastError: null,
                Tasks: [],
                Deployments:
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
            Assert.Equal(42, restored.Acquisition.ValidSnapshotCount);
            Assert.Equal(84, restored.Acquisition.EmittedEventCount);
            Assert.Equal(2, Assert.Single(restored.Acquisition.Deployments).AppliedVersion);
            Assert.NotNull(restored.Delivery);
            Assert.Equal(3, restored.Delivery.PendingEventCount);
            Assert.Equal(41, restored.Delivery.LastAcknowledgedSequence);
            Assert.Equal(2, restored.Delivery.RecoveryCount);
            var history = new EdgeRegistry(configuration).ListStatusHistory("EDGE-001");
            var snapshot = Assert.Single(history);
            Assert.Equal("running", snapshot.AcquisitionState);
            Assert.Equal(42, snapshot.ValidSnapshotCount);
            Assert.Equal(84, snapshot.EmittedEventCount);
            Assert.Equal(3, snapshot.PendingEventCount);
            Assert.Equal("synchronized", snapshot.DeliveryState);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
