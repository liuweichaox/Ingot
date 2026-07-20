namespace Ingot.Domain.Events;

/// <summary>
///     事件查询的共享过滤基座：Edge 与 Platform 的查询记录都从这里派生。
///     游标语义刻意留在派生类型上（Edge 用本地单调序号 AfterSeq，
///     Platform 用摄入序号 AfterIngestId），两者不可互换。
///     新增过滤字段应加在这里，避免两侧漂移。
/// </summary>
public abstract record EventFilter
{
    public string? EventType { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 100;
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();
}
