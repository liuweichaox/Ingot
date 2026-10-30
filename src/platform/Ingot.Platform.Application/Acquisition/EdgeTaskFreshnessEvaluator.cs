// Derives task snapshot freshness and its estimated effective time without database or clock I/O.
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Edge;

namespace Ingot.Platform.Application.Acquisition;

/// <summary>Pure policy for interpreting the latest Edge task report against its configured silence limit.</summary>
public static class EdgeTaskFreshnessEvaluator
{
    private const int MaximumStatusAgeMs = 30_000;

    public static EdgeTaskFreshnessEvaluation Evaluate(
        AcquisitionTaskRuntimeStatus task,
        EdgeAcquisitionRuntimeStatus acquisition,
        DateTimeOffset observedAt)
    {
        var threshold = task.MaximumValidSnapshotSilenceMs;
        if (threshold is null || threshold < 0 || threshold > 86_400_000)
            return new EdgeTaskFreshnessEvaluation(EdgeTaskFreshnessStates.Unknown, null, null);
        if (threshold == 0)
            return new EdgeTaskFreshnessEvaluation(EdgeTaskFreshnessStates.Unmonitored, threshold, null);
        if (acquisition.PlatformReceivedAt is null || acquisition.ReportedAt == default)
            return new EdgeTaskFreshnessEvaluation(EdgeTaskFreshnessStates.Unknown, threshold, null);
        if (task.LastValidSnapshotAt is null)
            return new EdgeTaskFreshnessEvaluation(EdgeTaskFreshnessStates.Unknown, threshold, null);

        var ageAtReportMs = (acquisition.ReportedAt - task.LastValidSnapshotAt.Value).TotalMilliseconds;
        var timeSinceReceivedMs = (observedAt - acquisition.PlatformReceivedAt.Value).TotalMilliseconds;
        if (!double.IsFinite(ageAtReportMs) || !double.IsFinite(timeSinceReceivedMs) ||
            ageAtReportMs < 0 || timeSinceReceivedMs < 0)
            return new EdgeTaskFreshnessEvaluation(EdgeTaskFreshnessStates.Unknown, threshold, null);
        if (ageAtReportMs > threshold)
            return new EdgeTaskFreshnessEvaluation(EdgeTaskFreshnessStates.Timeout, threshold, ageAtReportMs);
        if (timeSinceReceivedMs > MaximumStatusAgeMs)
            return new EdgeTaskFreshnessEvaluation(EdgeTaskFreshnessStates.Unknown, threshold, ageAtReportMs);

        return new EdgeTaskFreshnessEvaluation(
            EdgeTaskFreshnessStates.WithinLimit,
            threshold,
            ageAtReportMs);
    }

    private static DateTimeOffset EstimateEffectiveAt(
        EdgeTaskFreshnessEvaluation evaluation,
        EdgeAcquisitionRuntimeStatus acquisition,
        DateTimeOffset observedAt)
    {
        if (evaluation.State == EdgeTaskFreshnessStates.Timeout &&
            evaluation.ThresholdMs is { } threshold && evaluation.AgeAtReportMs is { } ageAtReportMs &&
            acquisition.PlatformReceivedAt is { } receivedAt)
        {
            try
            {
                return receivedAt.AddMilliseconds(threshold - ageAtReportMs);
            }
            catch (ArgumentOutOfRangeException)
            {
                return observedAt;
            }
        }

        if (evaluation.State == EdgeTaskFreshnessStates.WithinLimit &&
            evaluation.AgeAtReportMs is { } snapshotAgeAtReportMs &&
            acquisition.PlatformReceivedAt is { } snapshotReceivedAt)
        {
            try
            {
                return snapshotReceivedAt.AddMilliseconds(-snapshotAgeAtReportMs);
            }
            catch (ArgumentOutOfRangeException)
            {
                return observedAt;
            }
        }

        if (evaluation.State == EdgeTaskFreshnessStates.Unknown &&
            acquisition.PlatformReceivedAt is { } unknownReceivedAt)
        {
            var reportAgeMs = (observedAt - unknownReceivedAt).TotalMilliseconds;
            if (evaluation.AgeAtReportMs is not null && reportAgeMs > MaximumStatusAgeMs)
            {
                try
                {
                    return unknownReceivedAt.AddMilliseconds(MaximumStatusAgeMs);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return observedAt;
                }
            }

            return unknownReceivedAt;
        }

        return evaluation.State == EdgeTaskFreshnessStates.Unmonitored
            ? acquisition.PlatformReceivedAt ?? observedAt
            : observedAt;
    }

    public static EdgeTaskFreshnessTransitionDecision DecideTransition(
        string? previousState,
        int? previousThresholdMs,
        DateTimeOffset? previousStateSince,
        EdgeTaskFreshnessEvaluation evaluation,
        EdgeAcquisitionRuntimeStatus acquisition,
        DateTimeOffset observedAt)
    {
        var thresholdChanged = previousState is not null && previousThresholdMs != evaluation.ThresholdMs;
        var taskReappeared = previousState == EdgeTaskFreshnessStates.Removed;
        var changed = previousState is null ||
                      !string.Equals(previousState, evaluation.State, StringComparison.Ordinal) ||
                      thresholdChanged;
        if (!changed)
        {
            var stateSince = previousStateSince ?? observedAt;
            return new EdgeTaskFreshnessTransitionDecision(false, stateSince, stateSince, null);
        }

        var effectiveAt = thresholdChanged || taskReappeared
            ? observedAt
            : EstimateEffectiveAt(evaluation, acquisition, observedAt);
        var retrospectivelyConfirmedTimeout = previousState == EdgeTaskFreshnessStates.Unknown &&
                                               previousThresholdMs == evaluation.ThresholdMs &&
                                               evaluation.State == EdgeTaskFreshnessStates.Timeout;
        if (previousStateSince.HasValue && effectiveAt < previousStateSince.Value && !retrospectivelyConfirmedTimeout)
            effectiveAt = previousStateSince.Value;
        long? durationMs = previousState == EdgeTaskFreshnessStates.Timeout &&
                           evaluation.State == EdgeTaskFreshnessStates.WithinLimit &&
                           !thresholdChanged && previousStateSince.HasValue
            ? (long)Math.Max(0d, (effectiveAt - previousStateSince.Value).TotalMilliseconds)
            : null;
        var nextStateSince = previousState == evaluation.State && previousStateSince.HasValue
            ? previousStateSince.Value
            : effectiveAt;

        return new EdgeTaskFreshnessTransitionDecision(true, effectiveAt, nextStateSince, durationMs);
    }
}

/// <summary>Normalized result used to persist a task freshness transition.</summary>
public sealed record EdgeTaskFreshnessEvaluation(string State, int? ThresholdMs, double? AgeAtReportMs);

/// <summary>Policy result separating freshness state changes from persistence.</summary>
public sealed record EdgeTaskFreshnessTransitionDecision(
    bool Changed,
    DateTimeOffset EffectiveAt,
    DateTimeOffset StateSince,
    long? DurationMs);
