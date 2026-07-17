using Ingot.Contracts.Events;

namespace Ingot.Central.Api.Webhooks;

public static class WebhookSubscriptionMatcher
{
    public static bool Matches(WebhookSubscription subscription, CentralProductionEvent item)
    {
        var evt = item.Event;
        if (subscription.EventTypes.Count > 0 &&
            !subscription.EventTypes.Contains(evt.EventType, StringComparer.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(subscription.SubjectType) &&
            !string.Equals(subscription.SubjectType, evt.Subject.Type, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(subscription.SubjectId) &&
            !string.Equals(subscription.SubjectId, evt.Subject.Id, StringComparison.OrdinalIgnoreCase))
            return false;

        return subscription.Context.All(pair =>
            evt.Context.TryGetValue(pair.Key, out var value) &&
            string.Equals(value, pair.Value, StringComparison.Ordinal));
    }
}
