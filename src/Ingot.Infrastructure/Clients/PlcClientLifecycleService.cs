using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;
using Microsoft.Extensions.Logging;

namespace Ingot.Infrastructure.Clients;

/// <summary>
///     Plc 客户端生命周期管理：创建、获取、关闭、清理。
/// </summary>
public class PlcClientLifecycleService : IPlcClientLifecycleService
{
    private readonly ILogger<PlcClientLifecycleService> _logger;
    private readonly IPlcClientFactory _plcClientFactory;
    private readonly ConcurrentDictionary<string, IPlcClientService> _plcClients = new();

    public PlcClientLifecycleService(
        IPlcClientFactory plcClientFactory,
        ILogger<PlcClientLifecycleService> logger)
    {
        _plcClientFactory = plcClientFactory;
        _logger = logger;
    }

    /// <summary>
    ///     获取或创建 Plc 客户端（线程安全）。
    /// </summary>
    public IPlcClientService GetOrCreateClient(DeviceConfig config)
    {
        return _plcClients.GetOrAdd(config.SourceCode, _ => _plcClientFactory.Create(config));
    }

    /// <summary>
    ///     关闭指定设备的 Plc 客户端并清理相关资源。
    /// </summary>
    public async Task CloseAsync(string plcCode)
    {
        if (_plcClients.TryRemove(plcCode, out var client))
            try
            {
                await client.ConnectCloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "关闭 Plc 客户端失败 {PlcCode}: {Message}", plcCode, ex.Message);
            }
    }

    /// <summary>
    ///     关闭所有 Plc 客户端并清理相关资源。
    /// </summary>
    public async Task CloseAllAsync()
    {
        var tasks = new List<Task>();
        var plcCodes = _plcClients.Keys.ToList();

        foreach (var plcCode in plcCodes) tasks.Add(CloseAsync(plcCode));

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
