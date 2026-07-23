namespace Ingot.Edge.Application.Options;

/// <summary>
/// Edge 节点向 Platform 上报（注册/心跳）的配置项。
/// </summary>
public sealed class EdgeReportingOptions
{
    /// <summary>
    /// 是否启用向 Platform 注册/心跳上报。
    /// </summary>
    public bool? EnablePlatformReporting { get; init; }

    /// <summary>
    /// Platform API 基地址。
    /// </summary>
    public string PlatformApiBaseUrl { get; init; } = string.Empty;

    /// <summary>兼容旧版配置；新部署应使用 EnablePlatformReporting。</summary>
    public bool EnableCentralReporting { get; init; } = true;

    /// <summary>兼容旧版配置；新部署应使用 PlatformApiBaseUrl。</summary>
    public string CentralApiBaseUrl { get; init; } = string.Empty;

    public bool IsPlatformReportingEnabled => EnablePlatformReporting ?? EnableCentralReporting;

    public string EffectivePlatformApiBaseUrl =>
        string.IsNullOrWhiteSpace(PlatformApiBaseUrl) ? CentralApiBaseUrl : PlatformApiBaseUrl;

    /// <summary>
    /// Platform 回访本节点时使用的地址。跨容器、NAT 或反向代理部署时应显式配置，
    /// 避免把本机回环地址或临时容器 IP 注册到平台。
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary>
    /// 可选：固定 EdgeId。为空时会从 IdentityFilePath 读取/生成并持久化。
    /// </summary>
    public string? EdgeId { get; init; }

    /// <summary>
    /// 心跳间隔（秒）。
    /// </summary>
    public int HeartbeatIntervalSeconds { get; init; } = 10;

    /// <summary>是否把本地生产事件 outbox 复制到 Platform。</summary>
    public bool EnableEventShipping { get; init; } = true;

    /// <summary>与 Platform EventIngest:EdgeTokens 对应的 bearer token。</summary>
    public string? EventIngestToken { get; init; }

    /// <summary>单次上行事件数量，服务端上限为 500。</summary>
    public int EventBatchSize { get; init; } = 500;

    /// <summary>outbox 为空时的轮询间隔。</summary>
    public int EventIdleDelayMs { get; init; } = 1000;

    /// <summary>失败指数退避上限。</summary>
    public int EventRetryMaxSeconds { get; init; } = 60;
}
