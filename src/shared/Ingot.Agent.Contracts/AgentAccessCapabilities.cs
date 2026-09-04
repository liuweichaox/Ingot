namespace Ingot.Contracts.Agents;

public sealed record PageContextRef
{
    public required string Kind { get; init; }

    public required string Id { get; init; }
}

public sealed record AgentCapabilities
{
    public required string EntryPoint { get; init; }

    public required string Purpose { get; init; }

    public required bool Enabled { get; init; }

    public required bool CombinedAnalysisEnabled { get; init; }

    public required string Provider { get; init; }

    public required string FastModel { get; init; }

    public required string ReasoningModel { get; init; }

    public required bool IsDeterministic { get; init; }

    public IReadOnlyList<string> Modes { get; init; } = [];

    public IReadOnlyList<string> Roles { get; init; } = [];

    public IReadOnlyList<AgentToolCapability> Tools { get; init; } = [];

    public required int MaxToolCalls { get; init; }

    public required int MaxRunSeconds { get; init; }

    public required int MaxDiscussionRounds { get; init; }

    public required int MaxDiscussionTurns { get; init; }
}

public sealed record AgentToolCapability
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required string Description { get; init; }

    public required string EntryPoint { get; init; }

    public required string Purpose { get; init; }

    public string Access { get; init; } = "read";
}

public sealed record AgentRunAccessScopeSnapshot
{
    public const int CurrentVersion = 1;

    public int Version { get; init; } = CurrentVersion;

    public bool AllowAllSites { get; init; }

    public IReadOnlyList<string> SiteIds { get; init; } = [];
}
