using System.Text.Json.Serialization;
using Ingot.Domain.Models;

namespace Ingot.Domain.Events;

/// <summary>
///     配置驱动的生产事件派生规则。
/// </summary>
public sealed class EventRule
{
    public string RuleId { get; set; } = string.Empty;

    public string Category { get; set; } = "cycle";

    public ObjectRef? Subject { get; set; }

    public EventRuleTrigger Trigger { get; set; } = new();

    public string? StartedEventType { get; set; }

    public string? CompletedEventType { get; set; }

    /// <summary>ValueChanged 等单事件规则产生的事件类型。</summary>
    public string? EventType { get; set; }

    public List<string> ContextKeys { get; set; } = new();

    /// <summary>事件触发后写入资产上下文。值 "$value" 表示使用当前标签值。</summary>
    public Dictionary<string, string> SetContext { get; set; } = new();

    /// <summary>随事件写入的静态载荷。</summary>
    public Dictionary<string, object?> Data { get; set; } = new();

    /// <summary>成对事件开始时从源读取并固化进事件载荷的字段。</summary>
    public List<EventSnapshotField> SnapshotOnStart { get; set; } = new();

    /// <summary>成对事件结束时从源读取并固化进事件载荷的字段。</summary>
    public List<EventSnapshotField> SnapshotOnEnd { get; set; } = new();

    public string GetStartedEventType() =>
        string.IsNullOrWhiteSpace(StartedEventType)
            ? Trigger.Kind switch
            {
                EventTriggerKind.BitFlag => $"{Category}.raised",
                EventTriggerKind.Threshold => $"{Category}.entered",
                _ => $"{Category}.started"
            }
            : StartedEventType;

    public string GetCompletedEventType() =>
        string.IsNullOrWhiteSpace(CompletedEventType)
            ? Trigger.Kind switch
            {
                EventTriggerKind.BitFlag => $"{Category}.cleared",
                EventTriggerKind.Threshold => $"{Category}.exited",
                _ => $"{Category}.completed"
            }
            : CompletedEventType;

    public string GetEventType() =>
        string.IsNullOrWhiteSpace(EventType) ? $"{Category}.changed" : EventType;
}

/// <summary>
///     事件触发瞬间需要从源读取的载荷字段。Tag 保持适配器中立；
///     PLC 适配器下它可以是寄存器地址。
/// </summary>
public sealed class EventSnapshotField
{
    public string FieldName { get; set; } = string.Empty;

    public string Tag { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public int StringByteLength { get; set; }

    public string? Encoding { get; set; }
}

public sealed class EventRuleTrigger
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EventTriggerKind Kind { get; set; } = EventTriggerKind.EdgePair;

    /// <summary>源适配器内的标签。PLC 适配器中可直接使用寄存器地址。</summary>
    public string Tag { get; set; } = string.Empty;

    public string DataType { get; set; } = string.Empty;

    public int StringByteLength { get; set; }

    public string? Encoding { get; set; }

    /// <summary>BitFlag 使用的零基位序。</summary>
    public int Bit { get; set; }

    /// <summary>Threshold 使用的数值边界。</summary>
    public decimal Threshold { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ThresholdDirection ThresholdDirection { get; set; } = ThresholdDirection.AboveOrEqual;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcquisitionTrigger StartTriggerMode { get; set; } = AcquisitionTrigger.RisingEdge;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AcquisitionTrigger EndTriggerMode { get; set; } = AcquisitionTrigger.FallingEdge;
}

public enum EventTriggerKind
{
    EdgePair,
    ValueChanged,
    BitFlag,
    Threshold
}

public enum ThresholdDirection
{
    AboveOrEqual,
    BelowOrEqual
}
