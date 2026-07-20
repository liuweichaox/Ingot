namespace Ingot.Domain.Events;

/// <summary>
///     生产事件：已经发生且不可变的业务记录。
/// </summary>
public sealed record ProductionEvent
{
    /// <summary>全局唯一、按时间大致有序的 UUIDv7。</summary>
    public required string EventId { get; init; }

    /// <summary>事件类型，例如 cycle.started、alarm.raised。</summary>
    public required string EventType { get; init; }

    /// <summary>事件载荷结构版本。</summary>
    public int EventTypeVersion { get; init; } = 1;

    /// <summary>采集侧观察到事件发生的 UTC 时间。</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>事件在边缘日志中持久化的 UTC 时间。</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>来源路径，例如 edge/EDGE-001/PLC-01/cycle-rule。</summary>
    public required string Source { get; init; }

    /// <summary>事件发生的业务对象。</summary>
    public required ObjectRef Subject { get; init; }

    /// <summary>事件发生时的业务关联信息快照。</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();

    /// <summary>本事件特有的载荷。</summary>
    public IReadOnlyDictionary<string, object?> Data { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>成对或成组事件的生产周期号。</summary>
    public string? CorrelationId { get; init; }

    /// <summary>边缘日志分配的单调序号。</summary>
    public long Seq { get; init; }

    public static ProductionEvent Create(
        string eventType,
        DateTimeOffset occurredAt,
        string source,
        ObjectRef subject,
        string? correlationId = null,
        IReadOnlyDictionary<string, string>? context = null,
        IReadOnlyDictionary<string, object?>? data = null)
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
            CorrelationId = correlationId,
            Context = context is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(context, StringComparer.Ordinal),
            Data = data is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(data, StringComparer.Ordinal)
        };
        if (!ProductionEventValidator.TryValidate(
                evt,
                requirePersistedSequence: false,
                out var error))
        {
            throw new ArgumentException(error, nameof(evt));
        }

        return evt;
    }
}
