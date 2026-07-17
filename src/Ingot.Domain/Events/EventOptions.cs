namespace Ingot.Domain.Events;

/// <summary>
///     边缘事件日志配置。
/// </summary>
public sealed class EventOptions
{
    public string DatabasePath { get; set; } = "Data/events.db";

    public int RetentionDays { get; set; } = 7;

    public int CleanupIntervalSeconds { get; set; } = 3600;

    public int MaxBacklogRows { get; set; } = 500_000;

    public bool EnableInfluxProjection { get; set; } = true;
}
