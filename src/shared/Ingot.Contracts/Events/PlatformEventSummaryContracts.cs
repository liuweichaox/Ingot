// 提供受站点授权的业务事件摘要契约，不暴露原始审计载荷。
namespace Ingot.Contracts.Events;

public sealed record PlatformEventSummaryResponse
{
    public IReadOnlyList<PlatformEventSummaryItem> Data { get; init; } = [];

    public long Total { get; init; }
}

public sealed record PlatformEventSummaryItem
{
    public long IngestId { get; init; }

    public required ProductionEventSummary Event { get; init; }
}

public sealed record ProductionEventSummary
{
    public required string EventType { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    public required EventSummarySubject Subject { get; init; }

    public string? ExecutionId { get; init; }
}

public sealed record EventSummarySubject
{
    public required string Id { get; init; }
}
