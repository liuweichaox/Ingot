namespace Ingot.Contracts.Edge;

public sealed record EdgeRegistrationRequest
{
    public required string EdgeId { get; init; }

    public string? HostBaseUrl { get; init; }

    public string? Hostname { get; init; }

    public string? Version { get; init; }
}
