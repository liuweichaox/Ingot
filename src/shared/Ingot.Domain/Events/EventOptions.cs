namespace Ingot.Domain.Events;

public sealed class EventOptions
{
    public string DatabasePath { get; set; } = "Data/events.db";

    public int RetentionDays { get; set; } = 7;

    public int CleanupIntervalSeconds { get; set; } = 3600;

    public int MaxBacklogRows { get; set; } = 500_000;

}
