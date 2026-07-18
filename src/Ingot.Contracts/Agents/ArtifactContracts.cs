using System.Text.Json;

namespace Ingot.Contracts.Agents;

public static class AgentArtifactKinds
{
    public const string ConnectorSpecification = "connector-specification";
    public const string ConnectorPackage = "connector-package";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        ConnectorSpecification,
        ConnectorPackage
    };
}

/// <summary>
/// A versioned platform-owned artifact. Agents may create these records but never choose a filesystem path.
/// </summary>
public sealed record AgentArtifact
{
    public required string ArtifactId { get; init; }
    public required string ActorId { get; init; }
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Format { get; init; }
    public required string Content { get; init; }
    public required int Version { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? RunId { get; init; }
    public JsonElement? Metadata { get; init; }
}

public static class ConnectorSpecificationStatuses
{
    public const string NeedsInput = "needs-input";
    public const string ReadyForBuild = "ready-for-build";
}

/// <summary>
/// Protocol-neutral acquisition contract authored through an Agent conversation.
/// No protocol SDK or device driver is embedded in Ingot.
/// </summary>
public sealed record ConnectorSpecification
{
    public required string Name { get; init; }
    public required string SourceCode { get; init; }
    public required string Protocol { get; init; }
    public required string Endpoint { get; init; }
    public string Authentication { get; init; } = "none";
    public string DataContract { get; init; } = string.Empty;
    public string SamplingPolicy { get; init; } = string.Empty;
    public string SuccessCriteria { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedNetworkTargets { get; init; } = [];
    public IReadOnlyList<string> MissingConditions { get; init; } = [];
    public string Status { get; init; } = ConnectorSpecificationStatuses.NeedsInput;
}
