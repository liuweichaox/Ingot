using System.Text.RegularExpressions;
using Ingot.Domain.Events;

namespace Ingot.Contracts.Events;

public static partial class EventBatchValidator
{
    public static bool TryValidate(
        EventBatchRequest? request,
        out EventBatchRequest? normalized,
        out string error)
    {
        normalized = null;
        if (request is null)
            return Fail("请求不能为空。", out error);

        var edgeId = request.EdgeId?.Trim();
        if (string.IsNullOrWhiteSpace(edgeId) || !EdgeIdPattern().IsMatch(edgeId))
        {
            return Fail(
                "EdgeId 只能包含字母、数字、点、下划线和连字符，长度为 1 到 128。",
                out error);
        }
        if (request.Events is null || request.Events.Count is < 1 or > 500)
            return Fail("Events 每批必须包含 1 到 500 条事件。", out error);

        var ordered = request.Events
            .OrderBy(static evt => evt?.Seq ?? long.MinValue)
            .ToArray();
        for (var index = 0; index < ordered.Length; index++)
        {
            if (!ProductionEventValidator.TryValidate(
                    ordered[index],
                    requirePersistedSequence: true,
                    out var eventError))
            {
                return Fail($"Events[{index}] 无效：{eventError}", out error);
            }
        }

        if (ordered.Select(static evt => evt.Seq).Distinct().Count() != ordered.Length)
            return Fail("同一批次不能包含重复 Seq。", out error);
        if (ordered.Select(static evt => evt.EventId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != ordered.Length)
        {
            return Fail("同一批次不能包含重复 EventId。", out error);
        }

        var expectedSourcePrefix = $"edge/{edgeId}/";
        if (ordered.Any(evt =>
                !evt.Source.StartsWith(expectedSourcePrefix, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail($"事件 Source 必须以 {expectedSourcePrefix} 开头。", out error);
        }

        normalized = request with
        {
            EdgeId = edgeId,
            Events = ordered
        };
        error = string.Empty;
        return true;
    }

    private static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }

    [GeneratedRegex(
        "^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EdgeIdPattern();
}
