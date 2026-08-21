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

    public IReadOnlyDictionary<string, string> HeaderSecretRefs { get; init; } = new Dictionary<string, string>();

    public int PollIntervalMs { get; init; } = 1000;
}

public sealed record AcquisitionExecutionOptions
{

    public int TimeoutMs { get; init; } = 10000;

    public int ReconnectDelayMs { get; init; } = 5000;

    public int SourceIdentityStaleAfterMs { get; init; } = 60_000;

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

    public bool ResetSessionOnConnect { get; init; } = true;
    public int KeepAliveSeconds { get; init; } = 30;
    public string PayloadCompression { get; init; } = "none";
    public string PayloadEncoding { get; init; } = "utf-8";

    public int SnapshotMaxAgeSeconds { get; init; }

    public IReadOnlyList<MqttTopicSubscription> Topics { get; init; } = [];
}

public sealed record MqttTopicSubscription
{

    public string? Channel { get; init; }
    public required string Topic { get; init; }
    public int Qos { get; init; }

    public IReadOnlyDictionary<string, int> TopicVariables { get; init; }
        = new Dictionary<string, int>();

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

    public string AddressBase { get; init; } = "zero-based";

    public int PollIntervalMs { get; init; } = 1000;

    public int MaxMergeGap { get; init; } = 8;
}

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

public sealed record McA1EConnection
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 5551;

    public int PollIntervalMs { get; init; } = 1000;

    public string DataCode { get; init; } = "binary";

    public byte PcNumber { get; init; } = 0xFF;

    public ushort MonitoringTimer { get; init; } = 0x0010;

    public string WordOrderLayout { get; init; } = "A";

    public int MaxMergeGap { get; init; } = 8;
}

public sealed record AcquisitionContextMapping
{
    public required string ContextKey { get; init; }
    public required string SourcePath { get; init; }
    public bool Required { get; init; }

    public string? Topic { get; init; }
}

public sealed record AcquisitionValueMapping
{
    public required string DataItemCode { get; init; }
    public required string SourcePath { get; init; }
    public bool Required { get; init; } = true;
    public string SourceDataType { get; init; } = "auto";

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

    public string? ModbusArea { get; init; }
    public ushort? ModbusAddress { get; init; }
    public ushort ModbusQuantity { get; init; } = 1;
    public string ByteOrder { get; init; } = "big-endian";
    public string WordOrder { get; init; } = "high-low";

    public string? MelsecDevice { get; init; }

    public string? MelsecAddress { get; init; }

    public int? BitIndex { get; init; }

    public string? Topic { get; init; }
}

public sealed record AcquisitionProcessSpecificationMapping
{
    public string EventType { get; init; } = "process.specification.applied";
    public required string IdPath { get; init; }
    public required string VersionPath { get; init; }
    public string? NamePath { get; init; }

    public string ParametersPath { get; init; } = ".";

    public IReadOnlyList<AcquisitionValueMapping> ParameterMappings { get; init; } = [];
}

public sealed record AcquisitionLifecycleMapping
{
    public string Mode { get; init; } = ProcessExecutionKinds.Discrete;

    public string? ActiveContextKey { get; init; }
    public string ActiveValue { get; init; } = "true";
    public string StartedEventType { get; init; } = "process.execution.started";
    public string CompletedEventType { get; init; } = "process.execution.completed";
    public string StepChangedEventType { get; init; } = "process.stage_changed";
}

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

public sealed record AcquisitionProbeRequest
{
    public required AcquisitionDeployment Deployment { get; init; }
    public SourceDiscoveryQuery Discovery { get; init; } = new();
}

public sealed record AcquisitionProbeTask
{
    public required string TaskId { get; init; }
    public required string EdgeId { get; init; }
    public required AcquisitionDeployment Deployment { get; init; }
    public SourceDiscoveryQuery Discovery { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
}

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

    public string? Topic { get; init; }
}

public sealed record AcquisitionMappingPreview
{
    public required string DataItemCode { get; init; }
    public required string SourcePath { get; init; }
    public bool Found { get; init; }

    public bool Accepted { get; init; }
    public string? RawValue { get; init; }
    public string? ConvertedValue { get; init; }
    public string? DataType { get; init; }
    public string? SourceUnit { get; init; }
    public string? TargetUnit { get; init; }
    public string? Error { get; init; }
}
