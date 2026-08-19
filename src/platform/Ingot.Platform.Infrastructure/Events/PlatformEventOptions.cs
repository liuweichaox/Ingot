namespace Ingot.Platform.Infrastructure.Events;

public sealed class PlatformEventOptions
{
    public bool RequireToken { get; set; } = true;

    public Dictionary<string, string> EdgeTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>EdgeId 到 SiteId 的唯一生产归属；请求中的 SiteId 必须与该映射一致。</summary>
    public Dictionary<string, string> EdgeSites { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>允许的 OccurredAt 未来时钟偏移（分钟）。超出则拒收，避免异常时间戳凭空创建远期月度分区。</summary>
    public int MaxFutureSkewMinutes { get; set; } = 60;

    /// <summary>允许的 OccurredAt 最早回填天数。早于该窗口则拒收，避免远古时间戳造成分区膨胀。默认约 10 年。</summary>
    public int MaxPastDays { get; set; } = 3650;

    /// <summary>保留天数：&gt;0 时由 Worker 成对清理超期事件、采样帧和值；0 表示不启用。</summary>
    public int RetentionDays { get; set; }

    /// <summary>
    ///     event_ingest_keys 幂等键保留天数：&gt;0 时启用每日修剪（下限 30 天，建议不小于 RetentionDays）；
    ///     0 表示不清理（键表将随事件量持续增长）。窗口必须覆盖边缘端最大补传跨度，否则超窗重放无法去重。
    /// </summary>
    public int KeyRetentionDays { get; set; }
}
