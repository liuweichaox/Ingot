using Ingot.Contracts.Acquisition;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class AcquisitionLifecycleTracker
{
    private const string StageContextKey = "stage_number";
    private string? _activeExecutionId;
    private string? _activeStep;
    private IReadOnlyDictionary<string, string> _activeContext = new Dictionary<string, string>();
    private ObjectRef? _activeSubject;
    private string? _activeSource;
    private ProductionEvent? _latestProcessSpecificationApplied;
    private long _sampleCount;
    private bool _activeRunStartedWithoutObservedIdle;
    private bool _hasObservedLifecycleState;

    public bool IsRunActive => _activeExecutionId is not null;

    public IReadOnlyList<ProductionEvent> Track(
        AcquisitionMappingResult mapped,
        AcquisitionLifecycleMapping? lifecycle,
        int pollDelayMs)
        => lifecycle is null
            ? WithoutLifecycle(mapped)
            : Track(
                mapped,
                lifecycle.ActiveContextKey,
                lifecycle.ActiveValue,
                lifecycle.StartedEventType,
                lifecycle.CompletedEventType,
                lifecycle.StepChangedEventType,
                pollDelayMs);

    public IReadOnlyList<ProductionEvent> Track(
        AcquisitionMappingResult mapped,
        LifecycleFieldMapping? lifecycle,
        int pollDelayMs)
        => lifecycle is null
            ? WithoutLifecycle(mapped)
            : Track(
                mapped,
                lifecycle.ActiveContextKey,
                lifecycle.ActiveValue,
                lifecycle.StartedEventType,
                lifecycle.CompletedEventType,
                lifecycle.StepChangedEventType,
                pollDelayMs);

    private IReadOnlyList<ProductionEvent> Track(
        AcquisitionMappingResult mapped,
        string? activeContextKey,
        string activeValue,
        string startedEventType,
        string completedEventType,
        string stepChangedEventType,
        int pollDelayMs)
    {
        var sample = mapped.Sample;
        if (mapped.ProcessSpecificationApplied is not null)
            _latestProcessSpecificationApplied = mapped.ProcessSpecificationApplied;

        var events = new List<ProductionEvent>(5);
        if (!string.IsNullOrWhiteSpace(activeContextKey))
        {
            if (!sample.Context.TryGetValue(activeContextKey, out var currentActiveValue))
            {
                throw new InvalidDataException(
                    $"离散运行采样缺少运行激活状态；请检查上下文映射 {activeContextKey}。");
            }

            var isActive = string.Equals(
                currentActiveValue,
                activeValue,
                StringComparison.OrdinalIgnoreCase);
            var firstObservedState = !_hasObservedLifecycleState;
            _hasObservedLifecycleState = true;
            if (!isActive)
            {
                if (_activeExecutionId is not null)
                {
                    events.Add(CompleteActiveRun(completedEventType, sample.OccurredAt));
                    ResetActiveRun();
                }
                return events;
            }
            if (firstObservedState)
                _activeRunStartedWithoutObservedIdle = true;
        }

        var startedNewRun = false;
        if (_activeExecutionId is null)
        {
            _activeExecutionId = Guid.CreateVersion7().ToString();
            _activeContext = sample.Context;
            _activeSubject = sample.Subject;
            _activeSource = sample.Source;
            startedNewRun = true;
            var startedData = new Dictionary<string, object?>();
            if (pollDelayMs > 0)
                startedData["pollDelayMs"] = pollDelayMs;
            if (_activeRunStartedWithoutObservedIdle)
                startedData["lifecycleCaptureStatus"] = "active_at_connector_start";
            events.Add(ProductionEvent.Create(
                startedEventType,
                sample.OccurredAt,
                sample.Source,
                sample.Subject,
                _activeExecutionId,
                sample.Context,
                startedData));
        }

        sample = sample with { ExecutionId = _activeExecutionId };
        var processSpecificationApplied = mapped.ProcessSpecificationApplied ?? (startedNewRun ? _latestProcessSpecificationApplied : null);
        if (processSpecificationApplied is not null)
        {
            events.Add(processSpecificationApplied with
            {
                EventId = Guid.CreateVersion7().ToString(),
                RecordedAt = DateTimeOffset.UtcNow,
                ExecutionId = _activeExecutionId,
                Context = sample.Context
            });
        }

        if (sample.Context.TryGetValue(StageContextKey, out var step) &&
            !string.Equals(step, _activeStep, StringComparison.Ordinal))
        {
            var data = new Dictionary<string, object?> { ["sourceStep"] = step };
            events.Add(ProductionEvent.Create(
                stepChangedEventType,
                sample.OccurredAt,
                sample.Source,
                sample.Subject,
                _activeExecutionId,
                sample.Context,
                data));
            _activeStep = step;
        }

        events.Add(sample);
        _activeContext = sample.Context;
        _sampleCount++;
        return events;
    }

    private ProductionEvent CompleteActiveRun(
        string completedEventType,
        DateTimeOffset occurredAt)
        => ProductionEvent.Create(
            completedEventType,
            occurredAt,
            _activeSource!,
            _activeSubject!,
            _activeExecutionId,
            _activeContext,
            new Dictionary<string, object?>
            {
                ["sampleCount"] = _sampleCount,
                ["completionStatus"] = _activeRunStartedWithoutObservedIdle
                    ? "partial_after_connector_start"
                    : "completed"
            });

    private void ResetActiveRun()
    {
        _activeExecutionId = null;
        _activeStep = null;
        _activeContext = new Dictionary<string, string>();
        _activeSubject = null;
        _activeSource = null;
        _sampleCount = 0;
        _activeRunStartedWithoutObservedIdle = false;
    }

    private static IReadOnlyList<ProductionEvent> WithoutLifecycle(AcquisitionMappingResult mapped)
        => mapped.ProcessSpecificationApplied is null
            ? [mapped.Sample]
            : [mapped.ProcessSpecificationApplied, mapped.Sample];
}
