// Exposes bounded workload, duration, and failure telemetry for freshness evaluation.
using System.Globalization;
using Ingot.Platform.Infrastructure.Services;

namespace Ingot.Platform.Worker;

public sealed class EdgeTaskFreshnessWorkerMetrics
{
    private static readonly double[] DurationBucketBoundsSeconds = [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30];
    private readonly object _gate = new();
    private readonly long[] _durationBuckets = new long[DurationBucketBoundsSeconds.Length];
    private long _evaluationCount;
    private long _errorCount;
    private long _durationSumTicks;
    private long _lastDurationTicks;
    private long _lastEdgeCount;
    private long _lastTaskCount;
    private long _lastTransitionCount;
    private long _lastSuccessTimestampSeconds;
    private long _lastErrorTimestampSeconds;

    public void RecordEvaluation(
        EdgeTaskFreshnessEvaluationSummary summary,
        TimeSpan duration,
        DateTimeOffset completedAt)
    {
        var durationTicks = Math.Max(0, duration.Ticks);
        var durationSeconds = durationTicks / (double)TimeSpan.TicksPerSecond;
        lock (_gate)
        {
            _evaluationCount++;
            _durationSumTicks += durationTicks;
            _lastDurationTicks = durationTicks;
            _lastEdgeCount = Math.Max(0, summary.EdgeCount);
            _lastTaskCount = Math.Max(0, summary.TaskCount);
            _lastTransitionCount = Math.Max(0, summary.TransitionCount);
            _lastSuccessTimestampSeconds = completedAt.ToUnixTimeSeconds();
            for (var index = 0; index < DurationBucketBoundsSeconds.Length; index++)
            {
                if (durationSeconds <= DurationBucketBoundsSeconds[index])
                    _durationBuckets[index]++;
            }
        }
    }

    public void RecordFailure(DateTimeOffset failedAt)
    {
        lock (_gate)
        {
            _errorCount++;
            _lastErrorTimestampSeconds = failedAt.ToUnixTimeSeconds();
        }
    }

    public string RenderPrometheus()
    {
        lock (_gate)
        {
            var builder = new System.Text.StringBuilder();
            AppendCounter(builder,
                "ingot_edge_task_freshness_evaluations_total",
                "Successful task freshness evaluation passes.",
                _evaluationCount);
            AppendCounter(builder,
                "ingot_edge_task_freshness_evaluation_errors_total",
                "Task freshness evaluation or retention cleanup failures.",
                _errorCount);
            builder.AppendLine("# HELP ingot_edge_task_freshness_evaluation_duration_seconds Duration of a freshness evaluation pass.");
            builder.AppendLine("# TYPE ingot_edge_task_freshness_evaluation_duration_seconds histogram");
            for (var index = 0; index < DurationBucketBoundsSeconds.Length; index++)
            {
                var bound = DurationBucketBoundsSeconds[index].ToString("R", CultureInfo.InvariantCulture);
                builder.Append("ingot_edge_task_freshness_evaluation_duration_seconds_bucket{le=\"")
                    .Append(bound).Append("\"} ").AppendLine(_durationBuckets[index].ToString(CultureInfo.InvariantCulture));
            }
            builder.Append("ingot_edge_task_freshness_evaluation_duration_seconds_bucket{le=\"+Inf\"} ")
                .AppendLine(_evaluationCount.ToString(CultureInfo.InvariantCulture));
            builder.Append("ingot_edge_task_freshness_evaluation_duration_seconds_sum ")
                .AppendLine((_durationSumTicks / (double)TimeSpan.TicksPerSecond).ToString("R", CultureInfo.InvariantCulture));
            builder.Append("ingot_edge_task_freshness_evaluation_duration_seconds_count ")
                .AppendLine(_evaluationCount.ToString(CultureInfo.InvariantCulture));
            AppendGauge(builder,
                "ingot_edge_task_freshness_last_evaluation_duration_seconds",
                "Duration of the latest successful freshness evaluation pass.",
                _lastDurationTicks / (double)TimeSpan.TicksPerSecond);
            AppendGauge(builder,
                "ingot_edge_task_freshness_last_evaluated_edges",
                "Edges evaluated during the latest successful freshness pass.",
                _lastEdgeCount);
            AppendGauge(builder,
                "ingot_edge_task_freshness_last_evaluated_tasks",
                "Tasks evaluated during the latest successful freshness pass.",
                _lastTaskCount);
            AppendGauge(builder,
                "ingot_edge_task_freshness_last_transitions",
                "State transitions recorded during the latest successful freshness pass.",
                _lastTransitionCount);
            AppendGauge(builder,
                "ingot_edge_task_freshness_last_success_timestamp_seconds",
                "Unix timestamp of the latest successful freshness evaluation.",
                _lastSuccessTimestampSeconds);
            AppendGauge(builder,
                "ingot_edge_task_freshness_last_error_timestamp_seconds",
                "Unix timestamp of the latest freshness evaluation or cleanup error.",
                _lastErrorTimestampSeconds);
            return builder.ToString();
        }
    }

    private static void AppendCounter(System.Text.StringBuilder builder, string name, string help, long value)
    {
        builder.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        builder.Append("# TYPE ").Append(name).AppendLine(" counter");
        builder.Append(name).Append(' ').AppendLine(value.ToString(CultureInfo.InvariantCulture));
    }

    private static void AppendGauge(System.Text.StringBuilder builder, string name, string help, double value)
    {
        builder.Append("# HELP ").Append(name).Append(' ').AppendLine(help);
        builder.Append("# TYPE ").Append(name).AppendLine(" gauge");
        builder.Append(name).Append(' ').AppendLine(value.ToString("R", CultureInfo.InvariantCulture));
    }
}
