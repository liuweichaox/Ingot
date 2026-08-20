namespace Ingot.Contracts.Edge;

using Ingot.Contracts.Acquisition;

/// <summary>
/// 边缘节点心跳：中心用它更新在线状态与运行指标。
/// </summary>
public sealed record EdgeHeartbeatRequest
{
    public required string EdgeId { get; init; }

    /// <summary>
    /// 可选：Edge.Agent 对中心可达的基础地址（如 http://10.0.0.12:8001）。
    /// 允许在心跳时更新（比如 DHCP/容器重建后地址变化）。
    /// </summary>
    public string? HostBaseUrl { get; init; }

    /// <summary>
    /// 可选：最后一次错误摘要。
    /// </summary>
    public string? LastError { get; init; }

    /// <summary>
    /// Edge 主动上报的采集运行与配置收敛状态。Platform 不需要反向连接 OT 网络即可查看。
    /// </summary>
    public EdgeAcquisitionRuntimeStatus? Acquisition { get; init; }

    /// <summary>Edge outbox 积压、确认、失败与恢复状态。</summary>
    public EdgeDeliveryRuntimeStatus? Delivery { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>中心按接收时间保存的现场采集与上行健康快照，用于判断故障何时开始和是否恢复。</summary>
public sealed record EdgeRuntimeStatusHistoryItem
{
    public required string EdgeId { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public string? AcquisitionState { get; init; }
    public DateTimeOffset? LastValidSnapshotAt { get; init; }
    public long ValidSnapshotCount { get; init; }
    public long EmittedEventCount { get; init; }
    public string? AcquisitionError { get; init; }
    public string? DeliveryState { get; init; }
    public long PendingEventCount { get; init; }
    public DateTimeOffset? OldestPendingEventAt { get; init; }
    public double? BacklogCapacityUsedPercent { get; init; }
    public double? ShipmentRatePerSecond { get; init; }
    public string? DeliveryError { get; init; }
}

/// <summary>把连续相同的采集、上行状态与问题合并为一个可读区间；原始心跳历史仍单独保留。</summary>
public sealed record EdgeRuntimeStatusInterval
{
    public required string EdgeId { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset EndedAt { get; init; }
    public long SampleCount { get; init; }
    public string? AcquisitionState { get; init; }
    public string? AcquisitionError { get; init; }
    public string? DeliveryState { get; init; }
    public string? DeliveryError { get; init; }
    public long StartingValidSnapshotCount { get; init; }
    public long EndingValidSnapshotCount { get; init; }
    public long StartingEmittedEventCount { get; init; }
    public long EndingEmittedEventCount { get; init; }
    public long MaximumPendingEventCount { get; init; }
}
