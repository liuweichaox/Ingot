using Ingot.Contracts.Acquisition;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
/// 将可配置的关联号和控制器步序转换为离散运行边界事件。没有生命周期配置时保持连续采集语义。
/// </summary>
public sealed class AcquisitionLifecycleTracker
{
    private string? _activeCorrelationId;
    private string? _activeStep;
    private IReadOnlyDictionary<string, string> _activeContext = new Dictionary<string, string>();
    private ObjectRef? _activeSubject;
    private string? _activeSource;
    private long _sampleCount;

    public IReadOnlyList<ProductionEvent> Track(
        AcquisitionMappingResult mapped,
        AcquisitionLifecycleMapping? lifecycle,
        int samplePeriodMs)
        => lifecycle is null
            ? WithoutLifecycle(mapped)
            : Track(
                mapped,
                lifecycle.CorrelationIdContextKey,
                lifecycle.StepContextKey,
                lifecycle.StepNameContextKey,
                lifecycle.StartedEventType,
                lifecycle.CompletedEventType,
                lifecycle.StepChangedEventType,
                lifecycle.ExpectedDurationMs,
                samplePeriodMs);

    public IReadOnlyList<ProductionEvent> Track(
        AcquisitionMappingResult mapped,
        LifecycleFieldMapping? lifecycle,
        int samplePeriodMs)
        => lifecycle is null
            ? WithoutLifecycle(mapped)
            : Track(
                mapped,
                lifecycle.CorrelationIdContextKey,
                lifecycle.StepContextKey,
                lifecycle.StepNameContextKey,
                lifecycle.StartedEventType,
                lifecycle.CompletedEventType,
                lifecycle.StepChangedEventType,
                lifecycle.ExpectedDurationMs,
                samplePeriodMs);

    private IReadOnlyList<ProductionEvent> Track(
        AcquisitionMappingResult mapped,
        string correlationIdContextKey,
        string? stepContextKey,
        string? stepNameContextKey,
        string startedEventType,
        string completedEventType,
        string stepChangedEventType,
        int? expectedDurationMs,
        int samplePeriodMs)
    {
        var sample = mapped.Sample;
        var correlationId = sample.CorrelationId;
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            throw new InvalidDataException(
                $"离散运行采样缺少关联号；请检查上下文映射 {correlationIdContextKey}。");
        }

        var events = new List<ProductionEvent>(5);
        if (_activeCorrelationId is not null &&
            !string.Equals(_activeCorrelationId, correlationId, StringComparison.Ordinal))
        {
            events.Add(ProductionEvent.Create(
                completedEventType,
                sample.OccurredAt,
                _activeSource!,
                _activeSubject!,
                _activeCorrelationId,
                _activeContext,
                new Dictionary<string, object?>
                {
                    ["sampleCount"] = _sampleCount,
                    ["completionStatus"] = "completed"
                }));
            _activeCorrelationId = null;
            _activeStep = null;
            _sampleCount = 0;
        }

        if (_activeCorrelationId is null)
        {
            _activeCorrelationId = correlationId;
            _activeContext = sample.Context;
            _activeSubject = sample.Subject;
            _activeSource = sample.Source;
            var startedData = new Dictionary<string, object?>
            {
                ["samplePeriodMs"] = samplePeriodMs
            };
            if (expectedDurationMs is not null)
            {
                startedData["expectedDurationMs"] = expectedDurationMs.Value;
                startedData["expectedSampleCount"] =
                    expectedDurationMs.Value / Math.Max(1, samplePeriodMs);
            }
            events.Add(ProductionEvent.Create(
                startedEventType,
                sample.OccurredAt,
                sample.Source,
                sample.Subject,
                correlationId,
                sample.Context,
                startedData));
        }

        if (mapped.RecipeApplied is not null)
            events.Add(mapped.RecipeApplied);

        if (!string.IsNullOrWhiteSpace(stepContextKey) &&
            sample.Context.TryGetValue(stepContextKey, out var step) &&
            !string.Equals(step, _activeStep, StringComparison.Ordinal))
        {
            var data = new Dictionary<string, object?> { ["sourceStep"] = step };
            if (!string.IsNullOrWhiteSpace(stepNameContextKey) &&
                sample.Context.TryGetValue(stepNameContextKey, out var stepName))
            {
                data["sourceStepName"] = stepName;
            }
            events.Add(ProductionEvent.Create(
                stepChangedEventType,
                sample.OccurredAt,
                sample.Source,
                sample.Subject,
                correlationId,
                sample.Context,
                data));
            _activeStep = step;
        }

        events.Add(sample);
        _activeContext = sample.Context;
        _sampleCount++;
        return events;
    }

    private static IReadOnlyList<ProductionEvent> WithoutLifecycle(AcquisitionMappingResult mapped)
        => mapped.RecipeApplied is null
            ? [mapped.Sample]
            : [mapped.RecipeApplied, mapped.Sample];
}
