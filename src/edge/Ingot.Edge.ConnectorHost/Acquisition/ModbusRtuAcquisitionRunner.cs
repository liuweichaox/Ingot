// 通过允许清单中的共享 RS-485 串口只读采集 Modbus RTU 寄存器。
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class ModbusRtuAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    ILogger<ModbusRtuAcquisitionRunner> logger,
    ModbusSerialSessionPool sessionPool) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.ModbusRtu;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var connection = deployment.Task.ModbusRtu
            ?? throw new InvalidOperationException("Modbus RTU 串口配置不能为空。");
        var task = deployment.Task;
        await using var lease = await sessionPool.AcquireAsync(
            connection,
            task.DataSourceId,
            task.DataSourceVersion,
            ModbusSerialSessionPool.CreateSourceVersionFingerprint(connection, task),
            ct).ConfigureAwait(false);

        var selectors = ModbusTcpAcquisitionRunner.BuildSelectors(deployment, connection.AddressBase);
        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        logger.LogInformation(
            "Modbus RTU 采集任务已加入共享串口：Configuration={Configuration}, Port={Port}, Unit={UnitId}",
            configurationKey,
            connection.PortName,
            connection.UnitId);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var read = await lease.ExecuteModbusAsync(
                    task.Execution.TimeoutMs,
                    async (master, readToken) =>
                    {
                        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                        status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                        var values = await ModbusTcpAcquisitionRunner.ReadSnapshotAsync(
                            master,
                            connection.UnitId,
                            selectors,
                            connection.MaxMergeGap,
                            readToken).ConfigureAwait(false);
                        return (Values: values, DurationMs: System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    },
                    ct).ConfigureAwait(false);

                var observedAt = DateTimeOffset.UtcNow;
                status.RecordReadSuccess(configurationKey, observedAt, read.DurationMs);
                var occurredAt = task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath)
                    ? AcquisitionTimestampParser.Parse(
                        read.Values[task.TimestampPath],
                        task.TimestampEncoding,
                        task.TimestampPath,
                        observedAt,
                        task.Execution.MaximumFutureTimestampSkewMs)
                    : observedAt;
                var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                    deployment, read.Values, normalizedSource, currentProcessSpecification, occurredAt);
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
                logger.LogWarning(exception, "Modbus RTU 采集任务 {Configuration} 读取失败，等待后重试", configurationKey);
                await Task.Delay(task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }
}
