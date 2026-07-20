namespace Ingot.Platform.Infrastructure.Webhooks;

public sealed class WebhookStoreInitializerHostedService(
    IWebhookSubscriptionStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
