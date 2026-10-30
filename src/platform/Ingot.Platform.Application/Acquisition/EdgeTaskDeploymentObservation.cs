// Describes bounded Platform observations of an Edge-reported applied task version.
namespace Ingot.Platform.Application.Acquisition;

/// <summary>One continuous sequence of heartbeats reporting the same applied task identity.</summary>
public sealed record EdgeTaskDeploymentObservation
{
    public required string EdgeId { get; init; }
    public required string TaskId { get; init; }
    public int TaskVersion { get; init; }
    public required string ConfigurationHash { get; init; }
    /// <summary>Configuration source on the first Platform observation; source changes do not split an applied identity.</summary>
    public required string ConfigurationSource { get; init; }
    public required DateTimeOffset FirstObservedAt { get; init; }
    public required DateTimeOffset LastObservedAt { get; init; }
    public DateTimeOffset? ClosedObservedAt { get; init; }
    public DateTimeOffset? FirstEdgeReportedAt { get; init; }
    public DateTimeOffset? LastEdgeReportedAt { get; init; }
    public DateTimeOffset? EdgeAppliedAtHint { get; init; }
    public string? LastDeploymentState { get; init; }
}

/// <summary>Bounded result of reading applied-version observations within one authorized site.</summary>
public sealed record EdgeTaskDeploymentObservationQueryResult(
    IReadOnlyList<EdgeTaskDeploymentObservation> Observations,
    bool Truncated);

/// <summary>Reads persisted observations without treating them as proof of continuous deployment or causality.</summary>
public interface IEdgeTaskDeploymentHistoryReader
{
    Task<EdgeTaskDeploymentObservationQueryResult> FindOverlappingAsync(
        string siteId,
        IReadOnlyCollection<string> edgeIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default);
}
