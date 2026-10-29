// Polls selected MTConnect DataItems as edge-received snapshots through the protected GET transport.
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal sealed class MtConnectAcquisitionRunner : IAcquisitionProtocolRunner
{
    private readonly IEventSink _sink;
    private readonly MtConnectTransport _transport;
    private readonly AcquisitionStatus _status;
    private readonly ILogger<MtConnectAcquisitionRunner> _logger;

    public MtConnectAcquisitionRunner(
        IEventSink sink,
        MtConnectTransport transport,
        AcquisitionStatus status,
        ILogger<MtConnectAcquisitionRunner> logger)
    {
        _sink = sink;
        _transport = transport;
        _status = status;
        _logger = logger;
    }

    public string Protocol => AcquisitionProtocols.MtConnect;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var task = deployment.Task;
        var connection = task.MtConnect
            ?? throw new InvalidOperationException("MTConnect 连接配置不能为空。");
        MtConnectAcquisitionSupport.ValidateTimestampMode(task);
        var configuredIds = MtConnectAcquisitionSupport.ConfiguredDataItemIds(deployment);
        if (configuredIds.Count == 0)
            throw new InvalidDataException("MTConnect 运行任务至少需要一个已映射的 DataItem ID。");

        var pollDelayMs = Math.Clamp(connection.PollIntervalMs, 100, 3_600_000);
        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        _logger.LogInformation(
            "MTConnect 采集任务已启动：Configuration={Configuration}, Device={Device}, DataItems={DataItemCount}, PollIntervalMs={PollIntervalMs}",
            configurationKey,
            connection.DeviceNameOrUuid,
            configuredIds.Count,
            pollDelayMs);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                MtConnectProbeDocument catalog;
                using (var timeout = CreateReadTimeout(task.Execution.TimeoutMs, ct))
                {
                    catalog = await _transport.ReadProbeAsync(connection, timeout.Token).ConfigureAwait(false);
                }
                EnsureConfiguredItemsSupported(configuredIds, catalog);

                while (!ct.IsCancellationRequested)
                {
                    using var timeout = CreateReadTimeout(task.Execution.TimeoutMs, ct);
                    _status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                    var started = System.Diagnostics.Stopwatch.GetTimestamp();
                    var observations = await _transport.ReadCurrentAsync(
                        connection,
                        catalog,
                        configuredIds,
                        timeout.Token).ConfigureAwait(false);
                    var observedAt = DateTimeOffset.UtcNow;
                    var readDurationMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    _status.RecordReadSuccess(configurationKey, observedAt, readDurationMs);

                    var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
                    foreach (var id in configuredIds)
                        raw[id] = observations.TryGetValue(id, out var observation) ? observation.Value : null;
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
                        _status.RecordDuplicateSnapshot(
                            configurationKey,
                            deduplication == AcquisitionDeduplicationResult.Stalled,
                            $"MTConnect /current 快照在 {task.Execution.SourceIdentityStaleAfterMs}ms 内未变化。");
                        await Task.Delay(pollDelayMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    var events = lifecycle.Track(mapped, task.Lifecycle, pollDelayMs);
                    await _sink.EmitBatchAsync(events, ct).ConfigureAwait(false);
                    _status.RecordProcessExecutionState(configurationKey, lifecycle.IsRunActive);
                    _status.RecordEmissionOutcome(configurationKey, events.Count, task.Lifecycle is not null && events.Count == 0);
                    currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                    _status.RecordValidSnapshot(
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
                _status.RecordFailure(configurationKey, exception.Message);
                _logger.LogWarning(
                    exception,
                    "MTConnect 采集任务 {Configuration} 读取失败，等待后重新探查并重试",
                    configurationKey);
                await Task.Delay(task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    private static CancellationTokenSource CreateReadTimeout(int timeoutMs, CancellationToken parent)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(parent);
        timeout.CancelAfter(Math.Clamp(timeoutMs, 500, 30_000));
        return timeout;
    }

    private static void EnsureConfiguredItemsSupported(
        IReadOnlyCollection<string> configuredIds,
        MtConnectProbeDocument catalog)
    {
        foreach (var id in configuredIds)
        {
            if (!catalog.DataItems.TryGetValue(id, out var item))
                throw new InvalidDataException(
                    $"MTConnect 设备目录中找不到已配置 DataItem ID {id}；请重新探查并修复映射。");
            if (!item.IsSupportedScalar)
                throw new InvalidDataException(
                    $"MTConnect DataItem {id} ({item.Category}/{item.Type}) 暂不支持；只支持标量 SAMPLE 和 EVENT，条件或复合值不能作为普通采样值发布。");
        }
    }
}
