// Poll configured Siemens S7 addresses and publish validated acquisition snapshots.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class SiemensS7AcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    AcquisitionHttpEgressPolicy egressPolicy,
    ILogger<SiemensS7AcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.SiemensS7;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.SiemensS7
            ?? throw new InvalidOperationException("Siemens S7 连接配置不能为空。");
        var selectors = SiemensS7ProtocolSupport.BuildSelectors(deployment);
        if (selectors.Count == 0)
            throw new InvalidOperationException("Siemens S7 不支持地址扫描；请先配置至少一个 DB、I、Q 或 M 区点位。");

        using var plc = await SiemensS7ProtocolSupport.OpenAsync(
            connection, egressPolicy, task.Execution.TimeoutMs, ct).ConfigureAwait(false);
        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        logger.LogInformation(
            "Siemens S7 采集任务已连接：Configuration={Configuration}, Device={Host}:{Port}, Cpu={CpuType}, Rack={Rack}, Slot={Slot}",
            configurationKey,
            connection.Host,
            connection.Port,
            connection.CpuType,
            connection.Rack,
            connection.Slot);

        while (!ct.IsCancellationRequested)
        {
            status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
            var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            var raw = await SiemensS7ProtocolSupport.ReadSnapshotAsync(
                plc,
                selectors,
                connection.MaxMergeGap,
                task.Execution.TimeoutMs,
                ct).ConfigureAwait(false);
            var observedAt = DateTimeOffset.UtcNow;
            status.RecordReadSuccess(
                configurationKey,
                observedAt,
                System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);
            var occurredAt = ResolveTimestamp(task, raw, observedAt);
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

    private static DateTimeOffset ResolveTimestamp(
        IngestionTask task,
        IReadOnlyDictionary<string, object?> raw,
        DateTimeOffset observedAt)
    {
        if (task.TimestampMode != "source" || string.IsNullOrWhiteSpace(task.TimestampPath))
            return observedAt;
        raw.TryGetValue(task.TimestampPath, out var value);
        return AcquisitionTimestampParser.Parse(
            value,
            task.TimestampEncoding,
            task.TimestampPath,
            observedAt,
            task.Execution.MaximumFutureTimestampSkewMs);
    }
}
