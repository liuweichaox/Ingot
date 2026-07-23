using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Contracts.Acquisition;

public static class AcquisitionProtocols
{
    public const string HttpPolling = "http-polling";
    public const string Mqtt = "mqtt";
    public const string OpcUa = "opc-ua";
    public const string ModbusTcp = "modbus-tcp";

    public static bool IsSupported(string? value) => value is HttpPolling or Mqtt or OpcUa or ModbusTcp;
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
    public int PollIntervalMs { get; init; } = 1000;
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
/// 可选的离散运行边界映射。连续设备不配置此项；周期设备由采集值中的关联号变化生成运行边界事件。
/// </summary>
public sealed record AcquisitionLifecycleMapping
{
    public string Mode { get; init; } = "discrete-cycle";
    public string CorrelationIdContextKey { get; init; } = "correlation_id";
    public string? StepContextKey { get; init; } = "recipe_step";
    public string? StepNameContextKey { get; init; } = "recipe_step_name";
    public string StartedEventType { get; init; } = "cycle.started";
    public string CompletedEventType { get; init; } = "cycle.completed";
    public string StepChangedEventType { get; init; } = "recipe.step_changed";
    public int? ExpectedDurationMs { get; init; }
}

/// <summary>平台下发给采集执行器的不可变配置及其数据语义。</summary>
public sealed record AcquisitionDeployment
{
    public required AcquisitionProfile Profile { get; init; }
    public required ProcessDataModel DataModel { get; init; }
}
