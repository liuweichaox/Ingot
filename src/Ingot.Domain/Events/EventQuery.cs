namespace Ingot.Domain.Events;

/// <summary>
///     边缘与中心共享的事件查询条件。
/// </summary>
public sealed record EventQuery
{
    public string? EventType { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public long? AfterSeq { get; init; }
    public int Limit { get; init; } = 100;
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();
}
