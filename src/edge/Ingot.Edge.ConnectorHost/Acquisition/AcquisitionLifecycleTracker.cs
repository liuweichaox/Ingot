using Ingot.Contracts.Acquisition;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
/// 将生产状态和控制器步序转换为离散运行边界事件。
/// 默认在生产开始时由 Edge 生成 CorrelationId；外部周期号仅作为向后兼容的可选输入。
/// </summary>
public sealed class AcquisitionLifecycleTracker
{
    private const string StageContextKey = "stage_number";
    private string? _activeCorrelationId;
    private string? _activeStep;
    private IReadOnlyDictionary<string, string> _activeContext = new Dictionary<string, string>();
    private ObjectRef? _activeSubject;
    private string? _activeSource;
    private ProductionEvent? _latestRecipeApplied;
    private long _sampleCount;

    public bool IsRunActive => _activeCorrelationId is not null;

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
        if (mapped.RecipeApplied is not null)
            _latestRecipeApplied = mapped.RecipeApplied;

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
            if (!isActive)
            {
                if (_activeCorrelationId is not null)
                {
                    events.Add(CompleteActiveRun(completedEventType, sample.OccurredAt));
                    ResetActiveRun();
                }
                return events;
            }
        }

        var incomingCorrelationId = string.IsNullOrWhiteSpace(sample.CorrelationId)
            ? null
            : sample.CorrelationId.Trim();
        if (_activeCorrelationId is not null &&
            incomingCorrelationId is not null &&
            !string.Equals(_activeCorrelationId, incomingCorrelationId, StringComparison.Ordinal))
        {
            events.Add(CompleteActiveRun(completedEventType, sample.OccurredAt));
            ResetActiveRun();
        }

        var startedNewRun = false;
        if (_activeCorrelationId is null)
        {
            _activeCorrelationId = incomingCorrelationId ?? Guid.CreateVersion7().ToString();
            _activeContext = sample.Context;
            _activeSubject = sample.Subject;
            _activeSource = sample.Source;
            startedNewRun = true;
            var startedData = new Dictionary<string, object?>();
            if (pollDelayMs > 0)
                startedData["pollDelayMs"] = pollDelayMs;
            events.Add(ProductionEvent.Create(
                startedEventType,
                sample.OccurredAt,
                sample.Source,
                sample.Subject,
                _activeCorrelationId,
                sample.Context,
                startedData));
        }

        sample = sample with { CorrelationId = _activeCorrelationId };
        var recipeApplied = mapped.RecipeApplied ?? (startedNewRun ? _latestRecipeApplied : null);
        if (recipeApplied is not null)
        {
            events.Add(recipeApplied with
            {
                EventId = Guid.CreateVersion7().ToString(),
                RecordedAt = DateTimeOffset.UtcNow,
                CorrelationId = _activeCorrelationId,
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
                _activeCorrelationId,
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
            _activeCorrelationId,
            _activeContext,
            new Dictionary<string, object?>
            {
                ["sampleCount"] = _sampleCount,
                ["completionStatus"] = "completed"
            });

    private void ResetActiveRun()
    {
        _activeCorrelationId = null;
        _activeStep = null;
        _activeContext = new Dictionary<string, string>();
        _activeSubject = null;
        _activeSource = null;
        _sampleCount = 0;
    }

    private static IReadOnlyList<ProductionEvent> WithoutLifecycle(AcquisitionMappingResult mapped)
        => mapped.RecipeApplied is null
            ? [mapped.Sample]
            : [mapped.RecipeApplied, mapped.Sample];
}
