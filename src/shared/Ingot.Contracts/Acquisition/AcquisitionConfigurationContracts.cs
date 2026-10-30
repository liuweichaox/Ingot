// 定义采集配置、逻辑归属对象与数据源身份的跨层契约。

using System.Text.Json.Serialization;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Contracts.Acquisition;

public static class AcquisitionProtocols
{
    public const string HttpPolling = "http-polling";
    public const string Mqtt = "mqtt";
    public const string OpcUa = "opc-ua";
    public const string ModbusTcp = "modbus-tcp";
    public const string ModbusRtu = "modbus-rtu";
    public const string ModbusAscii = "modbus-ascii";
    public const string MelsecA1E = "melsec-a1e";
    public const string MelsecMc3E = "melsec-mc-3e";
    public const string SiemensS7 = "siemens-s7";
    public const string EtherNetIp = "ethernet-ip";
    public const string OmronFinsTcp = "omron-fins-tcp";
    public const string OmronFinsUdp = "omron-fins-udp";
    public const string MtConnect = "mtconnect";
    public const string BacnetIp = "bacnet-ip";
    public const string BacnetMstp = "bacnet-mstp";
    public const string Snmp = "snmp";

    public static bool IsSupported(string? value) => value is HttpPolling or Mqtt or OpcUa or ModbusTcp or ModbusRtu or ModbusAscii or MelsecA1E or MelsecMc3E or SiemensS7 or EtherNetIp or OmronFinsTcp or OmronFinsUdp or MtConnect or BacnetIp or BacnetMstp or Snmp;
}

public static class AcquisitionDeviceKinds
{
    public const string Plc = "plc";
    public const string Cnc = "cnc";
    public const string IndustrialRobot = "industrial-robot";
    public const string Dcs = "dcs";
    public const string RemoteIo = "remote-io";
    public const string Drive = "drive";
    public const string SensorTransmitter = "sensor-transmitter";
    public const string MeasurementInstrument = "measurement-instrument";
    public const string EnergyMeter = "energy-meter";
    public const string MachineVision = "machine-vision";
    public const string IndustrialComputer = "industrial-computer";
    public const string IndustrialGateway = "industrial-gateway";
    public const string ScadaMes = "scada-mes";
    public const string Other = "other";
    public const string BuildingAutomation = "building-automation";
    public const string HmiPanel = "hmi-panel";
    public const string ProcessInstrument = "process-instrument";
    public const string WeighingInstrument = "weighing-instrument";
    public const string PowerAsset = "power-asset";
    public const string BarcodeRfid = "barcode-rfid";
    public const string EnvironmentalMonitor = "environmental-monitor";
    public const string NetworkInfrastructure = "network-infrastructure";

    public static bool IsSupported(string? value) => value is Plc or Cnc or IndustrialRobot or Dcs or RemoteIo or
        Drive or SensorTransmitter or MeasurementInstrument or EnergyMeter or MachineVision or IndustrialComputer or
        IndustrialGateway or ScadaMes or Other or BuildingAutomation or HmiPanel or ProcessInstrument or
        WeighingInstrument or PowerAsset or BarcodeRfid or EnvironmentalMonitor or NetworkInfrastructure;
}

