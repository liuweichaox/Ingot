// Poll configured BACnet Present_Value properties and publish validated acquisition snapshots.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class BacnetIpAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    AcquisitionHttpEgressPolicy egressPolicy,
    ILogger<BacnetIpAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.BacnetIp;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.BacnetIp
            ?? throw new InvalidOperationException("BACnet/IP 连接配置不能为空。");
        var selectors = BacnetIpProtocolSupport.BuildSelectors(deployment);
        using var session = await BacnetIpProtocolSupport.OpenAsync(
            connection,
            egressPolicy,
            task.Execution.TimeoutMs,
            logger,
            ct).ConfigureAwait(false);
        await session.VerifyDeviceIdentifierAsync(ct).ConfigureAwait(false);
        var objectList = await session.ReadObjectListAsync(ct).ConfigureAwait(false);

        foreach (var selector in selectors)
        {
            if (!ContainsObject(objectList, selector.ObjectId))
            {
                throw new InvalidOperationException(
                    $"配置的 BACnet 对象 {selector.SourcePath} 不在设备 Object_List 中。");
            }
        }

        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        logger.LogInformation(
            "BACnet/IP 采集任务已连接：Configuration={Configuration}, Device={Host}:{Port}, Instance={DeviceInstance}, Objects={ObjectCount}",
            configurationKey,
            connection.Host,
            connection.Port,
            connection.DeviceInstance,
            objectList.Count);

        while (!ct.IsCancellationRequested)
        {
            status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
            var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var raw = await session.ReadSnapshotAsync(selectors, ct).ConfigureAwait(false);
            var observedAt = DateTimeOffset.UtcNow;
            status.RecordReadSuccess(
                configurationKey,
                observedAt,
                System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);

            var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                deployment,
                raw,
                normalizedSource,
                currentProcessSpecification,
                observedAt);
            var deduplication = sourceDeduplicator.Evaluate(
                mapped.Sample,
                observedAt,
                TimeSpan.FromMilliseconds(task.Execution.SourceIdentityStaleAfterMs));
            if (deduplication is AcquisitionDeduplicationResult.Duplicate or AcquisitionDeduplicationResult.Stalled)
            {
                currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                status.RecordDuplicateSnapshot(
                    configurationKey,
                    deduplication == AcquisitionDeduplicationResult.Stalled,
                    $"设备源身份超过 {task.Execution.SourceIdentityStaleAfterMs}ms 未变化。");
                await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
                continue;
            }

            var events = lifecycle.Track(mapped, task.Lifecycle, connection.PollIntervalMs);
            await sink.EmitBatchAsync(events, ct).ConfigureAwait(false);
            status.RecordProcessExecutionState(configurationKey, lifecycle.IsRunActive);
            status.RecordEmissionOutcome(
                configurationKey,
                events.Count,
                task.Lifecycle is not null && events.Count == 0);
            currentProcessSpecification = mapped.ProcessSpecificationIdentity;
            status.RecordValidSnapshot(
                configurationKey,
                observedAt,
                currentProcessSpecification,
                deduplication == AcquisitionDeduplicationResult.Changed ? observedAt : null);
            await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
        }
    }

    private static bool ContainsObject(IReadOnlyList<System.IO.BACnet.BacnetObjectId> objects, System.IO.BACnet.BacnetObjectId expected)
        => objects.Any(item => item.Type == expected.Type && item.Instance == expected.Instance);
}
