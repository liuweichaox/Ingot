using System;
using System.Collections.Generic;
using Ingot.Application.Abstractions;
using Ingot.Domain.Models;

namespace Ingot.Infrastructure.Clients;

/// <summary>
///     PLC 客户端工厂。
/// </summary>
public class PlcClientFactory : IPlcClientFactory
{
    private readonly Dictionary<string, IPlcDriverProvider> _providersByDriver;

    public PlcClientFactory(IEnumerable<IPlcDriverProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providersByDriver = new Dictionary<string, IPlcDriverProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in providers)
        {
            foreach (var driver in provider.SupportedDrivers)
            {
                if (string.IsNullOrWhiteSpace(driver))
                    throw new InvalidOperationException("检测到空的 Driver 注册名。");

                if (!_providersByDriver.TryAdd(driver.Trim(), provider))
                    throw new InvalidOperationException($"检测到重复的 PLC Driver 注册名: {driver}");
            }
        }
    }

    /// <summary>
    ///     创建 Plc 客户端实例。
    /// </summary>
    public IPlcClientService Create(DeviceConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Driver))
            throw new ArgumentException($"未找到匹配的 PLC 驱动。SourceCode={config.SourceCode}, Driver 不能为空。", nameof(config));

        var driver = config.Driver.Trim();
        if (_providersByDriver.TryGetValue(driver, out var provider))
            return provider.Create(config);

        throw new InvalidOperationException(
            $"未找到匹配的 PLC 驱动。SourceCode={config.SourceCode}, Driver={config.Driver}");
    }
}
