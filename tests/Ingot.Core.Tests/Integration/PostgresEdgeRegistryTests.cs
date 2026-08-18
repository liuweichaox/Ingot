using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Edge;
using Ingot.Platform.Infrastructure.Services;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresEdgeRegistryTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task Heartbeat_ShouldBeVisibleAcrossRegistryInstancesWithHistory()
    {
        await postgres.EnsureSchemaAsync();
        var edgeId = $"EDGE-{Guid.NewGuid():N}";
        var now = DateTimeOffset.UtcNow;
        var acquisition = new EdgeAcquisitionRuntimeStatus(
            Enabled: true,
            State: "running",
            ReportedAt: now,
            ConfigurationSource: AcquisitionConfigurationSources.Platform,
            DesiredConfigurationSetHash: "desired-hash",
            AppliedConfigurationSetHash: "applied-hash",
            LastAttemptAt: now,
            LastReadSuccessAt: now,
            LastValidSnapshotAt: now,
            ReadSuccessCount: 45,
            ValidSnapshotCount: 42,
            EmittedEventCount: 84,
            DuplicateSuppressionCount: 3,
            InactiveSnapshotCount: 2,
            SourceIdentityStallCount: 0,
            LastReadDurationMs: 12,
            ObservedIntervalMs: 1000,
            ActiveProcessSpecification: "process-specification-a@1",
            LastError: null,
            Tasks: [],
            Deployments: []);
        var delivery = new EdgeDeliveryRuntimeStatus
        {
            State = "synchronized",
            PendingEventCount = 3,
            LastAcknowledgedSequence = 41,
            RecoveryCount = 2,
            LastRecoveryDurationMs = 1250
        };

        new EdgeRegistry(postgres.DataSource).Heartbeat(
            edgeId,
            "http://edge.local/",
            null,
            acquisition,
            now,
            delivery);

        var secondReplica = new EdgeRegistry(postgres.DataSource);
        var restored = secondReplica.Find(edgeId);
        Assert.NotNull(restored);
        Assert.Equal("http://edge.local", restored.HostBaseUrl);
        Assert.Equal(42, restored.Acquisition!.ValidSnapshotCount);
        Assert.Equal(3, restored.Delivery!.PendingEventCount);
        var history = Assert.Single(secondReplica.ListStatusHistory(edgeId));
        Assert.Equal("running", history.AcquisitionState);
        Assert.Equal("synchronized", history.DeliveryState);
        Assert.Equal(84, history.EmittedEventCount);
    }
}
