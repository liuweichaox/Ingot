namespace Ingot.Edge.Infrastructure.Logs;

public class LogOptions
{

    public string DatabasePath { get; set; } = "Data/logs.db";

    public int RetentionDays { get; set; } = 30;
}
