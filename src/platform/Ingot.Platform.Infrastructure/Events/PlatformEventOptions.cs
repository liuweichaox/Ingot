namespace Ingot.Platform.Infrastructure.Events;

public sealed class PlatformEventOptions
{
    public bool RequireToken { get; set; } = true;

    public Dictionary<string, string> EdgeTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> EdgeSites { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public int MaxFutureSkewMinutes { get; set; } = 60;

    public int MaxPastDays { get; set; } = 3650;

    public int RetentionDays { get; set; }

    public int KeyRetentionDays { get; set; }
}
