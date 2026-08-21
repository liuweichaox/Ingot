namespace Ingot.Domain.Events;

public sealed record ProductionEvent
{

    public int SchemaVersion { get; init; } = 1;

    public required string EventId { get; init; }

    public required string EventType { get; init; }

    public int EventTypeVersion { get; init; } = 1;

    public required DateTimeOffset OccurredAt { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required string Source { get; init; }

    public required ObjectRef Subject { get; init; }

    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, object?> Data { get; init; }
        = new Dictionary<string, object?>();

    public string? ExecutionId { get; init; }

    public AppliedConfigurationRef? AppliedConfiguration { get; init; }

    public IReadOnlyList<string> QualityFlags { get; init; } = [];

    public string PayloadHash { get; init; } = string.Empty;

    public long Seq { get; init; }

    public static ProductionEvent Create(
        string eventType,
        DateTimeOffset occurredAt,
        string source,
        ObjectRef subject,
        string? executionId = null,
        IReadOnlyDictionary<string, string>? context = null,
        IReadOnlyDictionary<string, object?>? data = null,
        AppliedConfigurationRef? appliedConfiguration = null,
        IReadOnlyList<string>? qualityFlags = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("事件类型不能为空。", nameof(eventType));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("事件来源不能为空。", nameof(source));

        var evt = new ProductionEvent
        {
            EventId = Guid.CreateVersion7().ToString(),
            EventType = eventType.Trim(),
            OccurredAt = occurredAt.ToUniversalTime(),
            RecordedAt = DateTimeOffset.UtcNow,
            Source = source.Trim(),
            Subject = subject,
            ExecutionId = executionId,
            AppliedConfiguration = appliedConfiguration,
            QualityFlags = qualityFlags ?? [],
            Context = context is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(context, StringComparer.Ordinal),
            Data = data is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(data, StringComparer.Ordinal)
        };
        var sealedEvent = ProductionEventIntegrity.Seal(evt);
        if (!ProductionEventValidator.TryValidate(
                sealedEvent,
                requirePersistedSequence: false,
                out var error))
        {
            throw new ArgumentException(error, nameof(evt));
        }

        return sealedEvent;
    }
}
