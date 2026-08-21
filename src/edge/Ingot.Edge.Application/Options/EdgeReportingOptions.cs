namespace Ingot.Edge.Application.Options;

public sealed class EdgeReportingOptions
{

    public string SiteId { get; init; } = string.Empty;

    public bool EnablePlatformReporting { get; init; } = true;

    public string PlatformApiBaseUrl { get; init; } = string.Empty;

    public string? PublicBaseUrl { get; init; }

    public string? EdgeId { get; init; }

    public int HeartbeatIntervalSeconds { get; init; } = 10;

    public bool EnableEventShipping { get; init; } = true;

    public string? EventIngestToken { get; init; }

    public int EventBatchSize { get; init; } = 500;

    public int EventIdleDelayMs { get; init; } = 1000;

    public int EventRetryMaxSeconds { get; init; } = 60;
}
