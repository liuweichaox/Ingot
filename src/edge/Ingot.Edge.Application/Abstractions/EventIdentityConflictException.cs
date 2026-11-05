// 表示同一事件身份被用于不同业务内容，调用方必须修复冲突而非重试。
namespace Ingot.Edge.Application.Abstractions;

/// <summary>同一事件身份已用于不同业务内容，不能作为重试接受。</summary>
public sealed class EventIdentityConflictException(string eventId)
    : Exception($"事件 {eventId} 的标识已用于不同内容。")
{
    public string EventId { get; } = eventId;
}
