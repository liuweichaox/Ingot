using System.Text.RegularExpressions;

namespace Ingot.Contracts.Events;

/// <summary>
///     Edge 与 Central 共用的事件查询协议边界。
/// </summary>
public static partial class EventQueryContractValidator
{
    public static bool TryValidate(
        DateTimeOffset? from,
        DateTimeOffset? to,
        long? cursor,
        int limit,
        IReadOnlyDictionary<string, string>? context,
        out string error)
    {
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return Fail("from 不能晚于 to。", out error);
        if (cursor is < 0)
            return Fail("游标不能小于 0。", out error);
        if (limit is < 1 or > 500)
            return Fail("limit 必须在 1 到 500 之间。", out error);
        if (context is null)
            return Fail("上下文过滤不能为空。", out error);
        if (context.Keys.Any(static key => !ContextKeyPattern().IsMatch(key)))
        {
            return Fail(
                "ctx.<key> 中的 key 只能包含字母、数字、点、下划线和连字符，长度为 1 到 128。",
                out error);
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    ///     基座重载：校验共享过滤字段 + 派生类型自带的游标。
    ///     Edge 传 AfterSeq，Central 传 AfterIngestId。
    /// </summary>
    public static bool TryValidate(
        Ingot.Domain.Events.EventFilter filter,
        long? cursor,
        out string error)
        => TryValidate(filter.From, filter.To, cursor, filter.Limit, filter.Context, out error);

    /// <summary>
    ///     ctx.&lt;key&gt; 合法性的唯一出处：字母、数字、点、下划线、连字符，长度 1-128。
    ///     存储层与查询层都应引用本方法，避免规则漂移。
    /// </summary>
    public static bool IsValidContextKey(string key) => ContextKeyPattern().IsMatch(key);

    public static bool TryParseCursor(string? value, out long? cursor)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            cursor = null;
            return true;
        }

        if (long.TryParse(value, out var parsed) && parsed >= 0)
        {
            cursor = parsed;
            return true;
        }

        cursor = null;
        return false;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    [GeneratedRegex(
        "^[A-Za-z0-9_.-]{1,128}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ContextKeyPattern();
}
