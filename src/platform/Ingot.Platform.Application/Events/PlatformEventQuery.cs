using Ingot.Domain.Events;

namespace Ingot.Platform.Application.Events;

// Add exact configuration identity and receipt-time ordering for evidence lookups.
public sealed record PlatformEventQuery : EventFilter
{
    public string? SiteId { get; init; }
    public string? EdgeId { get; init; }
    public string? EventId { get; init; }
    public IReadOnlyList<string>? ExecutionIds { get; init; }
    public string? AppliedConfigurationKind { get; init; }
    public string? AppliedConfigurationId { get; init; }
    public int? AppliedConfigurationVersion { get; init; }
    public bool OrderByIngestedAtDescending { get; init; }
    public bool RequireNonEmptyDataValues { get; init; }

    public string? SearchText { get; init; }
    public long? AfterIngestId { get; init; }
    public long? BeforeIngestId { get; init; }
    public int Offset { get; init; }
}
