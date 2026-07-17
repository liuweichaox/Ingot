namespace Ingot.Edge.Agent.Services;

/// <summary>
/// Edge 节点向中心上报（注册/心跳）的配置项。
/// </summary>
public sealed class EdgeReportingOptions
{
    /// <summary>
    /// 是否启用向中心注册/心跳上报。
    /// </summary>
    public bool EnableCentralReporting { get; init; } = true;

    /// <summary>
    /// 中心 API 基地址（例如 http://localhost:8000）。
    /// </summary>
    public string CentralApiBaseUrl { get; init; } = "http://localhost:8000";

    /// <summary>
    /// 可选：固定 EdgeId。为空时会从 IdentityFilePath 读取/生成并持久化。
    /// </summary>
    public string? EdgeId { get; init; }

    /// <summary>
    /// 心跳间隔（秒）。
    /// </summary>
    public int HeartbeatIntervalSeconds { get; init; } = 10;

    /// <summary>是否把本地生产事件 outbox 复制到中心。</summary>
    public bool EnableEventShipping { get; init; } = true;

    /// <summary>与 Central EventIngest:EdgeTokens 对应的 bearer token。</summary>
    public string? EventIngestToken { get; init; }

    /// <summary>单次上行事件数量，服务端上限为 500。</summary>
    public int EventBatchSize { get; init; } = 500;

    /// <summary>outbox 为空时的轮询间隔。</summary>
    public int EventIdleDelayMs { get; init; } = 1000;

    /// <summary>失败指数退避上限。</summary>
    public int EventRetryMaxSeconds { get; init; } = 60;
}
