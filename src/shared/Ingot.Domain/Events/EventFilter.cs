// 定义事件查询在逻辑 Subject 与物理 Equipment 上可独立使用的过滤字段。

namespace Ingot.Domain.Events;

public abstract record EventFilter
{
    public string? EventType { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? EquipmentId { get; init; }
    public string? ExecutionId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 100;
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();
}
