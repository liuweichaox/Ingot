using System.Net.Http.Headers;
using Ingot.Contracts.Events;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.Webhooks;

public sealed class WebhookDispatcher(
    IHttpClientFactory httpClientFactory,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDispatcher> logger)
{
    public async Task<WebhookDeliveryResult> DeliverAsync(
        WebhookSubscription subscription,
        PlatformProductionEvent item,
        CancellationToken ct = default)
    {
        var body = CloudEventMapper.Serialize(item, options.Value.EventTypePrefix);
        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Endpoint);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/cloudevents+json; charset=utf-8");
        request.Headers.TryAddWithoutValidation("X-Ingot-Subscription-Id", subscription.SubscriptionId.ToString());
        request.Headers.TryAddWithoutValidation("X-Ingot-Event-Id", item.Event.EventId);
        if (!string.IsNullOrEmpty(subscription.Secret))
            request.Headers.TryAddWithoutValidation(
                "X-Ingot-Signature",
                CloudEventMapper.ComputeSignature(body, subscription.Secret));

        try
        {
            var client = httpClientFactory.CreateClient("webhook");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return WebhookDeliveryResult.Success();

            var error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            logger.LogWarning(
                "Webhook 投递失败：SubscriptionId={SubscriptionId}, EventId={EventId}, {Error}",
                subscription.SubscriptionId,
                item.Event.EventId,
                error);
            return WebhookDeliveryResult.Failure(error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(
                ex,
                "Webhook 投递异常：SubscriptionId={SubscriptionId}, EventId={EventId}",
                subscription.SubscriptionId,
                item.Event.EventId);
            return WebhookDeliveryResult.Failure(ex.Message);
        }
    }
}

public readonly record struct WebhookDeliveryResult(bool Succeeded, string? Error)
{
    public static WebhookDeliveryResult Success() => new(true, null);
    public static WebhookDeliveryResult Failure(string error) => new(false, error);
}
