// Runs configured Omron FINS/TCP reads and publishes mapped production snapshots.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class OmronFinsTcpAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    AcquisitionHttpEgressPolicy egressPolicy,
    ILogger<OmronFinsTcpAcquisitionRunner> logger,
    RegisterAcquisitionSessionPool? sessionPool = null) : IAcquisitionProtocolRunner
{
    private readonly RegisterAcquisitionSessionPool _sessionPool = sessionPool ?? new();

    public string Protocol => AcquisitionProtocols.OmronFinsTcp;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.OmronFinsTcp
            ?? throw new InvalidOperationException("Omron FINS/TCP 连接配置不能为空。");
        var selectors = OmronFinsTcpProtocolSupport.BuildSelectors(deployment);
        await using var lease = await _sessionPool.AcquireAsync(
            AcquisitionProtocols.OmronFinsTcp,
            connection.Host,
            connection.Port,
            string.Empty,
            task.DataSourceId,
            task.DataSourceVersion,
            RegisterAcquisitionSessionPool.CreateSourceVersionFingerprint(connection, task),
            token => egressPolicy.ConnectTcpAsync(
                connection.Host,
                connection.Port,
                "Omron FINS/TCP",
                task.Execution.TimeoutMs,
                token),
            ct).ConfigureAwait(false);

        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        logger.LogInformation(
            "Omron FINS/TCP 采集任务已加入共享连接：Configuration={Configuration}, Device={Host}:{Port}, Points={PointCount}",
            configurationKey,
            connection.Host,
            connection.Port,
            selectors.Count);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var read = await lease.ExecuteOmronFinsTcpAsync(
                    task.Execution.TimeoutMs,
                    async (session, readToken) =>
                    {
                        var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                        var raw = await OmronFinsTcpProtocolSupport.ReadSnapshotAsync(session, selectors, readToken)
                            .ConfigureAwait(false);
                        return (Raw: raw, ReadDurationMs: System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);
                    },
                    ct).ConfigureAwait(false);

                var observedAt = DateTimeOffset.UtcNow;
                status.RecordReadSuccess(configurationKey, observedAt, read.ReadDurationMs);
                var occurredAt = ResolveTimestamp(task, read.Raw, observedAt);
                var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                    deployment, read.Raw, normalizedSource, currentProcessSpecification, occurredAt);
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
                status.RecordEmissionOutcome(configurationKey, events.Count, task.Lifecycle is not null && events.Count == 0);
                currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                status.RecordValidSnapshot(
                    configurationKey,
                    observedAt,
                    currentProcessSpecification,
                    deduplication == AcquisitionDeduplicationResult.Changed ? observedAt : null);
                await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
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
                    "Omron FINS/TCP 共享采集任务 {Configuration} 读取失败，等待后重试",
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
