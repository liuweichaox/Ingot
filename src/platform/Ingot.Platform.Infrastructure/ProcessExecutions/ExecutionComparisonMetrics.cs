using Ingot.Contracts.Events;
using Prometheus;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public sealed class ExecutionComparisonMetrics
{
    private readonly Counter readinessModes = Metrics.CreateCounter(
        "ingot_execution_comparison_readiness_total",
        "Completed execution comparisons by readiness mode.",
        new CounterConfiguration { LabelNames = ["mode"] });

    private readonly Counter blockingReasons = Metrics.CreateCounter(
        "ingot_execution_comparison_blocking_reason_total",
        "Structured reasons that prevented stronger execution comparison analysis.",
        new CounterConfiguration { LabelNames = ["reason"] });

    public void Observe(ExecutionAnalysisReadiness readiness)
    {
        readinessModes.WithLabels(readiness.Mode).Inc();
        foreach (var reason in readiness.BlockingReasons.Distinct(StringComparer.Ordinal))
            blockingReasons.WithLabels(reason).Inc();
    }
}
