using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ingot.Infrastructure.Acquisition;

/// <summary>
///     心跳监控器，周期性检测 Plc 连通性。
///     连接恢复由下次心跳检测自动完成，无需额外重连逻辑。
/// </summary>
public class HeartbeatMonitor : IHeartbeatMonitor
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _connectionStartTimes = new();
    private readonly ILogger<HeartbeatMonitor> _logger;
    private readonly IMetricsCollector? _metricsCollector;
    private readonly ConcurrentDictionary<string, bool> _connectionHealth = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastConnectedTimes = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastDisconnectedTimes = new();
    private readonly ConcurrentDictionary<string, int> _reconnectCounts = new();
    private readonly ConcurrentDictionary<string, string?> _lastErrors = new();
    private readonly IPlcClientLifecycleService _plcLifecycle;

    public HeartbeatMonitor(IPlcClientLifecycleService plcLifecycle, ILogger<HeartbeatMonitor> logger,
        IMetricsCollector? metricsCollector = null)
    {
        _plcLifecycle = plcLifecycle;
        _logger = logger;
        _metricsCollector = metricsCollector;
    }

    public async Task MonitorAsync(DeviceConfig config, IPlcTypedWriteClient client, CancellationToken ct = default)
    {
        var lastOk = false;
        ushort writeData = 0;

        _logger.LogInformation("{SourceCode}-开始心跳监控，目标地址: {Host}:{Port}，心跳寄存器: {Register}，检测间隔: {Interval}ms",
            config.SourceCode, config.Host, config.Port, config.HeartbeatMonitorRegister, config.HeartbeatPollingInterval);

        while (!ct.IsCancellationRequested)
            try
            {
                // 直接使用已获取的客户端实例写入心跳寄存器
                var connect = await client.WriteUShortAsync(config.HeartbeatMonitorRegister, writeData)
                    .ConfigureAwait(false);
                var ok = connect.IsSuccess;

                if (ok)
                {
                    writeData ^= 1;
                    _connectionHealth[config.SourceCode] = true;
                    _lastErrors.TryRemove(config.SourceCode, out _); // 清除错误信息

                    // 从失败状态恢复时记录日志
                    if (!lastOk)
                    {
                        _lastConnectedTimes[config.SourceCode] = DateTimeOffset.UtcNow;
                        _reconnectCounts.AddOrUpdate(config.SourceCode, 1, (_, c) => c + 1);
                        _logger.LogInformation("{SourceCode}-✓ PLC 连接成功，心跳检测正常 (地址: {Host}:{Port}, 寄存器: {Register})",
                            config.SourceCode, config.Host, config.Port, config.HeartbeatMonitorRegister);
                        _metricsCollector?.RecordConnectionStatus(config.SourceCode, true);
                        _connectionStartTimes[config.SourceCode] = DateTimeOffset.UtcNow;
                    }
                }
                else
                {
                    _connectionHealth[config.SourceCode] = false;
                    _lastErrors[config.SourceCode] = connect.Message; // 记录错误信息

                    // 从成功状态变为失败时记录日志
                    if (lastOk)
                    {
                        _lastDisconnectedTimes[config.SourceCode] = DateTimeOffset.UtcNow;
                        _logger.LogWarning("{SourceCode}-✗ PLC 连接失败: {Message} (地址: {Host}:{Port}, 寄存器: {Register})",
                            config.SourceCode, connect.Message, config.Host, config.Port, config.HeartbeatMonitorRegister);
                        _metricsCollector?.RecordConnectionStatus(config.SourceCode, false);
                        if (_connectionStartTimes.TryRemove(config.SourceCode, out var startTime))
                            _metricsCollector?.RecordConnectionDuration(config.SourceCode,
                                (DateTimeOffset.UtcNow - startTime).TotalSeconds);
                    }
                }

                lastOk = ok;
            }
            catch (Exception ex)
            {
                _connectionHealth[config.SourceCode] = false;
                _lastErrors[config.SourceCode] = ex.Message; // 记录异常信息
                if (lastOk)
                    _logger.LogError(ex, "{SourceCode}-心跳检测异常: {Message}", config.SourceCode, ex.Message);
                lastOk = false;
            }
            finally
            {
                await Task.Delay(config.HeartbeatPollingInterval, ct).ConfigureAwait(false);
            }
    }

    public bool TryGetConnectionHealth(string sourceCode, out bool isConnected)
    {
        return _connectionHealth.TryGetValue(sourceCode, out isConnected);
    }

    public PlcConnectionStatus? GetConnectionStatus(string sourceCode)
    {
        if (!_connectionHealth.TryGetValue(sourceCode, out var isConnected))
            return null;

        var lastConnectedTime = _lastConnectedTimes.TryGetValue(sourceCode, out var time) ? time : (DateTimeOffset?)null;
        var lastDisconnectedTime = _lastDisconnectedTimes.TryGetValue(sourceCode, out var dTime) ? dTime : (DateTimeOffset?)null;
        var reconnectCount = _reconnectCounts.TryGetValue(sourceCode, out var rc) ? rc : 0;
        var lastError = _lastErrors.TryGetValue(sourceCode, out var error) ? error : null;

        double? connectionDuration = null;
        if (isConnected && _connectionStartTimes.TryGetValue(sourceCode, out var startTime))
        {
            connectionDuration = (DateTimeOffset.UtcNow - startTime).TotalSeconds;
        }

        double? disconnectedDuration = null;
        if (!isConnected && lastDisconnectedTime.HasValue)
        {
            disconnectedDuration = (DateTimeOffset.UtcNow - lastDisconnectedTime.Value).TotalSeconds;
        }

        return new PlcConnectionStatus
        {
            SourceCode = sourceCode,
            IsConnected = isConnected,
            LastConnectedTime = lastConnectedTime,
            ConnectionDurationSeconds = connectionDuration,
            LastError = lastError,
            DisconnectedDurationSeconds = disconnectedDuration,
            TotalReconnectCount = reconnectCount,
            LastDisconnectedTime = lastDisconnectedTime
        };
    }
}
