using System.Text.RegularExpressions;

namespace Ingot.Domain.Events;

/// <summary>
///     生产事件信封的领域边界校验。反序列化可以绕过 <see cref="ProductionEvent.Create" />，
///     因此任何外部摄入入口都必须显式调用此校验器。
/// </summary>
public static partial class ProductionEventValidator
{
    public static bool TryValidate(
        ProductionEvent? evt,
        bool requirePersistedSequence,
        out string error)
    {
        if (evt is null)
            return Fail("事件不能为空。", out error);
        if (!Guid.TryParse(evt.EventId, out var eventId) || eventId.Version != 7)
            return Fail("EventId 必须是 UUIDv7。", out error);
        if (string.IsNullOrWhiteSpace(evt.EventType) ||
            !EventTypePattern().IsMatch(evt.EventType))
        {
            return Fail("EventType 必须使用小写点分格式，例如 cycle.completed。", out error);
        }
        if (evt.EventTypeVersion <= 0)
            return Fail("EventTypeVersion 必须大于 0。", out error);
        if (evt.OccurredAt == default)
            return Fail("OccurredAt 不能为空。", out error);
        if (evt.RecordedAt == default)
            return Fail("RecordedAt 不能为空。", out error);
        if (string.IsNullOrWhiteSpace(evt.Source))
            return Fail("Source 不能为空。", out error);
        if (evt.Subject is null ||
            string.IsNullOrWhiteSpace(evt.Subject.Type) ||
            string.IsNullOrWhiteSpace(evt.Subject.Id))
        {
            return Fail("Subject.Type 和 Subject.Id 不能为空。", out error);
        }
        if (evt.Context is null)
            return Fail("Context 不能为空。", out error);
        if (evt.Context.Any(static pair =>
                string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null))
        {
            return Fail("Context 不能包含空键或 null 值。", out error);
        }
        if (evt.Data is null)
            return Fail("Data 不能为空。", out error);
        if (evt.CorrelationId is not null && string.IsNullOrWhiteSpace(evt.CorrelationId))
            return Fail("CorrelationId 不能是空白字符串。", out error);
        if (requirePersistedSequence && evt.Seq <= 0)
            return Fail("Seq 必须大于 0。", out error);
        if (!requirePersistedSequence && evt.Seq < 0)
            return Fail("Seq 不能小于 0。", out error);

        error = string.Empty;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    [GeneratedRegex(
        "^[a-z][a-z0-9_]*(?:\\.[a-z][a-z0-9_]*)+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EventTypePattern();
}
