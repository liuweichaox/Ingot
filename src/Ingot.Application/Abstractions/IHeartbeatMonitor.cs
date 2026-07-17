using System.Threading;
using System.Threading.Tasks;

namespace Ingot.Application.Abstractions;

/// <summary>
///     心跳监控器接口。
/// </summary>
public interface IHeartbeatMonitor
{
    /// <summary>
    ///     监控设备心跳状态。
    /// </summary>
    /// <param name="config">设备配置</param>
    /// <param name="client">Plc 通讯客户端</param>
    /// <param name="ct">可选的取消标记</param>
    /// <returns>表示异步操作的任务。</returns>
    Task MonitorAsync(DeviceConfig config, IPlcTypedWriteClient client, CancellationToken ct = default);

    /// <summary>
    ///     获取 Plc 连接状态。
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <param name="isConnected">连接状态（如果存在）</param>
    /// <returns>是否成功获取</returns>
    bool TryGetConnectionHealth(string sourceCode, out bool isConnected);

    /// <summary>
    ///     获取 Plc 连接详细信息。
    /// </summary>
    /// <param name="sourceCode">数据源编码</param>
    /// <returns>连接状态信息，如果不存在则返回 null</returns>
    PlcConnectionStatus? GetConnectionStatus(string sourceCode);
}
