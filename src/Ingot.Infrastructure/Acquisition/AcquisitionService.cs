using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Application;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Ingot.Infrastructure.Acquisition;

/// <summary>
///     数据采集任务编排器。管理 PLC 运行时生命周期（启动/停止/热更新）、心跳和通道采集任务。
/// </summary>
public class AcquisitionService : IAcquisitionService
{
    private readonly IChannelCollector _channelCollector;
    private readonly IDeviceConfigService _deviceConfigService;
    private readonly IHeartbeatMonitor _heartbeatMonitor;
    private readonly IEventRuleCollector _eventRuleCollector;
    private readonly IEventSink _eventSink;
    private readonly string _edgeId;
    private readonly ILogger<AcquisitionService> _logger;
    private readonly IPlcClientLifecycleService _plcLifecycle;
    private readonly ConcurrentDictionary<string, PlcRuntime> _runtimes = new();
    private bool _disposed;
    private int _stopStarted;

    public AcquisitionService(IDeviceConfigService deviceConfigService,
        IPlcClientLifecycleService plcLifecycle,
        ILogger<AcquisitionService> logger,
        IHeartbeatMonitor heartbeatMonitor,
        IChannelCollector channelCollector,
        IEventRuleCollector eventRuleCollector,
        IEventSink eventSink,
        IConfiguration configuration)
    {
        _deviceConfigService = deviceConfigService;
        _plcLifecycle = plcLifecycle;
        _logger = logger;
        _heartbeatMonitor = heartbeatMonitor;
        _channelCollector = channelCollector;
        _eventRuleCollector = eventRuleCollector;
        _eventSink = eventSink;
        _edgeId = configuration["Edge:EdgeId"]?.Trim() ?? Environment.MachineName;

        // 订阅配置变更事件
        _deviceConfigService.ConfigChanged += OnConfigChanged;
    }

    public async Task StartCollectionTasks()
    {
        var configs = await _deviceConfigService.GetConfigs().ConfigureAwait(false);
        foreach (var config in configs.Where(static config => config.IsEnabled))
            TryStartCollectionTask(config);
    }

