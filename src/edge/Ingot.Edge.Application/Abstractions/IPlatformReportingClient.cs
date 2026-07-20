using System.Threading;
using System.Threading.Tasks;

namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     Edge → Platform 注册与心跳客户端的契约。
///     宿主的 HostedService 只负责节拍循环；地址归一化、出口 IP 探测、
///     注册重试退避与心跳发送由实现承担。
/// </summary>
public interface IPlatformReportingClient
{
    /// <summary>
    ///     准备客户端（解析 EdgeId、归一化 Agent 回调地址、创建 HTTP 客户端）。
    ///     未启用上报或配置不完整时返回 false，宿主应直接结束循环。
    /// </summary>
    /// <param name="listenUrls">宿主监听地址（用于推导中心代理访问 Edge 的回调地址）。</param>
    bool TryInitialize(string? listenUrls);

    /// <summary>启动即注册；中心不可用时按指数退避持续重试，直到成功或取消。</summary>
    Task RegisterWithRetryAsync(CancellationToken ct = default);

    /// <summary>发送一次心跳（在线状态、错误摘要）。失败不抛出，仅记录。</summary>
    Task SendHeartbeatAsync(CancellationToken ct = default);

    /// <summary>心跳间隔（秒），已做下限保护。</summary>
    int HeartbeatIntervalSeconds { get; }
}
