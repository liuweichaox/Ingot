using Ingot.Platform.Infrastructure.Events;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.Webhooks;

public sealed class WebhookDeliveryHostedService(
    IWebhookSubscriptionStore subscriptions,
    IPlatformEventStore events,
    WebhookDispatcher dispatcher,
    WebhookMetrics metrics,
    IOptions<WebhookOptions> options,
    ILogger<WebhookDeliveryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Webhook 投递已禁用（Webhook:Enabled=false）");
            return;
        }

        var delay = TimeSpan.FromMilliseconds(Math.Clamp(options.Value.PollIntervalMs, 100, 60_000));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Webhook 投递轮询失败");
            }

            await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
        }
    }

    public async Task RunOnceAsync(CancellationToken ct = default)
    {
        var batchSize = Math.Clamp(options.Value.BatchSize, 1, 500);
        var active = (await subscriptions.ListAsync(ct).ConfigureAwait(false))
            .Where(static item => item.Enabled)
            .ToArray();

        foreach (var subscription in active)
        {
            var batch = await events.QueryAsync(
                    new PlatformEventQuery
                    {
                        AfterIngestId = subscription.Cursor,
                        Limit = batchSize
                    },
                    ct)
                .ConfigureAwait(false);

            foreach (var item in batch.OrderBy(static item => item.IngestId))
            {
                if (!WebhookSubscriptionMatcher.Matches(subscription, item))
                {
                    await subscriptions.AdvanceAsync(subscription.SubscriptionId, item.IngestId, ct)
                        .ConfigureAwait(false);
                    continue;
                }

                var result = await dispatcher.DeliverAsync(subscription, item, ct).ConfigureAwait(false);
                if (!result.Succeeded)
                {
                    await subscriptions.RecordFailureAsync(
                            subscription.SubscriptionId,
                            result.Error ?? "未知 Webhook 投递错误",
                            ct)
                        .ConfigureAwait(false);
                    RecordFailureMetric(subscription.SubscriptionId);
                    break;
                }

                await subscriptions.AdvanceAsync(subscription.SubscriptionId, item.IngestId, ct)
                    .ConfigureAwait(false);
                RecordSuccessMetric(subscription.SubscriptionId);
            }
        }
    }

    private void RecordSuccessMetric(Guid subscriptionId)
    {
        try
        {
            metrics.RecordSuccess(subscriptionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Webhook 已成功投递且游标已推进，但记录成功指标失败：SubscriptionId={SubscriptionId}",
                subscriptionId);
        }
    }

    private void RecordFailureMetric(Guid subscriptionId)
    {
        try
        {
            metrics.RecordFailure(subscriptionId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Webhook 失败状态已持久化，但记录失败指标失败：SubscriptionId={SubscriptionId}",
                subscriptionId);
        }
    }
}
