namespace Ingot.Contracts.Edge;

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
    public string? AgentBaseUrl { get; init; }

    /// <summary>
    /// 可选：最后一次错误摘要。
    /// </summary>
    public string? LastError { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
