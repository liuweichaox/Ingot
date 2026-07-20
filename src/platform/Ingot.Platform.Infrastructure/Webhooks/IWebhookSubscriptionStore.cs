namespace Ingot.Platform.Infrastructure.Webhooks;

public interface IWebhookSubscriptionStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<WebhookSubscription> CreateAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken ct = default);
    Task<WebhookSubscription?> GetAsync(Guid subscriptionId, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid subscriptionId, CancellationToken ct = default);
    Task<bool> SetEnabledAsync(Guid subscriptionId, bool enabled, CancellationToken ct = default);
    Task AdvanceAsync(Guid subscriptionId, long ingestId, CancellationToken ct = default);
    Task RecordFailureAsync(Guid subscriptionId, string error, CancellationToken ct = default);
}
