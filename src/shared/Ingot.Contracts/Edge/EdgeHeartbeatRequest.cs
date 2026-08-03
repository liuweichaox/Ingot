namespace Ingot.Contracts.Edge;

using Ingot.Contracts.Acquisition;

/// <summary>
/// 边缘节点心跳：中心用它更新在线状态与运行指标。
/// </summary>
public sealed record EdgeHeartbeatRequest
{
    public required string EdgeId { get; init; }

    /// <summary>
    /// 可选：Edge.Agent 对中心可达的基础地址（如 http://10.0.0.12:8001）。
    /// 允许在心跳时更新（比如 DHCP/容器重建后地址变化）。
    /// </summary>
    public string? HostBaseUrl { get; init; }

    /// <summary>
    /// 可选：最后一次错误摘要。
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Edge 主动上报的采集运行与配置收敛状态。Platform 不需要反向连接 OT 网络即可查看。
    /// </summary>
    public EdgeAcquisitionRuntimeStatus? Acquisition { get; init; }

    /// <summary>Edge outbox 积压、确认、失败与恢复状态。</summary>
    public EdgeDeliveryRuntimeStatus? Delivery { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
