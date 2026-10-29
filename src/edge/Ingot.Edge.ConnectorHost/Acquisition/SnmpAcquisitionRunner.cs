// Reads selected SNMP OIDs using GET only and publishes validated edge snapshots.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class SnmpAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    AcquisitionHttpEgressPolicy egressPolicy,
    IAcquisitionSecretResolver secretResolver,
    ILogger<SnmpAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.Snmp;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.Snmp
            ?? throw new InvalidOperationException("SNMP 连接配置不能为空。");
        var selectors = SnmpProtocolSupport.ConfiguredOids(deployment);
        if (selectors.Count == 0)
            throw new InvalidDataException("SNMP 运行任务至少需要一个已映射的数字 OID。");
        var dataTypes = SnmpProtocolSupport.ConfiguredDataTypes(deployment);
        var pollDelayMs = Math.Clamp(connection.PollIntervalMs, 100, 3_600_000);
        var requestTimeoutMs = Math.Clamp(task.Execution.TimeoutMs, 100, 30_000);
        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();

        logger.LogInformation(
            "SNMP 采集任务已启动：Configuration={Configuration}, Device={Host}:{Port}, Version={Version}, Oids={OidCount}, PollIntervalMs={PollIntervalMs}",
            configurationKey,
            connection.Host,
            connection.Port,
            connection.Version,
            selectors.Count,
            pollDelayMs);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var session = await SnmpProtocolSupport.OpenAsync(
                    connection,
                    secretResolver,
                    egressPolicy,
                    requestTimeoutMs,
                    logger,
                    ct).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    var raw = await session.ReadSnapshotAsync(selectors, dataTypes, ct).ConfigureAwait(false);
                    var observedAt = DateTimeOffset.UtcNow;
                    status.RecordReadSuccess(
                        configurationKey,
                        observedAt,
                        System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds);

                    var occurredAt = observedAt;
                    if (task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath))
                    {
                        raw.TryGetValue(task.TimestampPath, out var sourceTimestamp);
                        occurredAt = AcquisitionTimestampParser.Parse(
                            sourceTimestamp,
                            SnmpProtocolSupport.ResolveTimestampEncoding(task, sourceTimestamp),
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
                            $"SNMP 源身份在 {task.Execution.SourceIdentityStaleAfterMs}ms 内未变化。");
                        await Task.Delay(pollDelayMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    var events = lifecycle.Track(mapped, task.Lifecycle, pollDelayMs);
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
                    await Task.Delay(pollDelayMs, ct).ConfigureAwait(false);
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
                    "SNMP 采集任务 {Configuration} 读取失败，等待后重新建立会话并重试",
                    configurationKey);
                await Task.Delay(task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }
}
