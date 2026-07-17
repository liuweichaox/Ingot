using System.Threading.Tasks;

namespace Ingot.Application.Abstractions;

/// <summary>
///     Plc 客户端生命周期管理：创建、获取、关闭、清理。
/// </summary>
public interface IPlcClientLifecycleService
{
    /// <summary>
    ///     获取或创建 Plc 客户端。
    /// </summary>
    /// <param name="config">设备配置</param>
    /// <returns>Plc 客户端实例</returns>
    IPlcClientService GetOrCreateClient(DeviceConfig config);

    /// <summary>
    ///     关闭指定设备的 Plc 客户端并清理相关资源。
    /// </summary>
    /// <param name="plcCode">Plc编码</param>
    Task CloseAsync(string plcCode);

    /// <summary>
    ///     关闭所有 Plc 客户端并清理相关资源。
    /// </summary>
    Task CloseAllAsync();
}