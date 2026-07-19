namespace Ingot.Central.Infrastructure.Events;

public sealed class CentralEventOptions
{
    public bool RequireToken { get; set; } = true;

    public Dictionary<string, string> EdgeTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>允许的 OccurredAt 未来时钟偏移（分钟）。超出则拒收，避免异常时间戳凭空创建远期月度分区。</summary>
    public int MaxFutureSkewMinutes { get; set; } = 60;

    /// <summary>允许的 OccurredAt 最早回填天数。早于该窗口则拒收，避免远古时间戳造成分区膨胀。默认约 10 年。</summary>
    public int MaxPastDays { get; set; } = 3650;
}
