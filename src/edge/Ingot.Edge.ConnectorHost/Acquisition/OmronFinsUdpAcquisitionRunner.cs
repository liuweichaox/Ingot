// Poll configured Omron FINS/UDP points and publish validated mapped snapshots.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class OmronFinsUdpAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    AcquisitionHttpEgressPolicy egressPolicy,
    ILogger<OmronFinsUdpAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.OmronFinsUdp;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.OmronFinsUdp
            ?? throw new InvalidOperationException("Omron FINS/UDP 连接配置不能为空。");
        var selectors = OmronFinsUdpProtocolSupport.BuildSelectors(deployment);
        if (selectors.Count == 0)
            throw new InvalidOperationException("Omron FINS/UDP 不支持地址扫描；请先配置至少一个已知 PLC 点位。");

        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await egressPolicy.ConnectUdpAsync(
                    connection.Host,
                    connection.Port,
                    "Omron FINS/UDP",
                    task.Execution.TimeoutMs,
                    ct).ConfigureAwait(false);
                using var session = new OmronFinsUdpSession(
                    client,
                    connection.ClientNodeAddress,
                    connection.ServerNodeAddress);
                logger.LogInformation(
                    "Omron FINS/UDP 采集任务已连接：Configuration={Configuration}, Device={Host}:{Port}, ClientNode={ClientNode}, ServerNode={ServerNode}, Points={PointCount}",
                    configurationKey,
                    connection.Host,
                    connection.Port,
                    connection.ClientNodeAddress,
                    connection.ServerNodeAddress,
                    selectors.Count);

                while (!ct.IsCancellationRequested)
                {
                    status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                    var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    var raw = await OmronFinsUdpProtocolSupport.ReadSnapshotAsync(
                        session,
                        selectors,
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
                    status.RecordEmissionOutcome(configurationKey, events.Count, task.Lifecycle is not null && events.Count == 0);
                    currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                    status.RecordValidSnapshot(
                        configurationKey,
                        observedAt,
                        currentProcessSpecification,
                        deduplication == AcquisitionDeduplicationResult.Changed ? observedAt : null);
                    await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(
                    exception,
                    "Omron FINS/UDP 采集任务 {Configuration} 读取失败，等待后重新连接",
                    configurationKey);
                await Task.Delay(task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
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
