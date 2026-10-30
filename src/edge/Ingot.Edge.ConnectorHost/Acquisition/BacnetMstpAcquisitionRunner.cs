// Polls configured BACnet MS/TP Present_Value properties and emits validated snapshots.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class BacnetMstpAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    BacnetMstpSessionPool sessionPool,
    ILogger<BacnetMstpAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.BacnetMstp;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.BacnetMstp
            ?? throw new InvalidOperationException("BACnet MS/TP 连接配置不能为空。");
        var selectors = BacnetIpProtocolSupport.BuildSelectors(deployment);
        await using var session = await sessionPool.AcquireAsync(connection, ct).ConfigureAwait(false);
        var objectList = await session.ReadObjectListAsync(
            connection.DeviceInstance, task.Execution.TimeoutMs, ct).ConfigureAwait(false);

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
            "BACnet MS/TP 采集任务已连接：Configuration={Configuration}, Port={Port}, Baud={Baud}, DeviceInstance={DeviceInstance}, Objects={ObjectCount}",
            configurationKey,
            connection.PortName,
            connection.BaudRate,
            connection.DeviceInstance,
            objectList.Count);

        while (!ct.IsCancellationRequested)
        {
            status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
            var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var raw = await session.ReadSnapshotAsync(
                connection.DeviceInstance, selectors, task.Execution.TimeoutMs, ct).ConfigureAwait(false);
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
            var deduplication = sourceDeduplicator.EvaluatePending(
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

            var events = await lifecycle.TrackAndEmitAsync(mapped, task.Lifecycle, connection.PollIntervalMs, sink, ct).ConfigureAwait(false);
            sourceDeduplicator.Commit(mapped.Sample, observedAt);
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

    private static bool ContainsObject(
        IReadOnlyList<System.IO.BACnet.BacnetObjectId> objects,
        System.IO.BACnet.BacnetObjectId expected)
        => objects.Any(item => item.Type == expected.Type && item.Instance == expected.Instance);
}
