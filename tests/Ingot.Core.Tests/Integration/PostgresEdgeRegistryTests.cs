using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Edge;
using Ingot.Platform.Infrastructure.Services;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresEdgeRegistryTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task ConcurrentHeartbeats_CommitEveryStatusHistorySample()
    {
        await postgres.EnsureSchemaAsync();
        var edgeId = $"EDGE-CONCURRENT-{Guid.NewGuid():N}";
        var at = DateTimeOffset.UtcNow;
        var registry = new EdgeRegistry(postgres.DataSource);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => registry.HeartbeatAsync(
            edgeId,
            "http://edge.local",
            null,
            new EdgeAcquisitionRuntimeStatus(
                Enabled: true,
                State: "running",
                ReportedAt: at.AddMilliseconds(index),
                ConfigurationSource: AcquisitionConfigurationSources.Platform,
                DesiredConfigurationSetHash: "desired",
                AppliedConfigurationSetHash: "applied",
                LastAttemptAt: at,
                LastReadSuccessAt: at,
                LastValidSnapshotAt: at,
                ReadSuccessCount: index,
                ValidSnapshotCount: index,
                EmittedEventCount: index,
                DuplicateSuppressionCount: 0,
                InactiveSnapshotCount: 0,
                SourceIdentityStallCount: 0,
                LastReadDurationMs: 1,
                ObservedIntervalMs: 1,
                ActiveProcessSpecification: null,
                LastError: null,
                Tasks: [],
                Deployments: []),
            at.AddMilliseconds(index))));

        var history = await registry.ListStatusHistoryAsync(edgeId, 20);
        Assert.Equal(8, history.Count);
        Assert.Equal(7, history[0].ValidSnapshotCount);
    }

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

        await new EdgeRegistry(postgres.DataSource).HeartbeatAsync(
            edgeId,
            "http://edge.local/",
            null,
            acquisition,
            now,
            delivery);

        await new EdgeRegistry(postgres.DataSource).HeartbeatAsync(
            edgeId,
            null,
            null,
            acquisition with { ValidSnapshotCount = 47, EmittedEventCount = 94 },
            now.AddSeconds(10),
            delivery with { PendingEventCount = 5 });

        await new EdgeRegistry(postgres.DataSource).HeartbeatAsync(
            edgeId,
            null,
            "上送中断",
            acquisition with { ValidSnapshotCount = 48, EmittedEventCount = 96 },
            now.AddSeconds(20),
            delivery with { State = "blocked", PendingEventCount = 7, LastError = "平台不可达" });

        var secondReplica = new EdgeRegistry(postgres.DataSource);
        var restored = await secondReplica.FindAsync(edgeId);
        Assert.NotNull(restored);
        Assert.Equal("http://edge.local", restored.HostBaseUrl);
        Assert.Equal(48, restored.Acquisition!.ValidSnapshotCount);
        Assert.Equal(7, restored.Delivery!.PendingEventCount);
        var history = await secondReplica.ListStatusHistoryAsync(edgeId);
        Assert.Equal(3, history.Count);
        Assert.Equal("blocked", history[0].DeliveryState);

        var intervals = await secondReplica.ListStatusIntervalsAsync(edgeId);
        Assert.Equal(2, intervals.Count);
        Assert.Equal("blocked", intervals[0].DeliveryState);
        Assert.Equal("平台不可达", intervals[0].DeliveryError);
        Assert.Equal(1, intervals[0].SampleCount);

        var synchronizedInterval = intervals[1];
        Assert.Equal(now, synchronizedInterval.StartedAt);
        Assert.Equal(now.AddSeconds(10), synchronizedInterval.EndedAt);
        Assert.Equal(2, synchronizedInterval.SampleCount);
        Assert.Equal(84, synchronizedInterval.StartingEmittedEventCount);
        Assert.Equal(94, synchronizedInterval.EndingEmittedEventCount);
        Assert.Equal(5, synchronizedInterval.MaximumPendingEventCount);
    }
}
