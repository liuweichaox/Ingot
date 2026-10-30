// Summarizes one freshness pass for bounded worker telemetry.
namespace Ingot.Platform.Infrastructure.Services;

public sealed record EdgeTaskFreshnessEvaluationSummary(
    int EdgeCount,
    int TaskCount,
    int TransitionCount);
