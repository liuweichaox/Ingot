using Prometheus;

namespace Ingot.Platform.Infrastructure.Webhooks;

public sealed class WebhookMetrics
{
    private readonly Counter _delivered = Metrics.CreateCounter(
        "webhook_delivered_total",
        "Webhook 成功投递的 CloudEvents 数量",
        new CounterConfiguration { LabelNames = ["subscription"] });

    private readonly Counter _failures = Metrics.CreateCounter(
        "webhook_delivery_failures_total",
        "Webhook 投递失败次数",
        new CounterConfiguration { LabelNames = ["subscription"] });

    public void RecordSuccess(Guid subscriptionId)
        => _delivered.WithLabels(subscriptionId.ToString()).Inc();

    public void RecordFailure(Guid subscriptionId)
        => _failures.WithLabels(subscriptionId.ToString()).Inc();
}
