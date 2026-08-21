using Ingot.Domain.Events;

namespace Ingot.Contracts.Events;

public sealed record EventBatchRequest
{

    public required string SiteId { get; init; }

    public required string EdgeId { get; init; }

    public IReadOnlyList<ProductionEvent> Events { get; init; } = [];
}

public sealed record EventBatchResponse
{
    public int Accepted { get; init; }

    public int Duplicates { get; init; }

    public long AckSeq { get; init; }

    public bool GapDetected { get; init; }
}

public sealed record PlatformProductionEvent
{
    public required long IngestId { get; init; }

    public required string SiteId { get; init; }

    public required string EdgeId { get; init; }

    public required DateTimeOffset IngestedAt { get; init; }

    public required ProductionEvent Event { get; init; }
}
