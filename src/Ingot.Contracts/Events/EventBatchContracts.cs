using Ingot.Domain.Events;

namespace Ingot.Contracts.Events;

/// <summary>
///     Edge 向 Central 发送的有序生产事件批次。
/// </summary>
public sealed record EventBatchRequest
{
    public required string EdgeId { get; init; }

    public IReadOnlyList<ProductionEvent> Events { get; init; } = [];
}

/// <summary>
///     Central 对批次的持久化确认。AckSeq 表示此前序号均已安全接收或确认重复。
/// </summary>
public sealed record EventBatchResponse
{
    public int Accepted { get; init; }

    public int Duplicates { get; init; }

    public long AckSeq { get; init; }

    public bool GapDetected { get; init; }
}

/// <summary>
///     中心事件查询返回值。IngestId 是跨 Edge 的中心游标。
/// </summary>
public sealed record CentralProductionEvent
{
    public required long IngestId { get; init; }

    public required string EdgeId { get; init; }

    public required DateTimeOffset IngestedAt { get; init; }

    public required ProductionEvent Event { get; init; }
}
