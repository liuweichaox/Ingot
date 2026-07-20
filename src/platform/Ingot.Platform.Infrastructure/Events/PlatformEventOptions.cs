namespace Ingot.Platform.Infrastructure.Events;

public sealed class PlatformEventOptions
{
    public bool RequireToken { get; set; } = true;

    public Dictionary<string, string> EdgeTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>允许的 OccurredAt 未来时钟偏移（分钟）。超出则拒收，避免异常时间戳凭空创建远期月度分区。</summary>
    public int MaxFutureSkewMinutes { get; set; } = 60;

    /// <summary>允许的 OccurredAt 最早回填天数。早于该窗口则拒收，避免远古时间戳造成分区膨胀。默认约 10 年。</summary>
    public int MaxPastDays { get; set; } = 3650;

    /// <summary>TimescaleDB hypertable 的分块时间间隔（Postgres INTERVAL 字面量，如 "30 days"、"1 month"）。</summary>
    public string ChunkTimeInterval { get; set; } = "30 days";

    /// <summary>保留天数：&gt;0 时注册 add_retention_policy，自动丢弃超期数据块；0 表示不启用。</summary>
    public int RetentionDays { get; set; }

    /// <summary>压缩阈值天数：&gt;0 时启用块级列式压缩并注册 add_compression_policy；0 表示不启用。</summary>
    public int CompressAfterDays { get; set; }
}
