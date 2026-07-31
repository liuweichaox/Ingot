using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Contracts.Acquisition;

public static class AcquisitionProtocols
{
    public const string HttpPolling = "http-polling";
    public const string Mqtt = "mqtt";
    public const string OpcUa = "opc-ua";
    public const string ModbusTcp = "modbus-tcp";
    public const string MelsecA1E = "melsec-a1e";

    public static bool IsSupported(string? value) => value is HttpPolling or Mqtt or OpcUa or ModbusTcp or MelsecA1E;
}

public sealed record AcquisitionProfile
{
    public required string ProfileId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public required string EdgeId { get; init; }
    public string Protocol { get; init; } = AcquisitionProtocols.HttpPolling;
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; } = 1;
    public required string Source { get; init; }
    public string SubjectType { get; init; } = "equipment";
    public required string SubjectId { get; init; }
    /// <summary>HTTP 轮询连接。保留 Connection 名称以兼容已经保存的配置版本。</summary>
    public HttpPollingConnection Connection { get; init; } = new();
    public MqttConnection? Mqtt { get; init; }
    public OpcUaConnection? OpcUa { get; init; }
    public ModbusTcpConnection? ModbusTcp { get; init; }
    public McA1EConnection? MelsecA1E { get; init; }
    public AcquisitionExecutionOptions Execution { get; init; } = new();
    public string TimestampMode { get; init; } = "source";
    public string TimestampPath { get; init; } = "timestamp";
    public string? SequencePath { get; init; } = "sequence";
    public string SampleEventType { get; init; } = "process.sample";
    public IReadOnlyDictionary<string, string> StaticContext { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<AcquisitionContextMapping> ContextMappings { get; init; } = [];
    public IReadOnlyList<AcquisitionValueMapping> ValueMappings { get; init; } = [];
    public AcquisitionRecipeMapping? Recipe { get; init; }
    public AcquisitionLifecycleMapping? Lifecycle { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record HttpPollingConnection
{
    public string BaseUrl { get; init; } = string.Empty;
    public string SnapshotPath { get; init; } = "/api/v1/snapshot";
    /// <summary>一次读取完成后，开始下一次读取前等待的时间；不是固定采样周期。</summary>
    public int PollIntervalMs { get; init; } = 1000;
}

public sealed record AcquisitionExecutionOptions
{
    public int TimeoutMs { get; init; } = 10000;
    public int ReconnectDelayMs { get; init; } = 5000;
}

public sealed record MqttConnection
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 1883;
    public string ProtocolVersion { get; init; } = "5.0";
    public string ClientId { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? PasswordSecretRef { get; init; }
    public bool UseTls { get; init; }
    public string? CaCertificatePath { get; init; }
    public string? ClientCertificatePath { get; init; }
    public string? ClientCertificatePasswordSecretRef { get; init; }
    /// <summary>
    /// MQTT 3.1.1 的 Clean Session；在 MQTT 5.0 中等价作为 Clean Start 使用。
    /// 字段名为兼容已保存配置保留，产品界面按所选协议版本显示正确术语。
    /// </summary>
    public bool CleanSession { get; init; } = true;
    public int KeepAliveSeconds { get; init; } = 30;
    public IReadOnlyList<MqttTopicSubscription> Topics { get; init; } = [];
}

public sealed record MqttTopicSubscription
{
    public required string Topic { get; init; }
    public int Qos { get; init; }
}

public sealed record OpcUaConnection
{
    public string EndpointUrl { get; init; } = string.Empty;
    public string SecurityMode { get; init; } = "none";
    public string SecurityPolicy { get; init; } = "None";
    public string AuthenticationType { get; init; } = "anonymous";
    public string? Username { get; init; }
    public string? PasswordSecretRef { get; init; }
    public string? ClientCertificatePath { get; init; }
    public string? ClientCertificatePasswordSecretRef { get; init; }
    public bool TrustServerCertificate { get; init; }
    public int PublishingIntervalMs { get; init; } = 1000;
    public int SamplingIntervalMs { get; init; } = 1000;
}

public sealed record ModbusTcpConnection
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 502;
    public byte UnitId { get; init; } = 1;
    /// <summary>
    /// 工程地址的输入方式。zero-based 表示用户填写的就是线缆地址；one-based 表示按设备手册
    /// 从 1 开始编号，采集器发送请求前自动减 1。
    /// </summary>
    public string AddressBase { get; init; } = "zero-based";
    /// <summary>一次寄存器读取完成后，开始下一次读取前等待的时间；不是固定采样周期。</summary>
    public int PollIntervalMs { get; init; } = 1000;
}

/// <summary>
///     三菱 MC 协议 A 兼容 1E 帧连接配置，用于 FX3U-ENET(-L/-ADP) 等设备。
///     选择器格式：软元件:地址:类型（如 D:100:int16）。凭据不入库（本协议通常无需认证）。
/// </summary>
public sealed record McA1EConnection
{
    public string Host { get; init; } = string.Empty;
    /// <summary>FX3U-ENET-ADP 的 MC 端口（常见 5551，以现场配置为准；≠ MELSOFT 端口）。</summary>
    public int Port { get; init; } = 5551;
    /// <summary>一次读取完成后到下一次读取的等待时间；不是固定采样周期。</summary>
    public int PollIntervalMs { get; init; } = 1000;
    /// <summary>PLC 侧开放设置中的通信数据码：binary 或 ascii，必须与模块设置一致。</summary>
    public string DataCode { get; init; } = "binary";
    /// <summary>1E 帧目标 PC 号；直连 FX3U/FX3UC 通常为 FFH。</summary>
    public byte PcNumber { get; init; } = 0xFF;
    /// <summary>1E 帧监视定时器，单位 250ms（默认 0x0010=16→约 4s）。</summary>
    public ushort MonitoringTimer { get; init; } = 0x0010;
    /// <summary>软元件号/代码字段顺序。FX3U-ENET-ADP A-compatible 1E 固定为 A（号在前）。</summary>
    public string WordOrderLayout { get; init; } = "A";
}

public sealed record AcquisitionContextMapping
{
    public required string ContextKey { get; init; }
    public required string SourcePath { get; init; }
    public bool Required { get; init; }
}

public sealed record AcquisitionValueMapping
{
    public required string DataItemCode { get; init; }
    public required string SourcePath { get; init; }
    public bool Required { get; init; } = true;
    public string SourceDataType { get; init; } = "auto";
    public double Scale { get; init; } = 1;
    public double Offset { get; init; }
    public string? ModbusArea { get; init; }
    public ushort? ModbusAddress { get; init; }
    public ushort ModbusQuantity { get; init; } = 1;
    public string ByteOrder { get; init; } = "big-endian";
    public string WordOrder { get; init; } = "high-low";
}

public sealed record AcquisitionRecipeMapping
{
    public string EventType { get; init; } = "recipe.applied";
    public required string IdPath { get; init; }
    public required string VersionPath { get; init; }
    public string? NamePath { get; init; }
    public required string ParametersPath { get; init; }
    public IReadOnlyList<AcquisitionValueMapping> ParameterMappings { get; init; } = [];
}

/// <summary>
/// 可选的离散运行边界映射。连续设备不配置此项；周期设备通常由运行状态变化生成边界，
/// CorrelationId 由 Edge 在周期开始时生成。CorrelationIdContextKey 仅用于兼容确实提供外部周期号的旧设备。
/// </summary>
public sealed record AcquisitionLifecycleMapping
{
    public string Mode { get; init; } = "discrete-cycle";
    public string? CorrelationIdContextKey { get; init; }
    /// <summary>
    /// 可选的运行激活上下文键。配置后，值不等于 ActiveValue 的快照只用于结束当前运行，
    /// 不会生成新的过程采样或虚假占位周期。
    /// </summary>
    public string? ActiveContextKey { get; init; }
    public string ActiveValue { get; init; } = "true";
    public string StartedEventType { get; init; } = "cycle.started";
    public string CompletedEventType { get; init; } = "cycle.completed";
    public string StepChangedEventType { get; init; } = "process.stage_changed";
}

/// <summary>平台下发给采集执行器的不可变配置及其数据语义。</summary>
public sealed record AcquisitionDeployment
{
    public required AcquisitionProfile Profile { get; init; }
    public required ProcessDataModel DataModel { get; init; }
}

/// <summary>平台要求指定 Edge 对一份尚未发布的采集配置执行一次真实设备探查。</summary>
public sealed record AcquisitionProbeRequest
{
    public required AcquisitionDeployment Deployment { get; init; }
}

public sealed record AcquisitionProbeResult
{
    public bool Success { get; init; }
    public bool MappingsValidated { get; init; }
    public required string Protocol { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset TestedAt { get; init; }
    public IReadOnlyList<AcquisitionProbePoint> Points { get; init; } = [];
    public IReadOnlyList<AcquisitionMappingPreview> Mappings { get; init; } = [];
}

/// <summary>设备暴露的一个可选择点位。Path 是写入映射的稳定设备路径或寄存器选择器。</summary>
public sealed record AcquisitionProbePoint
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string DataType { get; init; }
    public string? RawValue { get; init; }
}

/// <summary>一次设备原始值到平台语义项的换算预览。</summary>
public sealed record AcquisitionMappingPreview
{
    public required string DataItemCode { get; init; }
    public required string SourcePath { get; init; }
    public bool Found { get; init; }
    public string? RawValue { get; init; }
    public string? ConvertedValue { get; init; }
    public string? DataType { get; init; }
    public string? Unit { get; init; }
    public string? Error { get; init; }
}
