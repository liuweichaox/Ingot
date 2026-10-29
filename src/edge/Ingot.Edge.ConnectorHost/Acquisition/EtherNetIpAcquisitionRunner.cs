// Poll declared EtherNet/IP tags and publish mapper-validated production snapshots.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class EtherNetIpAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    AcquisitionHttpEgressPolicy egressPolicy,
    ILogger<EtherNetIpAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.EtherNetIp;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.EtherNetIp
            ?? throw new InvalidOperationException("EtherNet/IP 连接配置不能为空。");
        var selectors = EtherNetIpProtocolSupport.BuildSelectors(deployment);
        using var session = await EtherNetIpProtocolSupport.OpenAsync(
            connection,
            selectors,
            egressPolicy,
            task.Execution.TimeoutMs,
            ct).ConfigureAwait(false);

        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        logger.LogInformation(
            "EtherNet/IP 采集任务已连接：Configuration={Configuration}, Device={Host}:{Port}, Controller={ControllerFamily}, Tags={TagCount}",
            configurationKey,
            connection.Host,
            connection.Port,
            connection.ControllerFamily,
            selectors.Count);

        while (!ct.IsCancellationRequested)
        {
            status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
            var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var raw = await session.ReadSnapshotAsync(ct).ConfigureAwait(false);
            var observedAt = DateTimeOffset.UtcNow;
            status.RecordReadSuccess(
                configurationKey,
                observedAt,
                System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);

            var occurredAt = observedAt;
            if (task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath))
            {
                raw.TryGetValue(task.TimestampPath, out var sourceTimestamp);
                occurredAt = AcquisitionTimestampParser.Parse(
                    sourceTimestamp,
                    task.TimestampEncoding,
                    task.TimestampPath,
                    observedAt,
                    task.Execution.MaximumFutureTimestampSkewMs);
            }

            var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                deployment,
                raw,
                normalizedSource,
                currentProcessSpecification,
                occurredAt);
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
}
