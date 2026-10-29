namespace Ingot.Contracts.Acquisition;

public static class AcquisitionAddressingKinds
{

    public const string JsonPath = "json-path";

    public const string NodeId = "node-id";

    public const string ModbusRegister = "modbus-register";

    public const string MelsecDevice = "melsec-device";

    public const string MelsecMc3EDevice = "melsec-mc-3e-device";

    public const string SiemensS7Address = "siemens-s7-address";

    public const string EthernetIpTag = "ethernet-ip-tag";

    public const string OmronFinsAddress = "omron-fins-address";

    public const string MtConnectDataItem = "mtconnect-data-item";
    public const string BacnetObject = "bacnet-object";
    public const string SnmpOid = "snmp-oid";
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

    // Only drivers with a shared runtime may advertise cross-task connection reuse.
    public bool SharesConnectionBetweenTasks { get; init; }

    public IReadOnlyList<string> SupportedWireEncodings { get; init; } = [];

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

    private static readonly string[] SiemensS7Types = [.. RegisterTypes, "byte"];

    private static readonly string[] EtherNetIpTypes =
    ["boolean", "int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float32", "float64"];

    private static readonly string[] OmronFinsTypes =
    ["boolean", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float32", "float64"];

    private static readonly string[] MelsecMc3ETypes =
    ["boolean", "int16", "uint16", "int32", "uint32", "float32", "int64", "uint64", "float64"];

    private static readonly string[] DocumentTypes = ["auto"];

    private static readonly string[] SnmpTypes = ["auto", "boolean", "int32", "uint32", "uint64", "string"];

    public static bool IsRegisterDataType(string? value)
        => value is not null && Array.IndexOf(RegisterTypes, value) >= 0;

    public static bool IsOmronFinsDataType(string? value)
        => value is not null && Array.IndexOf(OmronFinsTypes, value) >= 0;

    public static bool IsMelsecMc3EDataType(string? value)
        => value is not null && Array.IndexOf(MelsecMc3ETypes, value) >= 0;

    private static readonly Dictionary<string, AcquisitionProtocolCapability> Registry =
        new(StringComparer.Ordinal)
        {
            [AcquisitionProtocols.HttpPolling] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.HttpPolling,
                DisplayName = "HTTP 轮询",
                Summary = "设备或网关以 HTTP 提供一份 JSON 快照，现场节点按间隔读取。",
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
                Summary = "设备或网关主动向消息服务器发布 JSON 报文，现场节点订阅接收。",
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
                    "采样时间固定使用服务器提供的 SourceTimestamp，不能改用现场节点接收时间。",
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
                SharesConnectionBetweenTasks = true,
                SourceDataTypes = RegisterTypes,
                Constraints =
                [
                    "协议不能枚举地址空间，验证连接只会回读已经配置的寄存器。",
                    "一个逻辑采集组只能访问一个从站编号；同一端点的多个组与探查共享串行连接。",
                    "批读在单个采集组内规划，不将不同组的采样视为同一时刻的原子快照。"
                ]
            },
            [AcquisitionProtocols.ModbusRtu] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.ModbusRtu,
                DisplayName = "Modbus RTU",
                Summary = "通过 RS-485/串口总线读取已配置寄存器，可复用同一总线访问多个从站。",
                Addressing = AcquisitionAddressingKinds.ModbusRegister,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "modbusRtu",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsRegisterByteOrder = true,
                SupportsBitAddressing = true,
                SharesConnectionBetweenTasks = true,
                SourceDataTypes = RegisterTypes,
                Constraints =
                [
                    "仅回读用户明确配置的地址，不扫描寄存器或写入设备。",
                    "同一物理串口共享独占会话并串行读取，可使用不同从站编号；串口参数必须一致。",
                    "边缘节点须显式允许串口名，并具有操作系统设备权限。"
                ]
            },
            [AcquisitionProtocols.ModbusAscii] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.ModbusAscii,
                DisplayName = "Modbus ASCII",
                Summary = "通过 RS-485/串口总线读取已配置寄存器，可复用同一总线访问多个从站。",
                Addressing = AcquisitionAddressingKinds.ModbusRegister,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "modbusAscii",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsRegisterByteOrder = true,
                SupportsBitAddressing = true,
                SharesConnectionBetweenTasks = true,
                SourceDataTypes = RegisterTypes,
                Constraints =
                [
                    "仅回读用户明确配置的地址，不扫描寄存器或写入设备。",
                    "同一物理串口共享独占会话并串行读取，可使用不同从站编号；串口参数必须一致。",
                    "边缘节点须显式允许串口名，并具有操作系统设备权限。"
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
                SharesConnectionBetweenTasks = true,
                SupportedWireEncodings = ["binary", "ascii"],
                SourceDataTypes = RegisterTypes,
                Constraints =
                [
                    "协议不能枚举软元件，验证连接只会回读已经配置的点位。",
                    "X / Y 软元件在 FX 系列按八进制编号，编号中不能出现数字 8 和 9。",
                    "位软元件（M / X / Y / B / S / L）读取布尔值时使用位单位批量读命令。",
                    "支持 Binary 与 ASCII；同一端点的多个逻辑组与探查共享串行连接，活动连接的编码必须一致。",
                    "批读在单个采集组内规划，不保证不同读取块的原子一致性。"
                ]
            },
            [AcquisitionProtocols.MelsecMc3E] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.MelsecMc3E,
                DisplayName = "三菱 MC 协议（3E 帧 / SLMP）",
                Summary = "通过二进制 MC 3E / SLMP TCP 读取 Q/L、iQ-R 与 iQ-F PLC 中已配置的软元件点位。",
                Addressing = AcquisitionAddressingKinds.MelsecMc3EDevice,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "melsecMc3E",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsBitAddressing = true,
                SharesConnectionBetweenTasks = true,
                SupportedWireEncodings = ["binary"],
                SourceDataTypes = MelsecMc3ETypes,
                Constraints =
                [
                    "仅使用二进制 3E 帧读取用户明确配置的点位，不扫描地址空间、不写入 PLC。",
                    "首期设备目录为 X、Y、M、L、B、D、W；实际地址范围由 PLC 系列、CPU 与工程配置决定。",
                    "X / Y / B / W 使用十六进制编号，M / L / D 使用十进制编号；D / W 字内布尔点位需指定 0-15 位偏移。",
                    "TCP 端口由目标 PLC 或网络模块工程配置，没有通用默认端口；必须显式填写后探查回读。",
                    "同一端点和路由参数的采集与探查共享串行连接；批读在单个采集组内规划，不保证不同读取块的原子一致性。"
                ]
            },
            [AcquisitionProtocols.SiemensS7] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.SiemensS7,
                DisplayName = "Siemens S7",
                Summary = "通过 S7 ISO-on-TCP 读取已配置的 PLC 存储区地址。",
                Addressing = AcquisitionAddressingKinds.SiemensS7Address,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "siemensS7",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsBitAddressing = true,
                SourceDataTypes = SiemensS7Types,
                Constraints =
                [
                    "只读访问显式配置的 DB、输入、输出和 M 区地址，不扫描地址或写入 PLC。",
                    "S7-1200/1500 需按 CPU 安全设置允许 PUT/GET，并为 DB 点位使用非优化访问。",
                    "CPU 系列、机架和槽位必须与现场硬件配置一致；不同 CPU 的可访问范围由设备自身决定。"
                ]
            },
            [AcquisitionProtocols.EtherNetIp] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.EtherNetIp,
                DisplayName = "Rockwell EtherNet/IP",
                Summary = "按符号标签读取 ControlLogix、CompactLogix 或 Micro800 PLC 中已配置的标量点位。",
                Addressing = AcquisitionAddressingKinds.EthernetIpTag,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "ethernetIp",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                ReadsPointsIndividually = true,
                SourceDataTypes = EtherNetIpTypes,
                Constraints =
                [
                    "仅回读用户明确配置的标量符号标签，不枚举标签、不扫描地址、不写入 PLC，也不支持隐式 I/O。",
                    "ControlLogix 与 CompactLogix 需配置直连背板槽位路径；Micro800 不使用路径。",
                    "当前支持 Boolean、8/16/32/64 位整数和 32/64 位浮点标签；字符串、UDT 和结构体暂不支持。",
                    "单任务最多读取 256 个不同标签；当前不支持动态上下文、质量路径或工艺规格辅助标签。"
                ]
            },
            [AcquisitionProtocols.OmronFinsTcp] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.OmronFinsTcp,
                DisplayName = "Omron FINS/TCP",
                Summary = "按 PLC 内存区读取已配置的 Omron 点位。",
                Addressing = AcquisitionAddressingKinds.OmronFinsAddress,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "omronFinsTcp",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsBitAddressing = true,
                SharesConnectionBetweenTasks = true,
                SourceDataTypes = OmronFinsTypes,
                Constraints =
                [
                    "仅通过 FINS/TCP 读取用户明确配置的 CIO、WR、HR、AR 与 DM 点位，不扫描、不写入 PLC，也不执行 FINS 路由。",
                    "内存区有效范围由 PLC 型号和 CPU 配置决定；语法校验通过后仍须在现场探查回读。",
                    "支持 Boolean、16/32/64 位整数和 32/64 位浮点；字内固定高字节在前，多字点位可配置字序；当前不支持文本、计时器、计数器和 EM 区。",
                    "同一 TCP 端点的采集与探查共享串行连接；PLC 的可用连接数因机型而异。"
                ]
            },
            [AcquisitionProtocols.OmronFinsUdp] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.OmronFinsUdp,
                DisplayName = "Omron FINS/UDP",
                Summary = "通过 FINS/UDP 单播读取已配置的 Omron PLC 内存区点位。",
                Addressing = AcquisitionAddressingKinds.OmronFinsAddress,
                ProbeMode = AcquisitionProbeModes.ConfiguredPointsOnly,
                ConnectionSection = "omronFinsUdp",
                SupportsSourceTimestamp = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SupportsBitAddressing = true,
                SourceDataTypes = OmronFinsTypes,
                Constraints =
                [
                    "仅向单个配置主机和 UDP 端口发送 FINS MEMORY AREA READ；默认 PLC 端口为 9600，不扫描网络、不写入 PLC，也不执行 FINS 路由。",
                    "客户端节点号与 PLC 节点号必须和现场 FINS 网络配置一致，取值 1-254。",
                    "只支持用户明确配置的 CIO、WR、HR、AR 与 DM 点位；内存区范围由 PLC 型号和 CPU 配置决定。",
                    "支持 Boolean、16/32/64 位整数和 32/64 位浮点；字内固定高字节在前，多字点位可配置字序；当前不支持文本、计时器、计数器和 EM 区。"
                ]
            },
            [AcquisitionProtocols.MtConnect] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.MtConnect,
                DisplayName = "MTConnect",
                Summary = "按所选设备与 DataItem ID 读取 MTConnect Agent 的最新值快照。",
                Addressing = AcquisitionAddressingKinds.MtConnectDataItem,
                ProbeMode = AcquisitionProbeModes.Discover,
                ConnectionSection = "mtConnect",
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SourceDataTypes = DocumentTypes,
                SupportedWireEncodings = ["xml"],
                Constraints =
                [
                    "仅使用 HTTP GET 探查所选设备并轮询 /current；读取的是最新值快照，不保证多个 Observation 属于同一时刻。",
                    "Probe 提供 DataItem 的 category、type、subType、units 与组件路径；首期只把可转换的标量 SAMPLE/EVENT 作为 auto 点位。",
                    "首期固定使用现场节点接收时间，不支持源时间戳路径或源序号路径。",
                    "DataItem ID 区分大小写；单任务最多选择 128 个不同 DataItem，筛选路径预算不超过 8 KiB。",
                    "请求只读；首期接收 XML，响应体上限由连接配置限制在 16 MiB 以内。",
                    "Condition 与复杂/集合型 Observation 不是标量点位，不能映射为普通采样值。"
                ]
            },
            [AcquisitionProtocols.BacnetIp] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.BacnetIp,
                DisplayName = "BACnet/IP",
                Summary = "按指定 BACnet 设备实例，只读浏览对象目录并读取已映射对象的 Present_Value。",
                Addressing = AcquisitionAddressingKinds.BacnetObject,
                ProbeMode = AcquisitionProbeModes.Discover,
                ConnectionSection = "bacnetIp",
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SourceDataTypes = ["auto"],
                Constraints =
                [
                    "仅连接到显式配置的单个主机、UDP 端口和 Device Instance；使用单播，不发送 Who-Is 广播或网络扫描。",
                    "仅支持 analog、binary 和 multi-state input/output/value 对象的标量 Present_Value，只读，不写设备。",
                    "每个任务最多映射 128 个对象；探查只预览已映射对象，目录浏览不会逐一读取目录页中的值。",
                    "BACnet/IP 使用 UDP；目标主机仍须通过 Edge 私有网络或显式允许主机策略。"
                ]
            },
            [AcquisitionProtocols.BacnetMstp] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.BacnetMstp,
                DisplayName = "BACnet MS/TP",
                Summary = "通过允许的 RS-485 串口连接到指定设备实例，只读浏览对象目录与映射值。",
                Addressing = AcquisitionAddressingKinds.BacnetObject,
                ProbeMode = AcquisitionProbeModes.Discover,
                ConnectionSection = "bacnetMstp",
                SharesConnectionBetweenTasks = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SourceDataTypes = ["auto"],
                Constraints =
                [
                    "只连接配置的单个 BACnet Device Instance；Who-Is 仅在已选的本地 MS/TP 总线上按实例定向，不扫描 IP 网络。",
                    "RS-485 串口使用 8N1；总线波特率、现场站地址、Max_Master 和 Max_Info_Frames 必须与现场工程匹配。",
                    "同一串口可共享同一 MS/TP 会话，但不能同时用于 Modbus RTU、Modbus ASCII 或其他串行驱动。",
                    "只支持 analog、binary 和 multi-state input/output/value 对象的标量 Present_Value；不写设备。",
                    "每个任务最多映射 128 个对象，目录预览最多接收 512 个对象；端口必须加入 Edge AllowedSerialPorts。"
                ]
            },
            [AcquisitionProtocols.Snmp] = new AcquisitionProtocolCapability
            {
                Protocol = AcquisitionProtocols.Snmp,
                DisplayName = "SNMP",
                Summary = "通过单播 UDP 只读读取 SNMP v2c/v3 OID，并在显式子树内有限浏览数字 OID。",
                Addressing = AcquisitionAddressingKinds.SnmpOid,
                ProbeMode = AcquisitionProbeModes.Discover,
                ConnectionSection = "snmp",
                SupportsSourceTimestamp = true,
                SupportsSequencePath = true,
                SupportsConnectTimeout = true,
                SupportsReconnectDelay = true,
                SourceDataTypes = SnmpTypes,
                Constraints =
                [
                    "只支持固定 IPv4 单播目标和 UDP 单播；仅支持 v2c/v3，凭据通过 Edge 密钥引用提供，凭据目标必须显式加入 AllowedNetworkHosts。",
                    "运行时只发送 GET；OID 浏览只允许在连接配置指定的数字 OID 子树内使用 GETNEXT/GETBULK。",
                    "每任务最多读取 128 个不同 OID，每个 GET 最多 32 个 OID；子树浏览最多 512 个条目，每次请求最多 25 个 repetitions。",
                    "仅映射 auto、boolean、int32、uint32、uint64 和 string 标量；Counter 保留累计值，不生成速率。",
                    "禁止 SET、Trap/Inform 接收、广播、组播和网络扫描。"
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
