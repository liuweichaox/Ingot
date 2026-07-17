namespace Ingot.Application.Abstractions;

/// <summary>
///     通讯客户端工厂。
/// </summary>
public interface IPlcClientFactory
{
    /// <summary>
    ///     创建对应协议的通讯客户端。
    /// </summary>
    /// <param name="config">设备配置</param>
    /// <returns>通讯客户端</returns>
    IPlcClientService Create(DeviceConfig config);
}
