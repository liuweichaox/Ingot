using Ingot.Contracts.Events;
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

public static class AcquisitionTimestampEncodings
{
    public const string Auto = "auto";
    public const string Iso8601 = "iso-8601";
    public const string UnixSeconds = "unix-s";
    public const string UnixMilliseconds = "unix-ms";

    public static bool IsSupported(string? value)
        => value is Auto or Iso8601 or UnixSeconds or UnixMilliseconds;
}

public sealed record IngestionTask
{
    public required string TaskId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string? TemplateId { get; init; }
    public int? TemplateVersion { get; init; }
    public string? DataSourceId { get; init; }
    public int? DataSourceVersion { get; init; }
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public required string EdgeId { get; init; }
    public string Protocol { get; init; } = AcquisitionProtocols.HttpPolling;
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; } = 1;
    public required string Source { get; init; }
    public string SubjectType { get; init; } = "equipment";
    public required string SubjectId { get; init; }
    public HttpPollingConnection HttpPolling { get; init; } = new();
    public MqttConnection? Mqtt { get; init; }
    public OpcUaConnection? OpcUa { get; init; }
    public ModbusTcpConnection? ModbusTcp { get; init; }
    public McA1EConnection? MelsecA1E { get; init; }
    public AcquisitionExecutionOptions Execution { get; init; } = new();
    public string TimestampMode { get; init; } = "source";
    public string TimestampPath { get; init; } = "timestamp";
    public string TimestampEncoding { get; init; } = AcquisitionTimestampEncodings.Auto;
    public string? SequencePath { get; init; }
    public string SampleEventType { get; init; } = "process.sample";
    public IReadOnlyDictionary<string, string> StaticContext { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<AcquisitionContextMapping> ContextMappings { get; init; } = [];
    public IReadOnlyList<AcquisitionValueMapping> ValueMappings { get; init; } = [];
    public AcquisitionProcessSpecificationMapping? ProcessSpecification { get; init; }
    public AcquisitionLifecycleMapping? Lifecycle { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record HttpPollingConnection
{
    public string BaseUrl { get; init; } = string.Empty;
    public string SnapshotPath { get; init; } = "/api/v1/snapshot";
    public string Method { get; init; } = "get";
    public string? ContentType { get; init; }
    public string? RequestBody { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    /// <summary>HTTP 头名称到 Edge 密钥库引用的映射；实际密钥不会进入平台配置。</summary>
    public IReadOnlyDictionary<string, string> HeaderSecretRefs { get; init; } = new Dictionary<string, string>();
    /// <summary>一次读取完成后，开始下一次读取前等待的时间；不是固定采样周期。</summary>
    public int PollIntervalMs { get; init; } = 1000;
}

public sealed record AcquisitionExecutionOptions
{
    /// <summary>
    ///     建立连接与等待单次响应的上限。是否生效由
    ///     <see cref="AcquisitionProtocolCapability.SupportsConnectTimeout"/> 决定。
    /// </summary>
    public int TimeoutMs { get; init; } = 10000;

    /// <summary>连接断开后重新建立连接前的等待时间。</summary>
    public int ReconnectDelayMs { get; init; } = 5000;

    /// <summary>
    ///     配置了源序号或源时间戳时，允许同一源身份连续不变化的最长时间。
    ///     超过该时间说明设备侧序号或时钟可能停滞；0 表示关闭停滞检测。
    /// </summary>
    public int SourceIdentityStaleAfterMs { get; init; } = 60_000;

    /// <summary>
    ///     设备源时间戳允许领先 Edge 接收时间的上限。超过该值通常表示设备时钟、编码或点位配置错误；
    ///     0 表示关闭该检查。
    /// </summary>
    public int MaximumFutureTimestampSkewMs { get; init; } = 300_000;
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
    /// <summary>连接时是否丢弃服务端保存的旧会话状态。</summary>
    public bool ResetSessionOnConnect { get; init; } = true;
    public int KeepAliveSeconds { get; init; } = 30;
    public string PayloadCompression { get; init; } = "none";
    public string PayloadEncoding { get; init; } = "utf-8";

    /// <summary>
    ///     跨主题合并快照时，单个值允许的最大陈旧时间（秒）。0 表示不限制。
    ///     订阅多个主题时，某个主题停止发布会让合并快照里一直保留它最后一次的值；
    ///     配置该上限后，超时的值视为缺失，必需点位缺失即不再产生采样。
    /// </summary>
    public int SnapshotMaxAgeSeconds { get; init; }

    public IReadOnlyList<MqttTopicSubscription> Topics { get; init; } = [];
}

public sealed record MqttTopicSubscription
{
    /// <summary>
    ///     可选的稳定通道代码。任务模板可在映射的 Topic 字段引用此代码，实例化时再解析为
    ///     该数据源的真实主题过滤器，使同一模板可复用于主题前缀不同的多台设备。
    /// </summary>
    public string? Channel { get; init; }
    public required string Topic { get; init; }
    public int Qos { get; init; }

    /// <summary>
    ///     可选的主题层级变量。键是变量名，值是从 0 开始的主题层级索引。
    ///     变量以 <c>$topic.&lt;name&gt;</c> 写入原始快照，可用于上下文和数据映射。
    /// </summary>
    public IReadOnlyDictionary<string, int> TopicVariables { get; init; }
        = new Dictionary<string, int>();

    /// <summary>
    ///     该主题报文中承载数据的 JSON 子对象路径。留空表示报文根即数据对象。
    ///     用于网关把设备数据包在 <c>payload</c>、<c>d</c> 之类的信封里的情况。
    /// </summary>
    public string? PayloadRoot { get; init; }
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
    public int MaximumValueAgeMs { get; init; } = 30_000;
    public int MaximumTimestampSkewMs { get; init; } = 10_000;
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

    /// <summary>
    ///     相邻点位之间允许合并读取的最大地址间隙。0 表示只合并严格连续的点位，
    ///     避免跨越设备未实现的寄存器区导致整批读取失败。
    /// </summary>
    public int MaxMergeGap { get; init; } = 8;
}

/// <summary>
///     可复用的数据摄取任务定义。模板保存源协议、标准数据模型、映射和执行策略，
///     不保存现场节点、真实数据源身份、网络地址或凭据。
/// </summary>
public sealed record IngestionTaskTemplate
{
    public required string TemplateId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public required string Protocol { get; init; }
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; } = 1;
    public AcquisitionExecutionOptions Execution { get; init; } = new();
    public string TimestampMode { get; init; } = "edge-received";
    public string TimestampPath { get; init; } = string.Empty;
    public string TimestampEncoding { get; init; } = AcquisitionTimestampEncodings.Auto;
    public string? SequencePath { get; init; }
    public string SampleEventType { get; init; } = "process.sample";
    public IReadOnlyDictionary<string, string> StaticContext { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<AcquisitionContextMapping> ContextMappings { get; init; } = [];
    public IReadOnlyList<AcquisitionValueMapping> ValueMappings { get; init; } = [];
    public AcquisitionProcessSpecificationMapping? ProcessSpecification { get; init; }
    public AcquisitionLifecycleMapping? Lifecycle { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>一个可独立连接和探查的真实数据源，只保存实例身份、位置和连接参数。</summary>
public sealed record DataSourceInstance
{
    public required string DataSourceId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public required string EdgeId { get; init; }
    public required string Protocol { get; init; }
    public required string SourceKey { get; init; }
    public string SubjectType { get; init; } = "equipment";
    public required string SubjectId { get; init; }
    public HttpPollingConnection? HttpPolling { get; init; }
    public MqttConnection? Mqtt { get; init; }
    public OpcUaConnection? OpcUa { get; init; }
    public ModbusTcpConnection? ModbusTcp { get; init; }
    public McA1EConnection? MelsecA1E { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>把一份模板与一个数据源绑定为可发布、可版本化的数据摄取任务。</summary>
public sealed record IngestionTaskBinding
{
    public required string TaskId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public required string TemplateId { get; init; }
    public int TemplateVersion { get; init; } = 1;
    public required string DataSourceId { get; init; }
    public int DataSourceVersion { get; init; } = 1;
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
///     三菱 MC 协议 A 兼容 1E 帧连接配置，用于 FX3U-ENET(-L/-ADP) 等设备。
///     选择器格式：软元件:编号:类型（如 D:100:int16、M:20:boolean、D:100.3:boolean）。
///     凭据不入库（本协议通常无需认证）。
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

    /// <summary>
    ///     相邻点位之间允许合并读取的最大编号间隔。设为 0 表示逐点读取。
    ///     合并可以把 N 个点位的 N 次往返压缩成少数几次，代价是会读取到用不上的中间软元件。
    /// </summary>
    public int MaxMergeGap { get; init; } = 8;
}

public sealed record AcquisitionContextMapping
{
    public required string ContextKey { get; init; }
    public required string SourcePath { get; init; }
    public bool Required { get; init; }

    /// <summary>MQTT 多主题订阅时，该上下文来自哪个主题；留空表示任意主题。</summary>
    public string? Topic { get; init; }
}

public sealed record AcquisitionValueMapping
{
    public required string DataItemCode { get; init; }
    public required string SourcePath { get; init; }
    public bool Required { get; init; } = true;
    public string SourceDataType { get; init; } = "auto";
    /// <summary>
    ///     源值实际占用的字节数。用于长度不能仅由寄存器数量表达的类型（当前为字符串），
    ///     避免奇数字节长度在结构化配置往返时被补齐为偶数。
    /// </summary>
    public ushort? SourceByteLength { get; init; }
    public string? SourceUnit { get; init; }
    public double Scale { get; init; } = 1;
    public double Offset { get; init; }
    public string? QualityPath { get; init; }
    public IReadOnlyList<string> AcceptedQualityValues { get; init; } = [];
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public string OutOfRangeBehavior { get; init; } = "reject";
    public string MissingValueBehavior { get; init; } = "inherit";
    public string? DefaultValue { get; init; }

    // ---- Modbus 结构化寻址 ----
    public string? ModbusArea { get; init; }
    public ushort? ModbusAddress { get; init; }
    public ushort ModbusQuantity { get; init; } = 1;
    public string ByteOrder { get; init; } = "big-endian";
    public string WordOrder { get; init; } = "high-low";

    // ---- MELSEC 结构化寻址 ----
    /// <summary>MELSEC 软元件代码，例如 D / M / X。</summary>
    public string? MelsecDevice { get; init; }

    /// <summary>
    ///     MELSEC 软元件编号，按该软元件在手册中的进制书写（X/Y 八进制、B/W 十六进制、其余十进制）。
    ///     保留字符串形式，避免十六进制与八进制在往返中丢失原始写法。
    /// </summary>
    public string? MelsecAddress { get; init; }

    // ---- 位寻址 ----
    /// <summary>
    ///     从一个 16 位字中提取的位序号（0-15）。仅在 <see cref="SourceDataType"/> 为
    ///     <c>boolean</c> 且目标是字寄存器/字软元件时有意义。
    /// </summary>
    public int? BitIndex { get; init; }

    // ---- MQTT 多主题 ----
    /// <summary>
    ///     该点位来自哪个 MQTT 主题。留空表示任意主题的报文都可以提供该值。
    ///     配置多主题时显式绑定，才能让不同主题各自携带一部分字段。
    /// </summary>
    public string? Topic { get; init; }
}

public sealed record AcquisitionProcessSpecificationMapping
{
    public string EventType { get; init; } = "process.specification.applied";
    public required string IdPath { get; init; }
    public required string VersionPath { get; init; }
    public string? NamePath { get; init; }

    /// <summary>
    ///     控制参数所在的 JSON 子对象路径，参数映射的路径相对于它；"." 表示报文根。
    ///     只有文档类协议（HTTP / MQTT）真正使用；寄存器类协议的参数直接用点位选择器寻址，
    ///     校验器对这些协议不再强制填写。
    /// </summary>
    public string ParametersPath { get; init; } = ".";

    public IReadOnlyList<AcquisitionValueMapping> ParameterMappings { get; init; } = [];
}

/// <summary>
/// 可选的离散过程执行边界映射。连续设备不配置此项；离散设备由运行状态变化生成边界，
/// ExecutionId 由 Edge 在过程执行开始时生成。
/// </summary>
public sealed record AcquisitionLifecycleMapping
{
    public string Mode { get; init; } = ProcessExecutionKinds.Discrete;
    /// <summary>
    /// 可选的运行激活上下文键。配置后，值不等于 ActiveValue 的快照只用于结束当前运行，
    /// 不会生成新的过程采样或虚假占位执行。
    /// </summary>
    public string? ActiveContextKey { get; init; }
    public string ActiveValue { get; init; } = "true";
    public string StartedEventType { get; init; } = "process.execution.started";
    public string CompletedEventType { get; init; } = "process.execution.completed";
    public string StepChangedEventType { get; init; } = "process.stage_changed";
}

/// <summary>平台下发给采集执行器的不可变配置及其数据语义。</summary>
public sealed record AcquisitionDeployment
{
    public required IngestionTask Task { get; init; }
    public required ProcessDataModel DataModel { get; init; }
}

public sealed record SourceDiscoveryQuery
{
    public string? Cursor { get; init; }
    public int PageSize { get; init; } = 200;
    public string? Search { get; init; }
    public string? RootPath { get; init; }
    public IReadOnlyList<string> Namespaces { get; init; } = [];
    public IReadOnlyList<string> Kinds { get; init; } = [];
    public string? PathPattern { get; init; }
    public string? NamePattern { get; init; }
}

public sealed record IngestionTaskProbeRequest
{
    public required IngestionTask Task { get; init; }
    public SourceDiscoveryQuery Discovery { get; init; } = new();
}

/// <summary>平台要求指定 Edge 对一份尚未发布的采集配置执行一次真实设备探查。</summary>
public sealed record AcquisitionProbeRequest
{
    public required AcquisitionDeployment Deployment { get; init; }
    public SourceDiscoveryQuery Discovery { get; init; } = new();
}

/// <summary>由 Edge 主动拉取的一次临时设备探查任务。</summary>
public sealed record AcquisitionProbeTask
{
    public required string TaskId { get; init; }
    public required string EdgeId { get; init; }
    public required AcquisitionDeployment Deployment { get; init; }
    public SourceDiscoveryQuery Discovery { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>Edge 完成主动拉取的探查任务后回报的结果。</summary>
public sealed record AcquisitionProbeTaskCompletion
{
    public required string TaskId { get; init; }
    public required string EdgeId { get; init; }
    public required AcquisitionProbeResult Result { get; init; }
}

public sealed record AcquisitionProbeResult
{
    public bool Success { get; init; }
    public bool MappingsValidated { get; init; }
    public required string Protocol { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset TestedAt { get; init; }
    public IReadOnlyList<AcquisitionProbePoint> Points { get; init; } = [];
    public string? NextCursor { get; init; }
    public int ScannedPointCount { get; init; }
    public bool ScanLimitReached { get; init; }
    public IReadOnlyList<AcquisitionMappingPreview> Mappings { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>设备暴露的一个可选择点位。Path 是写入映射的稳定设备路径或寄存器选择器。</summary>
public sealed record AcquisitionProbePoint
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public required string DataType { get; init; }
    public string? RawValue { get; init; }
    public string? Unit { get; init; }
    public string? Quality { get; init; }
    public DateTimeOffset? SourceTimestamp { get; init; }

    /// <summary>MQTT 探查时，该点位来自哪个主题。</summary>
    public string? Topic { get; init; }
}

/// <summary>一次设备原始值到平台语义项的换算预览。</summary>
public sealed record AcquisitionMappingPreview
{
    public required string DataItemCode { get; init; }
    public required string SourcePath { get; init; }
    public bool Found { get; init; }
    /// <summary>原始值缺失时也可能因默认值或可选省略策略而通过映射策略。</summary>
    public bool Accepted { get; init; }
    public string? RawValue { get; init; }
    public string? ConvertedValue { get; init; }
    public string? DataType { get; init; }
    public string? SourceUnit { get; init; }
    public string? TargetUnit { get; init; }
    public string? Error { get; init; }
}
