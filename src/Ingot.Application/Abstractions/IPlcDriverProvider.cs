using System.Collections.Generic;

namespace Ingot.Application.Abstractions;

/// <summary>
///     PLC 驱动提供者。用于将具体通讯库/协议实现注册到工厂中。
/// </summary>
public interface IPlcDriverProvider
{
    /// <summary>
    ///     当前 provider 支持的完整 Driver 名称列表。
    /// </summary>
    IReadOnlyCollection<string> SupportedDrivers { get; }

    /// <summary>
    ///     创建对应的 PLC 客户端实例。
    /// </summary>
    IPlcClientService Create(DeviceConfig config);
}