/// <summary>Describes the physical or logical source device independently from its data owner and protocol driver.</summary>
public sealed record AcquisitionDeviceProfile
{
    public required string Kind { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
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
    // Equipment unit used for independent production preparation and tooling; may be a machine or a cavity.
    public string? EquipmentId { get; init; }
    public AcquisitionDeviceProfile? DeviceProfile { get; init; }
    public HttpPollingConnection HttpPolling { get; init; } = new();
    public MqttConnection? Mqtt { get; init; }
    public OpcUaConnection? OpcUa { get; init; }
    public ModbusTcpConnection? ModbusTcp { get; init; }
    public ModbusRtuConnection? ModbusRtu { get; init; }
    public ModbusAsciiConnection? ModbusAscii { get; init; }
    public McA1EConnection? MelsecA1E { get; init; }
    public Mc3EConnection? MelsecMc3E { get; init; }
    public SiemensS7Connection? SiemensS7 { get; init; }
    [JsonPropertyName("ethernetIp")]
    public EtherNetIpConnection? EtherNetIp { get; init; }
    public OmronFinsTcpConnection? OmronFinsTcp { get; init; }
    [JsonPropertyName("omronFinsUdp")]
    public OmronFinsUdpConnection? OmronFinsUdp { get; init; }
    [JsonPropertyName("mtConnect")]
    public MtConnectConnection? MtConnect { get; init; }
    [JsonPropertyName("bacnetIp")]
    public BacnetIpConnection? BacnetIp { get; init; }
    [JsonPropertyName("bacnetMstp")]
    public BacnetMstpConnection? BacnetMstp { get; init; }
    [JsonPropertyName("snmp")]
    public SnmpConnection? Snmp { get; init; }
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

    public int MaximumValidSnapshotSilenceMs { get; init; }
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

public sealed record ModbusRtuConnection
{
    public string PortName { get; init; } = string.Empty;
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 8;
    public string Parity { get; init; } = "even";
    public string StopBits { get; init; } = "one";
    public byte UnitId { get; init; } = 1;
    public string AddressBase { get; init; } = "zero-based";
    public int PollIntervalMs { get; init; } = 1000;
    public int MaxMergeGap { get; init; } = 8;
}

public sealed record ModbusAsciiConnection
{
    public string PortName { get; init; } = string.Empty;
    public int BaudRate { get; init; } = 9600;
    public int DataBits { get; init; } = 7;
    public string Parity { get; init; } = "even";
    public string StopBits { get; init; } = "one";
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
    public AcquisitionDeviceProfile? DeviceProfile { get; init; }
    public HttpPollingConnection? HttpPolling { get; init; }
    public MqttConnection? Mqtt { get; init; }
    public OpcUaConnection? OpcUa { get; init; }
    public ModbusTcpConnection? ModbusTcp { get; init; }
    public ModbusRtuConnection? ModbusRtu { get; init; }
    public ModbusAsciiConnection? ModbusAscii { get; init; }
    public McA1EConnection? MelsecA1E { get; init; }
    public Mc3EConnection? MelsecMc3E { get; init; }
    public SiemensS7Connection? SiemensS7 { get; init; }
    [JsonPropertyName("ethernetIp")]
    public EtherNetIpConnection? EtherNetIp { get; init; }
    public OmronFinsTcpConnection? OmronFinsTcp { get; init; }
    [JsonPropertyName("omronFinsUdp")]
    public OmronFinsUdpConnection? OmronFinsUdp { get; init; }
    [JsonPropertyName("mtConnect")]
    public MtConnectConnection? MtConnect { get; init; }
    [JsonPropertyName("bacnetIp")]
    public BacnetIpConnection? BacnetIp { get; init; }
    [JsonPropertyName("bacnetMstp")]
    public BacnetMstpConnection? BacnetMstp { get; init; }
    [JsonPropertyName("snmp")]
    public SnmpConnection? Snmp { get; init; }
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
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? EquipmentId { get; init; }
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

public static class MelsecMc3EFamilies
{
    public const string QL = "q-l";
    public const string IqR = "iq-r";
    public const string IqF = "iq-f";

    public static bool IsSupported(string? value) => value is QL or IqR or IqF;
}

/// <summary>Connection and routing parameters for binary MC 3E / SLMP TCP sessions.</summary>
public sealed record Mc3EConnection
{
    public string Host { get; init; } = string.Empty;

    // SLMP port numbers are configured by the target PLC or network module; there is no universal default.
    public int Port { get; init; }

    public string Family { get; init; } = string.Empty;

    public byte NetworkNumber { get; init; }

    public byte PcNumber { get; init; } = 0xFF;

    public ushort ModuleIoNumber { get; init; } = 0x03FF;

    public byte StationNumber { get; init; }

    public ushort MonitoringTimer { get; init; } = 0x0010;

    public int PollIntervalMs { get; init; } = 1000;

    public int MaxMergeGap { get; init; }
}

public sealed record SiemensS7Connection
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 102;

    public string CpuType { get; init; } = "S71200";

    public int Rack { get; init; }

    public int Slot { get; init; }

    public int PollIntervalMs { get; init; } = 1000;

    public int MaxMergeGap { get; init; }
}

public sealed record EtherNetIpConnection
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 44818;

    public string ControllerFamily { get; init; } = "control-logix";

    public string Path { get; init; } = "1,0";

    public int PollIntervalMs { get; init; } = 1000;
}

public sealed record OmronFinsTcpConnection
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 9600;

