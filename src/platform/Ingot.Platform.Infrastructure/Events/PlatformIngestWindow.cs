using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.Events;

public static class PlatformIngestWindow
{
    public static bool TryValidate(
        EventBatchRequest request,
        PlatformEventOptions options,
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
