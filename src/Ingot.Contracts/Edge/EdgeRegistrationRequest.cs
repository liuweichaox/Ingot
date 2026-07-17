namespace Ingot.Contracts.Edge;

/// <summary>
/// 边缘节点注册（或更新）请求：用于让中心“认识”这个采集节点。
/// </summary>
public sealed record EdgeRegistrationRequest
{
    public required string EdgeId { get; init; }

    /// <summary>
    /// 可选：Edge.Agent 对中心可达的基础地址（如 http://10.0.0.12:8001）。
    /// 用于中心侧代理查询 edge 的 metrics / logs。
    /// </summary>
    public string? AgentBaseUrl { get; init; }

    /// <summary>
    /// 可选：机器名/容器名/实例标识（便于排障）。
    /// </summary>
    public string? Hostname { get; init; }

    /// <summary>
    /// 可选：边缘程序版本。
    /// </summary>
    public string? Version { get; init; }
}