    public async Task StopCollectionTasks()
    {
        if (Interlocked.Exchange(ref _stopStarted, 1) != 0)
            return;

        try
        {
            foreach (var runtime in _runtimes.Values)
            {
                try
                {
                    await runtime.Cts.CancelAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "取消采集任务失败: {Message}", ex.Message);
                }
            }

            foreach (var runtime in _runtimes.Values)
                await AwaitRuntimeCompletionAsync(runtime).ConfigureAwait(false);

            await _plcLifecycle.CloseAllAsync().ConfigureAwait(false);
        }
        finally
        {
            _runtimes.Clear();
        }
    }

    public async Task<PlcWriteResult> WritePlcAsync(string sourceCode, string address, object value,
        string dataType, CancellationToken ct = default)
    {
        var configs = await _deviceConfigService.GetConfigs().ConfigureAwait(false);
        var config = configs.FirstOrDefault(c =>
            string.Equals(c.SourceCode, sourceCode, StringComparison.OrdinalIgnoreCase));
        if (config == null)
            return new PlcWriteResult
            {
                IsSuccess = false,
                Message = $"未找到数据源 {sourceCode} 的配置"
            };

        var client = _plcLifecycle.GetOrCreateClient(config);
        var result = await PlcWriteDispatcher.WriteAsync(client, address, value, dataType).ConfigureAwait(false);
        if (!result.IsSuccess)
            return result;

        var subject = config.Asset ?? new Ingot.Domain.Events.ObjectRef("source", config.SourceCode);
        var evt = Ingot.Domain.Events.ProductionEvent.Create(
            "parameter.applied",
            DateTimeOffset.UtcNow,
            $"edge/{_edgeId}/{config.SourceCode}/control",
            subject,
            data: new Dictionary<string, object?>
            {
                ["tag"] = address,
                ["dataType"] = dataType,
                ["value"] = value
            });
        await _eventSink.EmitAsync(evt, ct).ConfigureAwait(false);
        return result;
    }

    public IReadOnlyCollection<PlcConnectionStatus> GetPlcConnections()
    {
        return _runtimes.Keys
            .Select(plcCode => _heartbeatMonitor.GetConnectionStatus(plcCode))
            .Where(s => s != null)
            .OrderBy(s => s!.SourceCode)
            .ToList()!;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _deviceConfigService.ConfigChanged -= OnConfigChanged;
        StopCollectionTasks().ConfigureAwait(false).GetAwaiter().GetResult();
    }

    /// <summary>启动单个采集任务，已存在则跳过。</summary>
    private void TryStartCollectionTask(DeviceConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SourceCode))
        {
            _logger.LogError("启动采集任务失败：设备编码为空");
            return;
        }

        if (config.Channels.Count == 0 && config.EventRules.Count == 0)
        {
            _logger.LogError("启动采集任务失败：数据源 {SourceCode} 没有配置采集通道或事件规则", config.SourceCode);
            return;
        }

        if (_runtimes.ContainsKey(config.SourceCode))
            return;

        var runtime = CreateRuntime(config);
        if (!_runtimes.TryAdd(config.SourceCode, runtime))
        {
            runtime.Cts.Dispose();
        }
    }

    /// <summary>配置变更事件处理。事件回调只负责分发，真正逻辑在异步方法中执行。</summary>
    private void OnConfigChanged(object? sender, ConfigChangedEventArgs e)
    {
        _ = HandleConfigChangedAsync(e);
    }

    private async Task HandleConfigChangedAsync(ConfigChangedEventArgs e)
    {
        try
        {
            switch (e.ChangeType)
            {
                case ConfigChangeType.Added:
                    if (e.NewConfig is { IsEnabled: true })
                    {
                        _logger.LogInformation("检测到新数据源配置: {SourceCode}，启动采集任务", e.SourceCode);
                        TryStartCollectionTask(e.NewConfig);
                    }

                    break;

                case ConfigChangeType.Updated:
                    if (e.OldConfig != null)
                        await StopCollectionTaskAsync(e.OldConfig.SourceCode).ConfigureAwait(false);
                    if (e.NewConfig is { IsEnabled: true })
                    {
                        _logger.LogInformation("数据源配置已更新: {SourceCode}，重启采集任务", e.SourceCode);
                        TryStartCollectionTask(e.NewConfig);
                    }

                    break;

                case ConfigChangeType.Removed:
                    if (e.OldConfig != null)
                    {
                        _logger.LogInformation("数据源配置已删除: {SourceCode}，停止采集任务", e.SourceCode);
                        await StopCollectionTaskAsync(e.OldConfig.SourceCode).ConfigureAwait(false);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            try
            {
                _logger.LogError(ex, "处理配置变更失败: {Message}", ex.Message);
            }
            catch
            {
                // 如果日志记录也失败，静默处理（避免崩溃）
                // 在实际生产环境中，可以考虑写入系统事件日志或使用其他故障安全机制
            }
        }
    }

    private PlcRuntime CreateRuntime(DeviceConfig config)
    {
        var cts = new CancellationTokenSource();
        var tasks = BuildRuntimeTasks(config, cts.Token);
        var running = Task.WhenAll(tasks);
        ObserveRuntimeFault(config.SourceCode, running);
        return new PlcRuntime(cts, running);
    }

    private List<Task> BuildRuntimeTasks(DeviceConfig config, CancellationToken cancellationToken)
    {
        var client = _plcLifecycle.GetOrCreateClient(config);
        var tasks = new List<Task>(config.Channels.Count + config.EventRules.Count + 1)
        {
            _heartbeatMonitor.MonitorAsync(config, client, cancellationToken)
        };

        foreach (var channel in config.Channels)
            tasks.Add(_channelCollector.CollectAsync(config, channel, client, cancellationToken));

        foreach (var rule in config.EventRules)
            tasks.Add(_eventRuleCollector.CollectAsync(config, rule, client, cancellationToken));

        return tasks;
    }

    private void ObserveRuntimeFault(string plcCode, Task runningTask)
    {
        _ = runningTask.ContinueWith(task =>
        {
            var ex = task.Exception?.Flatten().InnerException;
            if (ex != null)
                _logger.LogError(ex, "{PlcCode}-采集任务异常: {Message}", plcCode, ex.Message);
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task StopCollectionTaskAsync(string plcCode)
    {
        if (!_runtimes.TryRemove(plcCode, out var runtime)) return;

        try
        {
            await runtime.Cts.CancelAsync().ConfigureAwait(false);
            await AwaitRuntimeCompletionAsync(runtime).ConfigureAwait(false);
            await _plcLifecycle.CloseAsync(plcCode).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止采集任务失败 {PlcCode}: {Message}", plcCode, ex.Message);
        }
    }

    private async Task AwaitRuntimeCompletionAsync(PlcRuntime runtime)
    {
        try
        {
            await runtime.Running.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 预期的取消异常，忽略
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "等待采集任务完成失败: {Message}", ex.Message);
        }
        finally
        {
            runtime.Cts.Dispose();
        }
    }
}
