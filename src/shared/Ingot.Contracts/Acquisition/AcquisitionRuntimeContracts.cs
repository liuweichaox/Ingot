// Defines generic Edge acquisition health reported through the heartbeat contract.
namespace Ingot.Contracts.Acquisition;

/// <summary>Generic runtime counters and timestamps reported by an Edge adapter.</summary>
public sealed record EdgeAcquisitionRuntimeStatus(
    bool Enabled,
    string State,
    DateTimeOffset ReportedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastReadSuccessAt,
    DateTimeOffset? LastValidSnapshotAt,
    long ReadSuccessCount,
    long ValidSnapshotCount,
    long EmittedEventCount,
    long DuplicateSuppressionCount,
    long InactiveSnapshotCount,
    long SourceIdentityStallCount,
    double? LastReadDurationMs,
    double? ObservedIntervalMs,
    string? ActiveProcessSpecification,
    string? LastError,
    long StaleSnapshotRejectionCount = 0,
    long StaleValueRejectionCount = 0,
    DateTimeOffset? PlatformReceivedAt = null,
    DateTimeOffset? PlatformObservedAt = null,
    bool SupportsSampleEventIdCorrelation = false);
