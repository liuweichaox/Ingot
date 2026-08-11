using System.Text.RegularExpressions;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Contracts.Acquisition;

/// <summary>一条定位到具体字段的配置错误。<see cref="Path"/> 与配置界面的字段路径一致。</summary>
public sealed record AcquisitionValidationError(string Path, string Message)
{
    public override string ToString() => string.IsNullOrEmpty(Path) ? Message : $"{Path}：{Message}";
}

/// <summary>
///     采集配置校验与规范化。
///     平台保存、边缘节点加载和配置界面共同使用这组规则：
///     <list type="bullet">
///       <item>平台保存、边缘启动、配置界面共用同一份判断；</item>
///       <item>错误定位到字段，而不是一整条字符串；</item>
///       <item><see cref="AcquisitionProtocolCapabilities"/> 裁决"这个协议是否真的支持这个字段"，
///             不支持就明确拒绝，而不是接受后丢弃。</item>
///     </list>
/// </summary>
public static partial class AcquisitionProfileValidator
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex EventTypePattern();

    /// <param name="model">
    ///     可选的工艺数据模型。传入时会交叉校验数据项引用与发布完整性；
    ///     边缘节点只做协议自洽校验时传 null。
    /// </param>
    public static bool TryValidate(
        AcquisitionProfile? value,
        ProcessDataModel? model,
        out AcquisitionProfile? normalized,
        out IReadOnlyList<AcquisitionValidationError> errors)
    {
        normalized = null;
        var found = new List<AcquisitionValidationError>();
        if (value is null)
        {
            errors = [new AcquisitionValidationError(string.Empty, "采集配置不能为空。")];
            return false;
        }

        var protocol = value.Protocol?.Trim().ToLowerInvariant();
        if (!AcquisitionProtocols.IsSupported(protocol) ||
            !AcquisitionProtocolCapabilities.TryGet(protocol, out var capability))
        {
            errors = [new AcquisitionValidationError("protocol", "采集协议不在已登记的驱动列表中。")];
            return false;
        }

        ValidateIdentity(value, found);
        ValidateConnection(value, protocol!, capability, found);
        ValidateExecution(value, capability, found);
        ValidateTimestamp(value, capability, found);

        var contextMappings = NormalizeContextMappings(value, capability, found);
        var valueMappings = NormalizeValueMappings(
            value.ValueMappings, protocol!, capability, "valueMappings", found);
        var processSpecification = NormalizeProcessSpecification(value.ProcessSpecification, protocol!, capability, found);
        var staticContext = NormalizeStaticContext(value, found);

        if (valueMappings.Count == 0)
            found.Add(new AcquisitionValidationError("valueMappings", "至少需要配置一个采集数据项。"));

        ValidateTopicBindings(value, capability, valueMappings, contextMappings, processSpecification, found);

        if (model is not null)
            ValidateAgainstModel(value, valueMappings, processSpecification, model, found);

        if (found.Count > 0)
        {
            errors = found;
            return false;
        }

        normalized = value with
        {
            ProfileId = NormalizeCode(value.ProfileId),
            Name = value.Name.Trim(),
            Status = value.Status!.Trim().ToLowerInvariant(),
            EdgeId = value.EdgeId.Trim(),
            Protocol = protocol!,
            DataModelId = NormalizeCode(value.DataModelId),
            Source = value.Source.Trim().TrimStart('/'),
            // 接入页面只面向一个清晰概念：设备。SubjectType 是事件契约内部字段，不让用户选择。
            SubjectType = "equipment",
            SubjectId = value.SubjectId.Trim(),
            Connection = value.Connection with
            {
                BaseUrl = value.Connection.BaseUrl.Trim().TrimEnd('/'),
                SnapshotPath = value.Connection.SnapshotPath.Trim()
            },
            Mqtt = NormalizeMqtt(value.Mqtt),
            OpcUa = NormalizeOpcUa(value.OpcUa),
            ModbusTcp = NormalizeModbusTcp(value.ModbusTcp),
            MelsecA1E = NormalizeMelsecA1E(value.MelsecA1E),
            TimestampMode = capability.SupportsSourceTimestamp
                ? value.TimestampMode.Trim().ToLowerInvariant()
                : "edge-received",
            TimestampPath = value.TimestampPath?.Trim() ?? string.Empty,
            SequencePath = capability.SupportsSequencePath && !string.IsNullOrWhiteSpace(value.SequencePath)
                ? value.SequencePath.Trim()
                : null,
            SampleEventType = value.SampleEventType.Trim(),
            StaticContext = staticContext,
            ContextMappings = contextMappings,
            ValueMappings = valueMappings,
            ProcessSpecification = processSpecification,
            Lifecycle = NormalizeLifecycle(value.Lifecycle),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        errors = [];
        return true;
    }

    // ------------------------------------------------------------- 身份与连接

    private static void ValidateIdentity(AcquisitionProfile value, List<AcquisitionValidationError> found)
    {
        if (!CodePattern().IsMatch(NormalizeCode(value.ProfileId)))
            found.Add(new AcquisitionValidationError("profileId", "接入配置代码只能包含小写字母、数字、点、下划线和短横线。"));
        if (value.Version < 1)
            found.Add(new AcquisitionValidationError("version", "版本号必须大于等于 1。"));
        if (string.IsNullOrWhiteSpace(value.Name))
            found.Add(new AcquisitionValidationError("name", "配置名称不能为空。"));
        if (!ConfigurationStatuses.IsValid(value.Status?.Trim().ToLowerInvariant()))
            found.Add(new AcquisitionValidationError("status", "状态必须是 draft、published 或 retired。"));
        if (string.IsNullOrWhiteSpace(value.EdgeId))
            found.Add(new AcquisitionValidationError("edgeId", "必须选择执行采集的现场节点。"));
        if (string.IsNullOrWhiteSpace(value.SubjectId))
            found.Add(new AcquisitionValidationError("subjectId", "设备编号不能为空。"));
        if (string.IsNullOrWhiteSpace(value.Source))
            found.Add(new AcquisitionValidationError("source", "事件来源不能为空。"));
        if (string.IsNullOrWhiteSpace(value.DataModelId))
            found.Add(new AcquisitionValidationError("dataModelId", "必须选择工艺数据模型。"));
        if (string.IsNullOrWhiteSpace(value.SampleEventType) ||
            !EventTypePattern().IsMatch(value.SampleEventType.Trim()))
            found.Add(new AcquisitionValidationError("sampleEventType", "采样事件类型格式无效，例如 process.sample。"));
    }

    private static void ValidateConnection(
        AcquisitionProfile value,
        string protocol,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        var section = capability.ConnectionSection;
        switch (protocol)
        {
            case AcquisitionProtocols.HttpPolling:
                if (!Uri.TryCreate(value.Connection.BaseUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                    found.Add(new AcquisitionValidationError($"{section}.baseUrl", "设备地址必须是 HTTP 或 HTTPS 绝对地址。"));
                if (string.IsNullOrWhiteSpace(value.Connection.SnapshotPath))
                    found.Add(new AcquisitionValidationError($"{section}.snapshotPath", "快照路径不能为空。"));
                if (value.Connection.PollIntervalMs < 1)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须大于 0ms。"));
                break;

            case AcquisitionProtocols.Mqtt:
                if (value.Mqtt is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 MQTT 连接参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.Mqtt.Host))
                    found.Add(new AcquisitionValidationError($"{section}.host", "消息服务器地址不能为空。"));
                if (value.Mqtt.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "端口必须在 1-65535 之间。"));
                if (value.Mqtt.ProtocolVersion is not ("3.1.1" or "5.0"))
                    found.Add(new AcquisitionValidationError($"{section}.protocolVersion", "协议版本必须是 3.1.1 或 5.0。"));
                if (value.Mqtt.KeepAliveSeconds < 1)
                    found.Add(new AcquisitionValidationError($"{section}.keepAliveSeconds", "保活时间必须大于 0 秒。"));
                if (value.Mqtt.SnapshotMaxAgeSeconds < 0)
                    found.Add(new AcquisitionValidationError(
                        $"{section}.snapshotMaxAgeSeconds", "快照最大陈旧时间不能为负数。"));
                if (value.Mqtt.UseTls && string.IsNullOrWhiteSpace(value.Mqtt.CaCertificatePath) &&
                    string.IsNullOrWhiteSpace(value.Mqtt.ClientCertificatePath))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.caCertificatePath",
                        "启用 TLS 时至少需要配置 CA 证书或客户端证书路径。"));
                if (value.Mqtt.Topics.Count == 0)
                {
                    found.Add(new AcquisitionValidationError($"{section}.topics", "至少需要一个订阅主题。"));
                    break;
                }

                var seenTopics = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < value.Mqtt.Topics.Count; index++)
                {
                    var topic = value.Mqtt.Topics[index];
                    var path = $"{section}.topics[{index}]";
                    if (string.IsNullOrWhiteSpace(topic.Topic))
                        found.Add(new AcquisitionValidationError($"{path}.topic", "主题不能为空。"));
                    else if (!IsValidMqttTopicFilter(topic.Topic.Trim(), out var topicError))
                        found.Add(new AcquisitionValidationError($"{path}.topic", topicError));
                    else if (!seenTopics.Add(topic.Topic.Trim()))
                        found.Add(new AcquisitionValidationError($"{path}.topic", "订阅主题不能重复。"));
                    if (topic.Qos is < 0 or > 2)
                        found.Add(new AcquisitionValidationError($"{path}.qos", "QoS 必须是 0、1 或 2。"));
                    if (!string.IsNullOrWhiteSpace(topic.PayloadRoot) && topic.PayloadRoot.Trim() != ".")
                        ValidateSelectorSyntax(
                            capability,
                            topic.PayloadRoot.Trim(),
                            $"{path}.payloadRoot",
                            found);
                }

                break;

            case AcquisitionProtocols.OpcUa:
                if (value.OpcUa is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 OPC UA 连接参数。"));
                    break;
                }

                if (!Uri.TryCreate(value.OpcUa.EndpointUrl, UriKind.Absolute, out var opcUri) ||
                    opcUri.Scheme is not ("opc.tcp" or "https"))
                    found.Add(new AcquisitionValidationError($"{section}.endpointUrl", "端点必须是 opc.tcp 或 HTTPS 绝对地址。"));
                if (value.OpcUa.PublishingIntervalMs < 1)
                    found.Add(new AcquisitionValidationError($"{section}.publishingIntervalMs", "发布周期必须大于 0ms。"));
                if (value.OpcUa.SamplingIntervalMs < 1)
                    found.Add(new AcquisitionValidationError($"{section}.samplingIntervalMs", "采样周期必须大于 0ms。"));
                if (value.OpcUa.AuthenticationType is not ("anonymous" or "username" or "certificate"))
                    found.Add(new AcquisitionValidationError($"{section}.authenticationType", "身份认证类型无效。"));
                if (value.OpcUa.SecurityMode is not ("none" or "sign" or "sign-and-encrypt"))
                    found.Add(new AcquisitionValidationError($"{section}.securityMode", "安全模式无效。"));
                if (value.OpcUa.SecurityPolicy is not ("None" or "Basic256Sha256" or
                        "Aes128_Sha256_RsaOaep" or "Aes256_Sha256_RsaPss"))
                    found.Add(new AcquisitionValidationError($"{section}.securityPolicy", "安全策略无效。"));
                if (value.OpcUa.SecurityMode != "none" && value.OpcUa.SecurityPolicy == "None")
                    found.Add(new AcquisitionValidationError(
                        $"{section}.securityPolicy", "启用签名或加密时必须选择一个具体的安全策略。"));
                if (value.OpcUa.SecurityMode != "none" &&
                    string.IsNullOrWhiteSpace(value.OpcUa.ClientCertificatePath))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.clientCertificatePath", "启用安全通道时必须配置客户端证书路径。"));
                if (value.OpcUa.AuthenticationType == "username" &&
                    string.IsNullOrWhiteSpace(value.OpcUa.Username))
                    found.Add(new AcquisitionValidationError($"{section}.username", "用户名认证需要填写用户名。"));
                if (value.OpcUa.AuthenticationType == "certificate" &&
                    string.IsNullOrWhiteSpace(value.OpcUa.ClientCertificatePath))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.clientCertificatePath", "证书认证需要配置客户端证书路径。"));
                break;

            case AcquisitionProtocols.ModbusTcp:
                if (value.ModbusTcp is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 Modbus TCP 连接参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.ModbusTcp.Host))
                    found.Add(new AcquisitionValidationError($"{section}.host", "设备地址不能为空。"));
                if (value.ModbusTcp.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "端口必须在 1-65535 之间。"));
                if (value.ModbusTcp.PollIntervalMs < 1)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须大于 0ms。"));
                if (value.ModbusTcp.AddressBase is not ("zero-based" or "one-based"))
                    found.Add(new AcquisitionValidationError($"{section}.addressBase", "地址起点必须是 0 基地址或 1 基地址。"));
                break;

            case AcquisitionProtocols.MelsecA1E:
                if (value.MelsecA1E is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 MELSEC 1E 连接参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.MelsecA1E.Host))
                    found.Add(new AcquisitionValidationError($"{section}.host", "PLC 地址不能为空。"));
                if (value.MelsecA1E.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "端口必须在 1-65535 之间。"));
                if (value.MelsecA1E.PollIntervalMs < 1)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须大于 0ms。"));
                if (value.MelsecA1E.WordOrderLayout != "A")
                    found.Add(new AcquisitionValidationError(
                        $"{section}.wordOrderLayout", "软元件字段顺序必须是 A（FX3U-ENET-ADP A-compatible 1E）。"));
                if (value.MelsecA1E.DataCode is not ("binary" or "ascii"))
                    found.Add(new AcquisitionValidationError($"{section}.dataCode", "通信数据码必须是二进制或 ASCII。"));
                if (value.MelsecA1E.MaxMergeGap is < 0 or > 256)
                    found.Add(new AcquisitionValidationError($"{section}.maxMergeGap", "合并读取间隔必须在 0-256 之间。"));
                break;
        }
    }

    /// <summary>
    ///     点位绑定的主题必须是本配置真正订阅了的过滤器之一。
    ///     绑定到没订阅的主题不会报错、也永远收不到数据，是最难排查的一类误配。
    /// </summary>
    private static void ValidateTopicBindings(
        AcquisitionProfile value,
        AcquisitionProtocolCapability capability,
        IReadOnlyList<AcquisitionValueMapping> valueMappings,
        IReadOnlyList<AcquisitionContextMapping> contextMappings,
        AcquisitionProcessSpecificationMapping? processSpecification,
        List<AcquisitionValidationError> found)
    {
        if (!capability.SupportsPerTopicMapping) return;
        var subscribed = (value.Mqtt?.Topics ?? [])
            .Select(static item => item.Topic?.Trim())
            .Where(static item => !string.IsNullOrEmpty(item))
            .ToHashSet(StringComparer.Ordinal);
        if (subscribed.Count == 0) return;
        for (var index = 0; index < valueMappings.Count; index++)
        {
            var topic = valueMappings[index].Topic;
            if (!string.IsNullOrEmpty(topic) && !subscribed.Contains(topic))
                found.Add(new AcquisitionValidationError(
                    $"valueMappings[{index}].topic",
                    $"点位绑定的主题 {topic} 不在订阅列表中，永远收不到数据。"));
        }

        for (var index = 0; index < contextMappings.Count; index++)
        {
            var topic = contextMappings[index].Topic;
            if (!string.IsNullOrEmpty(topic) && !subscribed.Contains(topic))
                found.Add(new AcquisitionValidationError(
                    $"contextMappings[{index}].topic",
                    $"上下文绑定的主题 {topic} 不在订阅列表中，永远收不到数据。"));
        }

        if (processSpecification is null) return;
        for (var index = 0; index < processSpecification.ParameterMappings.Count; index++)
        {
            var topic = processSpecification.ParameterMappings[index].Topic;
            if (!string.IsNullOrEmpty(topic) && !subscribed.Contains(topic))
                found.Add(new AcquisitionValidationError(
                    $"processSpecification.parameterMappings[{index}].topic",
                    $"控制参数绑定的主题 {topic} 不在订阅列表中，永远收不到数据。"));
        }
    }

    private static void ValidateExecution(
        AcquisitionProfile value,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        if (capability.SupportsConnectTimeout && value.Execution.TimeoutMs < 100)
            found.Add(new AcquisitionValidationError("execution.timeoutMs", "连接超时不能小于 100ms。"));
        if (capability.SupportsReconnectDelay && value.Execution.ReconnectDelayMs < 100)
            found.Add(new AcquisitionValidationError("execution.reconnectDelayMs", "重连间隔不能小于 100ms。"));
    }

    private static void ValidateTimestamp(
        AcquisitionProfile value,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        var mode = value.TimestampMode?.Trim().ToLowerInvariant();
        if (mode is not ("source" or "edge-received"))
        {
            found.Add(new AcquisitionValidationError("timestampMode", "时间戳模式必须是源数据时间或采集节点接收时间。"));
            return;
        }

        if (mode != "source") return;

        // 以前 OPC UA 与 MELSEC 会接受 source 然后在 Runner 里静默丢弃，
        // 导致采样时间与工程师配置的不一致却毫无提示。
        // 现在分两种处理：MELSEC 由 Runner 真正实现设备时间读取；
        // OPC UA 的采样时间由服务器 SourceTimestamp 决定，这里规范化为 edge-received
        // 并由界面显式说明，而不是保留一个不会生效的取值。
        if (!capability.SupportsSourceTimestamp) return;

        if (string.IsNullOrWhiteSpace(value.TimestampPath))
        {
            found.Add(new AcquisitionValidationError("timestampPath", "使用设备时间时必须指定时间来源。"));
            return;
        }

        if (capability.Addressing == AcquisitionAddressingKinds.ModbusRegister &&
            !AcquisitionSelectors.TryParseModbus(value.TimestampPath.Trim(), out _, out var modbusError))
            found.Add(new AcquisitionValidationError("timestampPath", modbusError));
        if (capability.Addressing == AcquisitionAddressingKinds.MelsecDevice &&
            !AcquisitionSelectors.TryParseMelsec(value.TimestampPath.Trim(), out _, out var melsecError))
            found.Add(new AcquisitionValidationError("timestampPath", melsecError));
    }

    // ------------------------------------------------------------- 映射

    private static IReadOnlyList<AcquisitionContextMapping> NormalizeContextMappings(
        AcquisitionProfile value,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        var result = new List<AcquisitionContextMapping>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = -1;
        foreach (var item in value.ContextMappings)
        {
            index++;
            if (string.IsNullOrWhiteSpace(item.ContextKey) && string.IsNullOrWhiteSpace(item.SourcePath))
                continue;
            var path = $"contextMappings[{index}]";
            var key = NormalizeCode(item.ContextKey);
            if (!CodePattern().IsMatch(key))
                found.Add(new AcquisitionValidationError($"{path}.contextKey", "上下文键格式无效。"));
            else if (!seen.Add(key))
                found.Add(new AcquisitionValidationError($"{path}.contextKey", "上下文键重复。"));
            var source = item.SourcePath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source))
                found.Add(new AcquisitionValidationError($"{path}.sourcePath", "设备来源不能为空。"));
            else
                ValidateSelectorSyntax(capability, source, $"{path}.sourcePath", found);
            if (!capability.SupportsPerTopicMapping && !string.IsNullOrWhiteSpace(item.Topic))
                found.Add(new AcquisitionValidationError(
                    $"{path}.topic", $"{capability.DisplayName} 不支持按主题绑定来源。"));
            result.Add(item with
            {
                ContextKey = key,
                SourcePath = source,
                Topic = capability.SupportsPerTopicMapping ? CleanOptional(item.Topic) : null
            });
        }

        return result;
    }

    private static IReadOnlyList<AcquisitionValueMapping> NormalizeValueMappings(
        IReadOnlyList<AcquisitionValueMapping> mappings,
        string protocol,
        AcquisitionProtocolCapability capability,
        string pathPrefix,
        List<AcquisitionValidationError> found)
    {
        var result = new List<AcquisitionValueMapping>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = -1;
        foreach (var raw in mappings)
        {
            index++;
            var path = $"{pathPrefix}[{index}]";
            if (string.IsNullOrWhiteSpace(raw.DataItemCode) && !HasAnyAddress(raw))
                continue;

            var code = NormalizeCode(raw.DataItemCode);
            if (!CodePattern().IsMatch(code))
                found.Add(new AcquisitionValidationError($"{path}.dataItemCode", "必须选择一个平台数据项。"));
            else if (!seen.Add(code))
                found.Add(new AcquisitionValidationError($"{path}.dataItemCode", "同一个数据项只能映射一次。"));

            if (!double.IsFinite(raw.Scale))
                found.Add(new AcquisitionValidationError($"{path}.scale", "换算倍率必须是有限数字。"));
            if (!double.IsFinite(raw.Offset))
                found.Add(new AcquisitionValidationError($"{path}.offset", "换算偏移必须是有限数字。"));

            if (!capability.SourceDataTypes.Contains(raw.SourceDataType))
                found.Add(new AcquisitionValidationError(
                    $"{path}.sourceDataType",
                    $"{capability.DisplayName} 不支持数据类型 {raw.SourceDataType}。"));

            if (!capability.SupportsPerTopicMapping && !string.IsNullOrWhiteSpace(raw.Topic))
                found.Add(new AcquisitionValidationError(
                    $"{path}.topic", $"{capability.DisplayName} 不支持按主题绑定点位。"));

            var mapping = raw with
            {
                DataItemCode = code,
                Topic = capability.SupportsPerTopicMapping ? CleanOptional(raw.Topic) : null
            };
            mapping = NormalizeAddressing(mapping, protocol, capability, path, found);
            result.Add(mapping);
        }

        return result;
    }

    /// <summary>
    ///     把结构化寻址字段与选择器字符串对齐。结构化字段优先；
    ///     只有旧版本配置（只有 SourcePath）才反向解析回结构化字段。
    /// </summary>
    private static AcquisitionValueMapping NormalizeAddressing(
        AcquisitionValueMapping mapping,
        string protocol,
        AcquisitionProtocolCapability capability,
        string path,
        List<AcquisitionValidationError> found)
    {
        switch (capability.Addressing)
        {
            case AcquisitionAddressingKinds.ModbusRegister:
            {
                var selector = mapping.ModbusAddress.HasValue && AcquisitionSelectors.IsModbusArea(mapping.ModbusArea)
                    ? AcquisitionSelectors.FormatModbus(mapping)
                    : mapping.SourcePath?.Trim() ?? string.Empty;
                if (!AcquisitionSelectors.TryParseModbus(selector, out var point, out var error))
                {
                    found.Add(new AcquisitionValidationError($"{path}.modbusAddress", error));
                    return mapping with { SourcePath = selector };
                }

                if (mapping.ModbusQuantity is < 1 or > 64)
                    found.Add(new AcquisitionValidationError($"{path}.modbusQuantity", "寄存器数量必须在 1-64 之间。"));
                return mapping with
                {
                    SourcePath = AcquisitionSelectors.FormatModbus(mapping with
                    {
                        ModbusArea = point.Area,
                        ModbusAddress = point.Address,
                        ModbusQuantity = point.Quantity,
                        SourceDataType = point.DataType,
                        ByteOrder = point.ByteOrder,
                        WordOrder = point.WordOrder,
                        BitIndex = point.BitIndex
                    }),
                    ModbusArea = point.Area,
                    ModbusAddress = point.Address,
                    ModbusQuantity = point.Quantity,
                    SourceDataType = point.DataType,
                    ByteOrder = point.ByteOrder,
                    WordOrder = point.WordOrder,
                    BitIndex = point.BitIndex,
                    MelsecDevice = null,
                    MelsecAddress = null
                };
            }

            case AcquisitionAddressingKinds.MelsecDevice:
            {
                var selector = !string.IsNullOrWhiteSpace(mapping.MelsecDevice) &&
                               !string.IsNullOrWhiteSpace(mapping.MelsecAddress)
                    ? AcquisitionSelectors.FormatMelsec(mapping)
                    : mapping.SourcePath?.Trim() ?? string.Empty;
                if (!AcquisitionSelectors.TryParseMelsec(selector, out var point, out var error))
                {
                    found.Add(new AcquisitionValidationError($"{path}.melsecAddress", error));
                    return mapping with { SourcePath = selector };
                }

                return mapping with
                {
                    SourcePath = selector,
                    MelsecDevice = point.Device.Code,
                    MelsecAddress = point.DisplayAddress,
                    SourceDataType = point.DataType,
                    ModbusQuantity = (ushort)Math.Max(1, point.WordCount),
                    BitIndex = point.BitIndex,
                    ModbusArea = null,
                    ModbusAddress = null
                };
            }

            default:
            {
                var source = mapping.SourcePath?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(source))
                    found.Add(new AcquisitionValidationError(
                        $"{path}.sourcePath",
                        capability.Addressing == AcquisitionAddressingKinds.NodeId ? "节点编号不能为空。" : "设备字段路径不能为空。"));
                else
                    ValidateSelectorSyntax(capability, source, $"{path}.sourcePath", found);
                if (mapping.BitIndex is not null && !capability.SupportsBitAddressing)
                    found.Add(new AcquisitionValidationError(
                        $"{path}.bitIndex", $"{capability.DisplayName} 不支持位提取。"));
                return mapping with
                {
                    SourcePath = source,
                    ModbusArea = null,
                    ModbusAddress = null,
                    MelsecDevice = null,
                    MelsecAddress = null
                };
            }
        }
    }

    private static void ValidateSelectorSyntax(
        AcquisitionProtocolCapability capability,
        string selector,
        string path,
        List<AcquisitionValidationError> found)
    {
        switch (capability.Addressing)
        {
            case AcquisitionAddressingKinds.ModbusRegister:
                if (!AcquisitionSelectors.TryParseModbus(selector, out _, out var modbusError))
                    found.Add(new AcquisitionValidationError(path, modbusError));
                break;
            case AcquisitionAddressingKinds.MelsecDevice:
                if (!AcquisitionSelectors.TryParseMelsec(selector, out _, out var melsecError))
                    found.Add(new AcquisitionValidationError(path, melsecError));
                break;
            case AcquisitionAddressingKinds.NodeId:
                if (!IsPlausibleNodeId(selector, out var nodeError))
                    found.Add(new AcquisitionValidationError(path, nodeError));
                break;
            case AcquisitionAddressingKinds.JsonPath:
                if (selector.StartsWith('.') || selector.EndsWith('.') || selector.Contains(".."))
                    found.Add(new AcquisitionValidationError(path, "JSON 字段路径不能以点开头或结尾，也不能包含连续的点。"));
                break;
        }
    }

    private static AcquisitionProcessSpecificationMapping? NormalizeProcessSpecification(
        AcquisitionProcessSpecificationMapping? processSpecification,
        string protocol,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        if (processSpecification is null) return null;
        if (string.IsNullOrWhiteSpace(processSpecification.IdPath))
            found.Add(new AcquisitionValidationError("processSpecification.idPath", "工艺规范编号来源不能为空。"));
        else
            ValidateSelectorSyntax(capability, processSpecification.IdPath.Trim(), "processSpecification.idPath", found);
        if (string.IsNullOrWhiteSpace(processSpecification.VersionPath))
            found.Add(new AcquisitionValidationError("processSpecification.versionPath", "工艺规范版本来源不能为空。"));
        else
            ValidateSelectorSyntax(capability, processSpecification.VersionPath.Trim(), "processSpecification.versionPath", found);
        if (!string.IsNullOrWhiteSpace(processSpecification.NamePath))
            ValidateSelectorSyntax(capability, processSpecification.NamePath.Trim(), "processSpecification.namePath", found);
        var processSpecificationEventType = processSpecification.EventType?.Trim() ?? string.Empty;
        if (!EventTypePattern().IsMatch(processSpecificationEventType))
            found.Add(new AcquisitionValidationError("processSpecification.eventType", "工艺规范事件类型格式无效，例如 process.specification.applied。"));

        // 只有文档类协议真正使用参数集合路径；以前对全部协议强制必填，是一个虚假约束。
        var trimmedParametersPath = processSpecification.ParametersPath?.Trim();
        var parametersPath = capability.SupportsControlParametersPath && !string.IsNullOrEmpty(trimmedParametersPath)
            ? trimmedParametersPath
            : ".";
        var parameters = NormalizeValueMappings(
            processSpecification.ParameterMappings, protocol, capability, "processSpecification.parameterMappings", found);
        return processSpecification with
        {
            EventType = processSpecificationEventType,
            IdPath = processSpecification.IdPath?.Trim() ?? string.Empty,
            VersionPath = processSpecification.VersionPath?.Trim() ?? string.Empty,
            NamePath = CleanOptional(processSpecification.NamePath),
            ParametersPath = parametersPath,
            ParameterMappings = parameters
        };
    }

    private static Dictionary<string, string> NormalizeStaticContext(
        AcquisitionProfile value,
        List<AcquisitionValidationError> found)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in value.StaticContext.Where(static pair =>
                     !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)))
        {
            var key = NormalizeCode(pair.Key);
            if (!CodePattern().IsMatch(key) || !result.TryAdd(key, pair.Value.Trim()))
                found.Add(new AcquisitionValidationError($"staticContext.{pair.Key}", "固定上下文键无效或重复。"));
        }

        return result;
    }

    private static void ValidateAgainstModel(
        AcquisitionProfile value,
        IReadOnlyList<AcquisitionValueMapping> valueMappings,
        AcquisitionProcessSpecificationMapping? processSpecification,
        ProcessDataModel model,
        List<AcquisitionValidationError> found)
    {
        var dataItems = model.Acquisition.DataItems.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        var parameters = model.ControlParameters.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        for (var index = 0; index < valueMappings.Count; index++)
        {
            if (!dataItems.ContainsKey(valueMappings[index].DataItemCode))
                found.Add(new AcquisitionValidationError(
                    $"valueMappings[{index}].dataItemCode",
                    $"数据项 {valueMappings[index].DataItemCode} 不属于所选工艺数据模型。"));
        }

        if (value.Status == ConfigurationStatuses.Published)
        {
            var mapped = valueMappings.Select(static item => item.DataItemCode).ToHashSet(StringComparer.Ordinal);
            foreach (var required in model.Acquisition.DataItems.Where(static item => !item.Nullable))
            {
                if (!mapped.Contains(required.Code))
                    found.Add(new AcquisitionValidationError(
                        "valueMappings",
                        $"发布前必须映射周期必需的数据项 {required.Code}。"));
            }
        }

        if (processSpecification is null) return;
        for (var index = 0; index < processSpecification.ParameterMappings.Count; index++)
        {
            if (!parameters.ContainsKey(processSpecification.ParameterMappings[index].DataItemCode))
                found.Add(new AcquisitionValidationError(
                    $"processSpecification.parameterMappings[{index}].dataItemCode",
                    $"控制参数 {processSpecification.ParameterMappings[index].DataItemCode} 不属于所选工艺数据模型。"));
        }
    }

    // ------------------------------------------------------------- 规范化辅助

    private static bool HasAnyAddress(AcquisitionValueMapping mapping)
        => !string.IsNullOrWhiteSpace(mapping.SourcePath) ||
           mapping.ModbusAddress.HasValue ||
           !string.IsNullOrWhiteSpace(mapping.MelsecAddress);

    /// <summary>MQTT 主题过滤器语法。规则由 <see cref="MqttTopicFilter"/> 统一提供。</summary>
    public static bool IsValidMqttTopicFilter(string topic, out string error)
        => MqttTopicFilter.IsValid(topic, out error);

    /// <summary>
    ///     OPC UA NodeId 文本形式的结构检查。真正的合法性由服务器裁决，
    ///     这里只拦住明显写错的形式，避免等到边缘节点连接设备才发现。
    /// </summary>
    public static bool IsPlausibleNodeId(string value, out string error)
    {
        error = string.Empty;
        var text = value.Trim();
        var body = text;
        if (text.StartsWith("ns=", StringComparison.Ordinal))
        {
            var separator = text.IndexOf(';');
            if (separator < 0)
            {
                error = "NodeId 缺少命名空间与标识之间的分号，例如 ns=2;s=Machine.Temperature。";
                return false;
            }

            if (!uint.TryParse(text.AsSpan(3, separator - 3), out _))
            {
                error = "NodeId 的命名空间序号必须是非负整数。";
                return false;
            }

            body = text[(separator + 1)..];
        }

        if (body.Length < 3 || body[1] != '=')
        {
            error = "NodeId 标识必须以 i=、s=、g= 或 b= 开头。";
            return false;
        }

        switch (body[0])
        {
            case 'i' when !uint.TryParse(body.AsSpan(2), out _):
                error = "数字型 NodeId 的标识必须是非负整数。";
                return false;
            case 'g' when !Guid.TryParse(body.AsSpan(2), out _):
                error = "GUID 型 NodeId 的标识不是合法的 GUID。";
                return false;
            case 'i' or 's' or 'g' or 'b':
                return true;
            default:
                error = "NodeId 标识类型必须是 i、s、g 或 b。";
                return false;
        }
    }

    private static MqttConnection? NormalizeMqtt(MqttConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            ClientId = value.ClientId.Trim(),
            Username = CleanOptional(value.Username),
            PasswordSecretRef = CleanOptional(value.PasswordSecretRef),
            CaCertificatePath = CleanOptional(value.CaCertificatePath),
            ClientCertificatePath = CleanOptional(value.ClientCertificatePath),
            ClientCertificatePasswordSecretRef = CleanOptional(value.ClientCertificatePasswordSecretRef),
            Topics = value.Topics
                .Where(static item => !string.IsNullOrWhiteSpace(item.Topic))
                .Select(static item => item with
                {
                    Topic = item.Topic.Trim(),
                    PayloadRoot = CleanOptional(item.PayloadRoot)
                })
                .ToArray()
        };

    private static OpcUaConnection? NormalizeOpcUa(OpcUaConnection? value)
        => value is null ? null : value with
        {
            EndpointUrl = value.EndpointUrl.Trim(),
            SecurityMode = value.SecurityMode.Trim().ToLowerInvariant(),
            SecurityPolicy = value.SecurityPolicy.Trim(),
            AuthenticationType = value.AuthenticationType.Trim().ToLowerInvariant(),
            Username = CleanOptional(value.Username),
            PasswordSecretRef = CleanOptional(value.PasswordSecretRef),
            ClientCertificatePath = CleanOptional(value.ClientCertificatePath),
            ClientCertificatePasswordSecretRef = CleanOptional(value.ClientCertificatePasswordSecretRef)
        };

    private static ModbusTcpConnection? NormalizeModbusTcp(ModbusTcpConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            AddressBase = value.AddressBase.Trim().ToLowerInvariant()
        };

    private static McA1EConnection? NormalizeMelsecA1E(McA1EConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            DataCode = value.DataCode.Trim().ToLowerInvariant(),
            WordOrderLayout = value.WordOrderLayout.Trim().ToUpperInvariant()
        };

    private static AcquisitionLifecycleMapping? NormalizeLifecycle(AcquisitionLifecycleMapping? value)
        => value is null ? null : value with
        {
            Mode = value.Mode.Trim().ToLowerInvariant(),
            ActiveContextKey = CleanOptional(value.ActiveContextKey),
            ActiveValue = value.ActiveValue?.Trim() ?? string.Empty
        };

    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string? CleanOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
