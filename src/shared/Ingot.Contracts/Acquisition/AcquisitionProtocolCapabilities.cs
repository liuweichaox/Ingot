namespace Ingot.Contracts.Acquisition;

/// <summary>
///     采集寻址方式。决定配置界面用哪种点位编辑器，也决定校验器解析哪种选择器语法。
/// </summary>
public static class AcquisitionAddressingKinds
{
    /// <summary>JSON 文档中的点号路径，例如 <c>sensors.温度</c>。</summary>
    public const string JsonPath = "json-path";

    /// <summary>OPC UA NodeId 文本形式，例如 <c>ns=2;s=Machine.Temperature</c>。</summary>
    public const string NodeId = "node-id";

    /// <summary>Modbus 寄存器区 + 地址，例如 <c>holding-register:100:int16</c>。</summary>
    public const string ModbusRegister = "modbus-register";

    /// <summary>MELSEC 软元件 + 编号，例如 <c>D:100:int16</c>、<c>M:20:boolean</c>。</summary>
    public const string MelsecDevice = "melsec-device";
}

/// <summary>
///     探查模式。<see cref="Discover"/> 表示设备可以自我描述、能列出点位清单；
///     <see cref="ConfiguredPointsOnly"/> 表示协议无法枚举地址空间，只能回读已配置的点位。
/// </summary>
public static class AcquisitionProbeModes
{
    public const string Discover = "discover";
    public const string ConfiguredPointsOnly = "configured-points-only";
}

/// <summary>
///     一个采集协议真正具备的能力。
///
///     这份矩阵是**平台、边缘与配置界面共用的唯一事实来源**，统一裁决：
///
///     <list type="bullet">
///       <item>校验器据此拒绝对该协议无意义的配置，而不是静默接受后丢弃；</item>
///       <item>配置界面据此隐藏不生效的字段，而不是让工程师填一个无效值；</item>
///       <item>新增协议时，先在此登记能力，界面与校验自动跟随。</item>
///     </list>
/// </summary>
public sealed record AcquisitionProtocolCapability
{
    public required string Protocol { get; init; }

    /// <summary>产品界面显示的驱动名称。</summary>
    public required string DisplayName { get; init; }

    /// <summary>一句话说明该驱动适用的设备形态。</summary>
    public required string Summary { get; init; }

    /// <summary>见 <see cref="AcquisitionAddressingKinds"/>。</summary>
    public required string Addressing { get; init; }

    /// <summary>见 <see cref="AcquisitionProbeModes"/>。</summary>
    public required string ProbeMode { get; init; }

    /// <summary><see cref="IngestionTask"/> 中承载该协议连接参数的属性名（驼峰形式）。</summary>
    public required string ConnectionSection { get; init; }

    /// <summary>是否支持 <c>TimestampMode = source</c>。为 false 时校验器强制 edge-received。</summary>
    public bool SupportsSourceTimestamp { get; init; }

    /// <summary>源时间由协议值元数据直接提供，不需要另配一个时间字段或点位。</summary>
    public bool UsesIntrinsicSourceTimestamp { get; init; }

    /// <summary>是否消费 <see cref="IngestionTask.SequencePath"/>。</summary>
    public bool SupportsSequencePath { get; init; }

    /// <summary>是否消费 <see cref="AcquisitionProcessSpecificationMapping.ParametersPath"/>。</summary>
    public bool SupportsControlParametersPath { get; init; }

    /// <summary>是否消费 <see cref="AcquisitionExecutionOptions.TimeoutMs"/>。</summary>
    public bool SupportsConnectTimeout { get; init; }

    /// <summary>是否消费 <see cref="AcquisitionExecutionOptions.ReconnectDelayMs"/>。</summary>
    public bool SupportsReconnectDelay { get; init; }

    /// <summary>是否支持把不同点位绑定到不同的 MQTT 主题。</summary>
    public bool SupportsPerTopicMapping { get; init; }

    /// <summary>是否支持字节序 / 字序设置。</summary>
    public bool SupportsRegisterByteOrder { get; init; }

    /// <summary>是否支持从一个字中提取单个位。</summary>
    public bool SupportsBitAddressing { get; init; }

    /// <summary>
    ///     一次采集周期内，读取 N 个点位需要的网络往返次数是否随 N 线性增长。
    ///     为 true 时配置界面按点位数量给出周期预估提醒。
    /// </summary>
    public bool ReadsPointsIndividually { get; init; }

    /// <summary>该协议允许的 <see cref="AcquisitionValueMapping.SourceDataType"/> 取值。</summary>
    public IReadOnlyList<string> SourceDataTypes { get; init; } = [];

    /// <summary>界面上需要显式告知工程师的协议约束。</summary>
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

    /// <summary>该数据类型是否可用于寄存器类协议。</summary>
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

    /// <summary>该数据类型占用几个 16 位字。string 由调用方按字节长度另行计算。</summary>
    public static int WordCountFor(string dataType) => dataType switch
    {
        "boolean" or "int16" or "uint16" => 1,
        "int32" or "uint32" or "float32" => 2,
        "int64" or "uint64" or "float64" => 4,
        _ => 1
    };
}
