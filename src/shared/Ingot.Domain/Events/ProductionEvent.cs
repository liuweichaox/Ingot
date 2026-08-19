namespace Ingot.Domain.Events;

/// <summary>
///     生产事件：已经发生且不可变的业务记录。
/// </summary>
public sealed record ProductionEvent
{
    /// <summary>生产事件信封版本；与具体事件类型的载荷版本相互独立。</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>全局唯一、按时间大致有序的 UUIDv7。</summary>
    public required string EventId { get; init; }

    /// <summary>事件类型，例如 process.execution.started、alarm.raised。</summary>
    public required string EventType { get; init; }

    /// <summary>事件载荷结构版本。</summary>
    public int EventTypeVersion { get; init; } = 1;

    /// <summary>采集侧观察到事件发生的 UTC 时间。</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>事件在边缘日志中持久化的 UTC 时间。</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>来源路径，例如 edge/EDGE-001/PLC-01/execution-rule。</summary>
    public required string Source { get; init; }

    /// <summary>事件发生的业务对象。</summary>
    public required ObjectRef Subject { get; init; }

    /// <summary>事件发生时的业务关联信息快照。</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();

    /// <summary>本事件特有的载荷。</summary>
    public IReadOnlyDictionary<string, object?> Data { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>成对或成组事件的生产过程执行号。</summary>
    public string? ExecutionId { get; init; }

    /// <summary>事件生成时实际生效的版本化配置；无配置驱动的事件可以为空。</summary>
    public AppliedConfigurationRef? AppliedConfiguration { get; init; }

    /// <summary>事件级质量标记；逐测点质量仍保存在类型化时序值中。</summary>
    public IReadOnlyList<string> QualityFlags { get; init; } = [];

    /// <summary>规范化事件内容的 SHA-256 小写十六进制摘要。</summary>
    public string PayloadHash { get; init; } = string.Empty;

    /// <summary>边缘日志分配的单调序号。</summary>
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
