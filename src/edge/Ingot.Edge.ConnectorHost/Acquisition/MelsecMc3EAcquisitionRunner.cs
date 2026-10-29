// Runs read-only MC 3E sampling through the shared, allowlisted TCP session.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>Polls declared MELSEC MC 3E points and publishes mapped snapshots.</summary>
public sealed class MelsecMc3EAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    AcquisitionHttpEgressPolicy egressPolicy,
    ILogger<MelsecMc3EAcquisitionRunner> logger,
    RegisterAcquisitionSessionPool? sessionPool = null) : IAcquisitionProtocolRunner
{
    private readonly RegisterAcquisitionSessionPool _sessionPool = sessionPool ?? new();

    public string Protocol => AcquisitionProtocols.MelsecMc3E;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.MelsecMc3E
            ?? throw new InvalidOperationException("MELSEC MC 3E 连接配置不能为空。");
        var selectors = MelsecMc3EProtocolSupport.BuildSelectors(deployment);
        var plan = MelsecMc3EProtocolSupport.BuildReadPlan(selectors, connection.MaxMergeGap);
        await using var lease = await _sessionPool.AcquireAsync(
            AcquisitionProtocols.MelsecMc3E,
            connection.Host,
            connection.Port,
            "binary",
            task.DataSourceId,
            task.DataSourceVersion,
            RegisterAcquisitionSessionPool.CreateSourceVersionFingerprint(connection, task),
            token => egressPolicy.ConnectTcpAsync(
                connection.Host,
                connection.Port,
                "MELSEC MC 3E",
                task.Execution.TimeoutMs,
                token),
            ct).ConfigureAwait(false);

        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        logger.LogInformation(
            "MELSEC MC 3E 采集任务已加入共享连接：Configuration={Configuration}, Device={Host}:{Port}, Family={Family}, Points={PointCount}",
            configurationKey,
            connection.Host,
            connection.Port,
            connection.Family,
            selectors.Count);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var read = await lease.ExecuteMelsecMc3EAsync(
                    task.Execution.TimeoutMs,
                    async (stream, readToken) =>
                    {
                        var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                        var raw = await MelsecMc3EProtocolSupport.ReadSnapshotAsync(
                            stream,
                            connection,
                            plan,
                            readToken).ConfigureAwait(false);
                        return (Raw: raw, ReadDurationMs: System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);
                    },
                    ct).ConfigureAwait(false);

                var observedAt = DateTimeOffset.UtcNow;
                status.RecordReadSuccess(configurationKey, observedAt, read.ReadDurationMs);
                var occurredAt = ResolveTimestamp(task, read.Raw, observedAt);
                var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                    deployment,
                    read.Raw,
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(
                    exception,
                    "MELSEC MC 3E 共享采集任务 {Configuration} 读取失败，等待后重试",
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
