namespace Ingot.Contracts.Acquisition;

public static class AcquisitionAddressingKinds
{

    public const string JsonPath = "json-path";

    public const string NodeId = "node-id";

    public const string ModbusRegister = "modbus-register";

    public const string MelsecDevice = "melsec-device";
}

public static class AcquisitionProbeModes
{
    public const string Discover = "discover";
    public const string ConfiguredPointsOnly = "configured-points-only";
}

public sealed record AcquisitionProtocolCapability
{
    public required string Protocol { get; init; }

    public required string DisplayName { get; init; }

    public required string Summary { get; init; }

    public required string Addressing { get; init; }

    public required string ProbeMode { get; init; }

    public required string ConnectionSection { get; init; }

    public bool SupportsSourceTimestamp { get; init; }

    public bool UsesIntrinsicSourceTimestamp { get; init; }

    public bool SupportsSequencePath { get; init; }

    public bool SupportsControlParametersPath { get; init; }

    public bool SupportsConnectTimeout { get; init; }

    public bool SupportsReconnectDelay { get; init; }

    public bool SupportsPerTopicMapping { get; init; }

    public bool SupportsRegisterByteOrder { get; init; }

    public bool SupportsBitAddressing { get; init; }

    public bool ReadsPointsIndividually { get; init; }

    public IReadOnlyList<string> SourceDataTypes { get; init; } = [];

    public IReadOnlyList<string> Constraints { get; init; } = [];
}

public static class AcquisitionProtocolCapabilities
{
    private static readonly string[] RegisterTypes =
    [
        "int16", "uint16", "int32", "uint32", "float32",
        "int64", "uint64", "float64", "string", "boolean"
    ];

    private static readonly string[] DocumentTypes = ["auto"];

    public static bool IsRegisterDataType(string? value)
        => value is not null && Array.IndexOf(RegisterTypes, value) >= 0;

    private static readonly Dictionary<string, AcquisitionProtocolCapability> Registry =
        new(StringComparer.Ordinal)
        {
            [AcquisitionProtocols.HttpPolling] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.HttpPolling,
                DisplayName = "HTTP 轮询",
                Summary = "设备或网关以 HTTP 提供一份 JSON 快照，采集节点按间隔读取。",
                Addressing = AcquisitionAddressingKinds.JsonPath,
                ProbeMode = AcquisitionProbeModes.Discover,
                ConnectionSection = "httpPolling",
                SupportsSourceTimestamp = true,
                SupportsSequencePath = true,
                SupportsControlParametersPath = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SourceDataTypes = DocumentTypes,
                Constraints =
                [
                    "支持 GET 或 POST；敏感请求头必须引用现场节点密钥库，不在平台配置中保存值。",
                    "一次响应必须返回包含全部必需字段的完整 JSON 快照，响应体上限为 16 MiB。"
                ]
            },
            [AcquisitionProtocols.Mqtt] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.Mqtt,
                DisplayName = "MQTT 订阅",
                Summary = "设备或网关主动向消息服务器发布 JSON 报文，采集节点订阅接收。",
                Addressing = AcquisitionAddressingKinds.JsonPath,
                ProbeMode = AcquisitionProbeModes.Discover,
                ConnectionSection = "mqtt",
                SupportsSourceTimestamp = true,
                SupportsSequencePath = true,
                SupportsControlParametersPath = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsPerTopicMapping = true,
                SourceDataTypes = DocumentTypes,
                Constraints =
                [
                    "订阅多个主题时，请为每个点位指定来源主题；未指定的点位按任意主题的报文解析。",
                    "跨主题的值会合并为一份快照，只有全部必需点位都收到过报文后才会产生采样。",
                    "订阅过滤器不得重叠；同一报文只能归属一个报文根和稳定通道。"
                ]
            },
            [AcquisitionProtocols.OpcUa] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.OpcUa,
                DisplayName = "OPC UA",
                Summary = "通过 OPC UA 会话订阅变量节点，由服务器按变化推送。",
                Addressing = AcquisitionAddressingKinds.NodeId,
                ProbeMode = AcquisitionProbeModes.Discover,
                ConnectionSection = "opcUa",
                SupportsSourceTimestamp = true,
                UsesIntrinsicSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SourceDataTypes = DocumentTypes,
                Constraints =
                [
                    "采样时间固定使用服务器提供的 SourceTimestamp，不能改用采集节点接收时间。",
                    "NodeId 中的命名空间序号由服务器分配；服务器重排命名空间后需要重新验证配置。",
                    "当前驱动订阅变量节点，不采集 OPC UA 事件和报警。"
                ]
            },
            [AcquisitionProtocols.ModbusTcp] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.ModbusTcp,
                DisplayName = "Modbus TCP",
                Summary = "按寄存器地址读取，适用于仪表、变频器与通用 PLC 网关。",
                Addressing = AcquisitionAddressingKinds.ModbusRegister,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "modbusTcp",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsRegisterByteOrder = true,
                SupportsBitAddressing = true,
                SourceDataTypes = RegisterTypes,
                Constraints =
                [
                    "协议不能枚举地址空间，验证连接只会回读已经配置的寄存器。",
                    "一个采集配置只能访问一个从站编号。"
                ]
            },
            [AcquisitionProtocols.MelsecA1E] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.MelsecA1E,
                DisplayName = "三菱 MC 协议（A 兼容 1E 帧）",
                Summary = "直连 FX3U-ENET(-L/-ADP) 等以太网模块，按软元件编号读取。",
                Addressing = AcquisitionAddressingKinds.MelsecDevice,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "melsecA1E",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsBitAddressing = true,
                ReadsPointsIndividually = false,
                SourceDataTypes = RegisterTypes,
                Constraints =
                [
                    "协议不能枚举软元件，验证连接只会回读已经配置的点位。",
                    "X / Y 软元件在 FX 系列按八进制编号，编号中不能出现数字 8 和 9。",
                    "位软元件（M / X / Y / B / S / L）读取布尔值时使用位单位批量读命令。"
                ]
            }
        };

    public static IReadOnlyList<AcquisitionProtocolCapability> All { get; } =
        Registry.Values.OrderBy(static value => value.Protocol, StringComparer.Ordinal).ToArray();

    public static bool TryGet(string? protocol, out AcquisitionProtocolCapability capability)
    {
        if (protocol is not null && Registry.TryGetValue(protocol, out var found))
        {
            capability = found;
            return true;
        }

        capability = null!;
        return false;
    }

    public static int WordCountFor(string dataType) => dataType switch
    {
        "boolean" or "int16" or "uint16" => 1,
        "int32" or "uint32" or "float32" => 2,
        "int64" or "uint64" or "float64" => 4,
        _ => 1
    };
}
