using Ingot.Contracts.Events;

namespace Ingot.Central.Infrastructure.Events;

/// <summary>
///     中心侧摄入时间窗策略。把 OccurredAt 限制在合理区间内：既允许正常回填，
///     又防止异常或恶意的时间戳凭空创建大量月度分区（分区膨胀 / 轻量 DoS）。
/// </summary>
public static class CentralIngestWindow
{
    public static bool TryValidate(
        EventBatchRequest request,
        CentralEventOptions options,
        DateTimeOffset now,
        out string error)
    {
        var maxFuture = now.AddMinutes(Math.Max(0, options.MaxFutureSkewMinutes));
        var minPast = now.AddDays(-Math.Max(0, options.MaxPastDays));
        foreach (var evt in request.Events)
        {
            if (evt.OccurredAt > maxFuture)
            {
                error = $"事件 OccurredAt 超出允许的未来时间窗（EventId={evt.EventId}）。";
                return false;
            }

            if (evt.OccurredAt < minPast)
            {
                error = $"事件 OccurredAt 早于允许的最早时间窗（EventId={evt.EventId}）。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
