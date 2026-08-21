using Ingot.Domain.Events;

namespace Ingot.Platform.Application.Events;

public sealed record PlatformEventQuery : EventFilter
{
    public string? SiteId { get; init; }
    public string? EdgeId { get; init; }

    public string? SearchText { get; init; }
    public long? AfterIngestId { get; init; }
    public long? BeforeIngestId { get; init; }
    public int Offset { get; init; }
}