    public int PollIntervalMs { get; init; } = 1000;
}

/// <summary>Read-only direct FINS/UDP connection with explicitly assigned station node addresses.</summary>
public sealed record OmronFinsUdpConnection
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 9600;

    public int ClientNodeAddress { get; init; } = 1;

    public int ServerNodeAddress { get; init; } = 2;

    public int PollIntervalMs { get; init; } = 1000;
}

/// <summary>Read-only connection settings for the selected MTConnect Agent device.</summary>
public sealed record MtConnectConnection
{
    /// <summary>Absolute HTTP(S) Agent root URL; an optional reverse-proxy path prefix is allowed.</summary>
    public string BaseUrl { get; init; } = string.Empty;

    /// <summary>The exact Device name or UUID selected from the Agent probe response.</summary>
    public string DeviceNameOrUuid { get; init; } = string.Empty;

    public int PollIntervalMs { get; init; } = 1000;

    /// <summary>Maximum response body size in bytes for probe/current requests.</summary>
    public int MaximumResponseBytes { get; init; } = 4 * 1024 * 1024;

    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public IReadOnlyDictionary<string, string> HeaderSecretRefs { get; init; } = new Dictionary<string, string>();
}

/// <summary>Read-only BACnet/IP connection to one explicitly configured device over unicast UDP.</summary>
public sealed record BacnetIpConnection
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 47808;
    public uint DeviceInstance { get; init; }
    public int PollIntervalMs { get; init; } = 1000;
}

/// <summary>Read-only BACnet MS/TP connection to one explicitly selected device on an allowed RS-485 port.</summary>
public sealed record BacnetMstpConnection
{
    public string PortName { get; init; } = string.Empty;
    public int BaudRate { get; init; } = 38400;
    public int? LocalStationAddress { get; init; }
    public byte MaxMaster { get; init; } = 127;
    public byte MaxInfoFrames { get; init; } = 1;
    public uint DeviceInstance { get; init; }
    public int PollIntervalMs { get; init; } = 1000;
}

/// <summary>Read-only SNMP v2c/v3 connection to one explicitly configured IPv4 unicast agent.</summary>
public sealed record SnmpConnection
{
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; } = 161;
    public string Version { get; init; } = "v2c";
    public string BrowseRootOid { get; init; } = "1.3.6.1.2.1";
    public int PollIntervalMs { get; init; } = 1000;
    public string? CommunitySecretRef { get; init; }
    public string? UserName { get; init; }
    public string SecurityLevel { get; init; } = "authPriv";
    public string? AuthenticationProtocol { get; init; }
    public string? AuthenticationSecretRef { get; init; }
    public string? PrivacyProtocol { get; init; }
    public string? PrivacySecretRef { get; init; }
    public string? ContextName { get; init; }
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
    public string ActiveCondition { get; init; } = AcquisitionLifecycleActiveConditions.EqualTo;
    public string ActiveValue { get; init; } = "true";
    public string StartedEventType { get; init; } = "process.execution.started";
    public string CompletedEventType { get; init; } = "process.execution.completed";
    public string StepChangedEventType { get; init; } = "process.stage_changed";
}

public static class AcquisitionLifecycleActiveConditions
{
    public const string EqualTo = "equals";
    public const string Nonzero = "nonzero";

    public static bool IsSupported(string? value)
        => string.Equals(value, EqualTo, StringComparison.Ordinal) ||
           string.Equals(value, Nonzero, StringComparison.Ordinal);
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
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

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
