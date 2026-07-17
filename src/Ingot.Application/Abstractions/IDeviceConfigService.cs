using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ingot.Application.Abstractions;

/// <summary>
///     采集配置服务接口。
/// </summary>
public interface IDeviceConfigService
{
    /// <summary>
    ///     获取所有采集表格配置。
    /// </summary>
    /// <returns>包含所有采集表格配置的列表。</returns>
    Task<List<DeviceConfig>> GetConfigs();

    /// <summary>
    ///     配置变更事件，当配置文件发生变化时触发
    /// </summary>
    event EventHandler<ConfigChangedEventArgs>? ConfigChanged;

    /// <summary>
    ///     验证配置是否有效
    /// </summary>
    /// <param name="config">要验证的配置</param>
    /// <returns>验证结果，包含是否有效和错误信息</returns>
    Task<ConfigValidationResult> ValidateConfigAsync(DeviceConfig config);
}
