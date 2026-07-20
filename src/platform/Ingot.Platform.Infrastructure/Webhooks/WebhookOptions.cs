namespace Ingot.Platform.Infrastructure.Webhooks;

public sealed class WebhookOptions
{
    public bool Enabled { get; set; } = true;

    public int PollIntervalMs { get; set; } = 1_000;

    public int BatchSize { get; set; } = 100;

    public int RequestTimeoutSeconds { get; set; } = 15;

    public string EventTypePrefix { get; set; } = "com.ingot";
}
