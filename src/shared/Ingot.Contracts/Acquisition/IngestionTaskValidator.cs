// 校验采集任务字段、协议约束、输入规模和秘密引用格式。
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Contracts.Acquisition;

public sealed record AcquisitionValidationError(string Path, string Message)
{
    public override string ToString() => string.IsNullOrEmpty(Path) ? Message : $"{Path}：{Message}";
}

public static partial class IngestionTaskValidator
{
    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$")]
    private static partial Regex CodePattern();

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:\.[a-z][a-z0-9_]*)+$")]
    private static partial Regex EventTypePattern();

    public static bool TryValidate(
        IngestionTask? value,
        ProcessDataModel? model,
        out IngestionTask? normalized,
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

        value = SanitizeNullCollectionsAndMembers(value, found);
        value = value with
        {
            DeviceProfile = NormalizeDeviceProfile(value.DeviceProfile, "deviceProfile", found)
        };
        if (!ValidateStructuralLimits(value, found))
        {
            errors = found;
            return false;
        }

        ValidateIdentity(value, found);
        ValidateConnection(value, protocol!, capability, found);
        ValidateExecution(value, capability, found);
        ValidateTimestamp(value, capability, found);
        if (protocol == AcquisitionProtocols.Snmp && !string.IsNullOrWhiteSpace(value.SequencePath))
            ValidateSelectorSyntax(capability, value.SequencePath.Trim(), "sequencePath", found);

        var contextMappings = NormalizeContextMappings(value, capability, found);
        var valueMappings = NormalizeValueMappings(
            value.ValueMappings, protocol!, capability, "valueMappings", found, value.MelsecMc3E?.Family);
        ValidateEtherNetIpPointTypes(value, valueMappings, capability, found);
        ValidateOmronFinsPointLimit(value, protocol!, found);
        var processSpecification = NormalizeProcessSpecification(
            value.ProcessSpecification, protocol!, capability, found, value.MelsecMc3E?.Family);
        ValidateMtConnectPointLimit(
            value with
            {
                ContextMappings = contextMappings,
                ValueMappings = valueMappings,
                ProcessSpecification = processSpecification
            },
            protocol!,
            found);
        ValidateBacnetPointLimit(
            value with
            {
                ContextMappings = contextMappings,
                ValueMappings = valueMappings,
                ProcessSpecification = processSpecification
            },
            protocol!,
            found);
        ValidateSnmpPointLimit(
            value with
            {
                ContextMappings = contextMappings,
                ValueMappings = valueMappings,
                ProcessSpecification = processSpecification
            },
            protocol!,
            found);
        var staticContext = NormalizeStaticContext(value, found);
        ValidateEquipmentContext(value, staticContext, contextMappings, found);

        if (valueMappings.Count == 0)
            found.Add(new AcquisitionValidationError("valueMappings", "至少需要配置一个采集数据项。"));

        ValidateTopicBindings(value, capability, valueMappings, contextMappings, processSpecification, found);
        ValidateLifecycle(value.Lifecycle, contextMappings, found);

        if (model is not null)
            ValidateAgainstModel(value, valueMappings, processSpecification, model, found);

        if (found.Count > 0)
        {
            errors = found;
            return false;
        }

        normalized = value with
        {
            TaskId = NormalizeCode(value.TaskId),
            Name = value.Name.Trim(),
            Status = value.Status!.Trim().ToLowerInvariant(),
            EdgeId = value.EdgeId.Trim(),
            Protocol = protocol!,
            DataModelId = NormalizeCode(value.DataModelId),
            Source = value.Source.Trim().TrimStart('/'),
            SubjectType = value.SubjectType.Trim().ToLowerInvariant(),
            SubjectId = value.SubjectId.Trim(),
            EquipmentId = CleanOptional(value.EquipmentId),
            HttpPolling = value.HttpPolling with
            {
                BaseUrl = value.HttpPolling.BaseUrl.Trim().TrimEnd('/'),
                SnapshotPath = value.HttpPolling.SnapshotPath.Trim(),
                Method = value.HttpPolling.Method.Trim().ToLowerInvariant(),
                ContentType = CleanOptional(value.HttpPolling.ContentType),
                RequestBody = CleanOptional(value.HttpPolling.RequestBody),
                Headers = NormalizeHeaders(value.HttpPolling.Headers),
                HeaderSecretRefs = NormalizeHeaders(value.HttpPolling.HeaderSecretRefs)
            },
            Mqtt = NormalizeMqtt(value.Mqtt),
            OpcUa = NormalizeOpcUa(value.OpcUa),
            ModbusTcp = NormalizeModbusTcp(value.ModbusTcp),
            ModbusRtu = NormalizeModbusRtu(value.ModbusRtu),
            ModbusAscii = NormalizeModbusAscii(value.ModbusAscii),
            MelsecA1E = NormalizeMelsecA1E(value.MelsecA1E),
            MelsecMc3E = NormalizeMelsecMc3E(value.MelsecMc3E),
            SiemensS7 = NormalizeSiemensS7(value.SiemensS7),
            EtherNetIp = NormalizeEtherNetIp(value.EtherNetIp),
            OmronFinsTcp = NormalizeOmronFinsTcp(value.OmronFinsTcp),
            OmronFinsUdp = NormalizeOmronFinsUdp(value.OmronFinsUdp),
            MtConnect = NormalizeMtConnect(value.MtConnect),
            BacnetIp = NormalizeBacnetIp(value.BacnetIp),
            BacnetMstp = NormalizeBacnetMstp(value.BacnetMstp),
            Snmp = NormalizeSnmp(value.Snmp),
            TimestampMode = capability.SupportsSourceTimestamp
                ? capability.UsesIntrinsicSourceTimestamp
                    ? "source"
                    : value.TimestampMode.Trim().ToLowerInvariant()
                : "edge-received",
            TimestampPath = capability.UsesIntrinsicSourceTimestamp
                ? string.Empty
                : capability.Addressing is AcquisitionAddressingKinds.MtConnectDataItem or AcquisitionAddressingKinds.BacnetObject
                    ? string.Empty
                    : NormalizeSelector(value.TimestampPath, capability, value.MelsecMc3E?.Family),
            TimestampEncoding = NormalizeTimestampEncoding(value.TimestampEncoding, capability),
            SequencePath = capability.SupportsSequencePath && !string.IsNullOrWhiteSpace(value.SequencePath)
                ? NormalizeSelector(value.SequencePath, capability, value.MelsecMc3E?.Family)
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

    private static IngestionTask SanitizeNullCollectionsAndMembers(
        IngestionTask value,
        List<AcquisitionValidationError> found)
    {
        var contextMappings = value.ContextMappings ?? [];
        var valueMappings = value.ValueMappings ?? [];
        if (contextMappings.Any(static item => item is null))
            found.Add(new AcquisitionValidationError("contextMappings", "上下文映射不能包含 null 项。"));
        if (valueMappings.Any(static item => item is null))
            found.Add(new AcquisitionValidationError("valueMappings", "数据项映射不能包含 null 项。"));
        var processMappings = value.ProcessSpecification?.ParameterMappings ?? [];
        if (processMappings.Any(static item => item is null))
            found.Add(new AcquisitionValidationError(
                "processSpecification.parameterMappings", "控制参数映射不能包含 null 项。"));

        return value with
        {
            TaskId = value.TaskId ?? string.Empty,
            Name = value.Name ?? string.Empty,
            Status = value.Status ?? string.Empty,
            EdgeId = value.EdgeId ?? string.Empty,
            Protocol = value.Protocol ?? string.Empty,
            DataModelId = value.DataModelId ?? string.Empty,
            Source = value.Source ?? string.Empty,
            SubjectType = value.SubjectType ?? string.Empty,
            SubjectId = value.SubjectId ?? string.Empty,
            EquipmentId = value.EquipmentId,
            DeviceProfile = value.DeviceProfile is null ? null : value.DeviceProfile with
            {
                Kind = value.DeviceProfile.Kind ?? string.Empty,
                Manufacturer = value.DeviceProfile.Manufacturer,
                Model = value.DeviceProfile.Model
            },
            SampleEventType = value.SampleEventType ?? string.Empty,
            TimestampMode = value.TimestampMode ?? string.Empty,
            TimestampPath = value.TimestampPath ?? string.Empty,
            TimestampEncoding = value.TimestampEncoding ?? string.Empty,
            Execution = value.Execution ?? new AcquisitionExecutionOptions(),
            HttpPolling = SanitizeHttp(value.HttpPolling),
            Mqtt = SanitizeMqtt(value.Mqtt, found),
            OpcUa = SanitizeOpcUa(value.OpcUa),
            ModbusTcp = SanitizeModbus(value.ModbusTcp),
            ModbusRtu = SanitizeModbusRtu(value.ModbusRtu),
            ModbusAscii = SanitizeModbusAscii(value.ModbusAscii),
            MelsecA1E = SanitizeMelsec(value.MelsecA1E),
            MelsecMc3E = SanitizeMelsecMc3E(value.MelsecMc3E),
            SiemensS7 = SanitizeSiemensS7(value.SiemensS7),
            EtherNetIp = SanitizeEtherNetIp(value.EtherNetIp),
            OmronFinsTcp = SanitizeOmronFinsTcp(value.OmronFinsTcp),
            OmronFinsUdp = SanitizeOmronFinsUdp(value.OmronFinsUdp),
            MtConnect = SanitizeMtConnect(value.MtConnect),
            BacnetIp = SanitizeBacnetIp(value.BacnetIp),
            BacnetMstp = SanitizeBacnetMstp(value.BacnetMstp),
            Snmp = SanitizeSnmp(value.Snmp),
            StaticContext = value.StaticContext ?? new Dictionary<string, string>(),
            ContextMappings = contextMappings.OfType<AcquisitionContextMapping>()
                .Select(static item => item with
                {
                    ContextKey = item.ContextKey ?? string.Empty,
                    SourcePath = item.SourcePath ?? string.Empty
                }).ToArray(),
            ValueMappings = valueMappings.OfType<AcquisitionValueMapping>().Select(SanitizeMapping).ToArray(),
            ProcessSpecification = value.ProcessSpecification is null ? null : value.ProcessSpecification with
            {
                EventType = value.ProcessSpecification.EventType ?? string.Empty,
                IdPath = value.ProcessSpecification.IdPath ?? string.Empty,
                VersionPath = value.ProcessSpecification.VersionPath ?? string.Empty,
                ParametersPath = value.ProcessSpecification.ParametersPath ?? string.Empty,
                ParameterMappings = processMappings.OfType<AcquisitionValueMapping>()
                    .Select(SanitizeMapping).ToArray()
            },
            Lifecycle = value.Lifecycle is null ? null : value.Lifecycle with
            {
                Mode = value.Lifecycle.Mode ?? string.Empty,
                ActiveValue = value.Lifecycle.ActiveValue ?? string.Empty,
                StartedEventType = value.Lifecycle.StartedEventType ?? string.Empty,
                CompletedEventType = value.Lifecycle.CompletedEventType ?? string.Empty,
                StepChangedEventType = value.Lifecycle.StepChangedEventType ?? string.Empty
            }
        };
    }

    internal static AcquisitionDeviceProfile? NormalizeDeviceProfile(
        AcquisitionDeviceProfile? value,
        string path,
        List<AcquisitionValidationError> found)
    {
        if (value is null) return null;
        var kind = value.Kind?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!AcquisitionDeviceKinds.IsSupported(kind))
            found.Add(new AcquisitionValidationError($"{path}.kind", "设备类别不在已登记的设备类型中。"));
        return value with
        {
            Kind = kind,
            Manufacturer = CleanOptional(value.Manufacturer),
            Model = CleanOptional(value.Model)
        };
    }

    private static HttpPollingConnection SanitizeHttp(HttpPollingConnection? value)
        => (value ?? new HttpPollingConnection()) with
        {
            BaseUrl = value?.BaseUrl ?? string.Empty,
            SnapshotPath = value?.SnapshotPath ?? string.Empty,
            Method = value?.Method ?? string.Empty,
            Headers = value?.Headers ?? new Dictionary<string, string>(),
            HeaderSecretRefs = value?.HeaderSecretRefs ?? new Dictionary<string, string>()
        };

    private static MqttConnection? SanitizeMqtt(
        MqttConnection? value,
        List<AcquisitionValidationError> found)
    {
        if (value is null) return null;
        var topics = value.Topics ?? [];
        if (topics.Any(static item => item is null))
            found.Add(new AcquisitionValidationError("mqtt.topics", "订阅主题不能包含 null 项。"));
        return value with
        {
            Host = value.Host ?? string.Empty,
            ProtocolVersion = value.ProtocolVersion ?? string.Empty,
            ClientId = value.ClientId ?? string.Empty,
            PayloadCompression = value.PayloadCompression ?? string.Empty,
            PayloadEncoding = value.PayloadEncoding ?? string.Empty,
            Topics = topics.OfType<MqttTopicSubscription>().Select(static item => item with
            {
                Topic = item.Topic ?? string.Empty,
                TopicVariables = item.TopicVariables ?? new Dictionary<string, int>()
            }).ToArray()
        };
    }

    private static OpcUaConnection? SanitizeOpcUa(OpcUaConnection? value)
        => value is null ? null : value with
        {
            EndpointUrl = value.EndpointUrl ?? string.Empty,
            SecurityMode = value.SecurityMode ?? string.Empty,
            SecurityPolicy = value.SecurityPolicy ?? string.Empty,
            AuthenticationType = value.AuthenticationType ?? string.Empty
        };

    private static ModbusTcpConnection? SanitizeModbus(ModbusTcpConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host ?? string.Empty,
            AddressBase = value.AddressBase ?? string.Empty
        };

    private static ModbusRtuConnection? SanitizeModbusRtu(ModbusRtuConnection? value)
        => value is null ? null : value with
        {
            PortName = value.PortName ?? string.Empty,
            Parity = value.Parity ?? string.Empty,
            StopBits = value.StopBits ?? string.Empty,
            AddressBase = value.AddressBase ?? string.Empty
        };

    private static ModbusAsciiConnection? SanitizeModbusAscii(ModbusAsciiConnection? value)
        => value is null ? null : value with
        {
            PortName = value.PortName ?? string.Empty,
            Parity = value.Parity ?? string.Empty,
            StopBits = value.StopBits ?? string.Empty,
            AddressBase = value.AddressBase ?? string.Empty
        };

    private static McA1EConnection? SanitizeMelsec(McA1EConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host ?? string.Empty,
            DataCode = value.DataCode ?? string.Empty,
            WordOrderLayout = value.WordOrderLayout ?? string.Empty
        };

    private static Mc3EConnection? SanitizeMelsecMc3E(Mc3EConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host ?? string.Empty,
            Family = value.Family?.Trim().ToLowerInvariant() ?? string.Empty
        };

    private static SiemensS7Connection? SanitizeSiemensS7(SiemensS7Connection? value)
        => value is null ? null : value with
        {
            Host = value.Host ?? string.Empty,
            CpuType = value.CpuType ?? string.Empty
        };

    private static EtherNetIpConnection? SanitizeEtherNetIp(EtherNetIpConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host ?? string.Empty,
            ControllerFamily = value.ControllerFamily ?? string.Empty,
            Path = value.Path ?? string.Empty
        };

    private static OmronFinsTcpConnection? SanitizeOmronFinsTcp(OmronFinsTcpConnection? value)
        => value is null ? null : value with { Host = value.Host ?? string.Empty };

    private static OmronFinsUdpConnection? SanitizeOmronFinsUdp(OmronFinsUdpConnection? value)
        => value is null ? null : value with { Host = value.Host ?? string.Empty };

    private static MtConnectConnection? SanitizeMtConnect(MtConnectConnection? value)
        => value is null ? null : value with
        {
            BaseUrl = value.BaseUrl ?? string.Empty,
            DeviceNameOrUuid = value.DeviceNameOrUuid ?? string.Empty,
            Headers = value.Headers ?? new Dictionary<string, string>(),
            HeaderSecretRefs = value.HeaderSecretRefs ?? new Dictionary<string, string>()
        };

    private static BacnetIpConnection? SanitizeBacnetIp(BacnetIpConnection? value)
        => value is null ? null : value with { Host = value.Host ?? string.Empty };

    private static BacnetMstpConnection? SanitizeBacnetMstp(BacnetMstpConnection? value)
        => value is null ? null : value with { PortName = value.PortName ?? string.Empty };

    private static SnmpConnection? SanitizeSnmp(SnmpConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host ?? string.Empty,
            Version = value.Version ?? string.Empty,
            BrowseRootOid = value.BrowseRootOid ?? string.Empty,
            CommunitySecretRef = value.CommunitySecretRef,
            UserName = value.UserName,
            SecurityLevel = value.SecurityLevel ?? string.Empty,
            AuthenticationProtocol = value.AuthenticationProtocol,
            AuthenticationSecretRef = value.AuthenticationSecretRef,
            PrivacyProtocol = value.PrivacyProtocol,
            PrivacySecretRef = value.PrivacySecretRef,
            ContextName = value.ContextName
        };

    private static AcquisitionValueMapping SanitizeMapping(AcquisitionValueMapping value)
        => value with
        {
            DataItemCode = value.DataItemCode ?? string.Empty,
            SourcePath = value.SourcePath ?? string.Empty,
            SourceDataType = value.SourceDataType ?? string.Empty,
            AcceptedQualityValues = value.AcceptedQualityValues ?? [],
            OutOfRangeBehavior = value.OutOfRangeBehavior ?? string.Empty,
            MissingValueBehavior = value.MissingValueBehavior ?? string.Empty
        };

    private static bool ValidateStructuralLimits(
        IngestionTask value,
        List<AcquisitionValidationError> found)
    {
        var initialErrorCount = found.Count;
        CheckLength(value.TaskId, 128, "taskId", "任务代码", found);
        CheckLength(value.Name, 256, "name", "配置名称", found);
        CheckLength(value.Status, 32, "status", "配置状态", found);
        CheckLength(value.EdgeId, 256, "edgeId", "现场节点编号", found);
        CheckLength(value.Protocol, 64, "protocol", "协议代码", found);
        CheckLength(value.DataModelId, 128, "dataModelId", "数据模型代码", found);
        CheckLength(value.Source, 512, "source", "事件来源", found);
        CheckLength(value.SubjectType, 128, "subjectType", "归属对象类型", found);
        CheckLength(value.SubjectId, 256, "subjectId", "归属对象编号", found);
        CheckLength(value.EquipmentId, 256, "equipmentId", "物理设备编号", found);
        if (value.DeviceProfile is { } deviceProfile)
        {
            CheckLength(deviceProfile.Kind, 64, "deviceProfile.kind", "设备类别", found);
            CheckLength(deviceProfile.Manufacturer, 128, "deviceProfile.manufacturer", "设备厂商", found);
            CheckLength(deviceProfile.Model, 128, "deviceProfile.model", "设备型号", found);
        }
        CheckLength(value.SampleEventType, 256, "sampleEventType", "采样事件类型", found);
        CheckLength(value.TimestampMode, 64, "timestampMode", "时间戳模式", found);
        CheckLength(value.TimestampPath, 2048, "timestampPath", "时间戳来源", found);
        CheckLength(value.TimestampEncoding, 64, "timestampEncoding", "时间戳编码", found);
        CheckLength(value.SequencePath, 2048, "sequencePath", "源序号来源", found);
        if (value.Snmp is { } snmp)
        {
            CheckLength(snmp.Host, 15, "snmp.host", "SNMP IPv4 地址", found);
            CheckLength(snmp.BrowseRootOid, 2048, "snmp.browseRootOid", "SNMP 浏览根 OID", found);
            CheckLength(snmp.CommunitySecretRef, 512, "snmp.communitySecretRef", "SNMP community 密钥引用", found);
            CheckLength(snmp.UserName, 128, "snmp.userName", "SNMPv3 用户名", found);
            CheckLength(snmp.AuthenticationSecretRef, 512, "snmp.authenticationSecretRef", "SNMPv3 认证密钥引用", found);
            CheckLength(snmp.PrivacySecretRef, 512, "snmp.privacySecretRef", "SNMPv3 隐私密钥引用", found);
            CheckLength(snmp.ContextName, 256, "snmp.contextName", "SNMPv3 Context Name", found);
        }
        CheckLength(value.HttpPolling.BaseUrl, 2048, "httpPolling.baseUrl", "HTTP 地址", found);
        CheckLength(value.HttpPolling.SnapshotPath, 2048, "httpPolling.snapshotPath", "HTTP 路径", found);
        CheckLength(value.HttpPolling.Method, 16, "httpPolling.method", "HTTP 方法", found);
        CheckLength(value.HttpPolling.ContentType, 256, "httpPolling.contentType", "HTTP 内容类型", found);
        CheckLength(value.HttpPolling.RequestBody, 1_048_576, "httpPolling.requestBody", "HTTP 请求体", found);
        if (value.HttpPolling.Headers.Count > 64 || value.HttpPolling.HeaderSecretRefs.Count > 64)
            found.Add(new AcquisitionValidationError("httpPolling.headers", "HTTP 请求头和密钥请求头分别最多 64 项。"));
        foreach (var (name, content) in value.HttpPolling.Headers.Concat(value.HttpPolling.HeaderSecretRefs))
        {
            CheckLength(name, 256, "httpPolling.headers", "HTTP 请求头名称", found);
            CheckLength(content, 8192, $"httpPolling.headers.{name}", "HTTP 请求头值", found);
        }
        if (value.MtConnect is { } mtConnect)
        {
            CheckLength(mtConnect.BaseUrl, 2048, "mtConnect.baseUrl", "MTConnect Agent 地址", found);
            CheckLength(mtConnect.DeviceNameOrUuid, 256, "mtConnect.deviceNameOrUuid", "MTConnect 设备名称或 UUID", found);
            if (mtConnect.Headers.Count > 64 || mtConnect.HeaderSecretRefs.Count > 64)
                found.Add(new AcquisitionValidationError("mtConnect.headers", "MTConnect 请求头和密钥请求头分别最多 64 项。"));
            foreach (var (name, content) in mtConnect.Headers.Concat(mtConnect.HeaderSecretRefs))
            {
                CheckLength(name, 256, "mtConnect.headers", "MTConnect 请求头名称", found);
                CheckLength(content, 8192, $"mtConnect.headers.{name}", "MTConnect 请求头值", found);
            }
        }
        if (value.BacnetIp is { } bacnetIp)
            CheckLength(bacnetIp.Host, 253, "bacnetIp.host", "BACnet/IP 设备地址", found);
        if (value.BacnetMstp is { } bacnetMstp)
            CheckLength(bacnetMstp.PortName, 260, "bacnetMstp.portName", "BACnet MS/TP 串口名", found);
        if (value.ContextMappings.Count > 1024)
            found.Add(new AcquisitionValidationError("contextMappings", "单个任务最多配置 1024 个动态上下文。"));
        if (value.ValueMappings.Count > 20_000)
            found.Add(new AcquisitionValidationError("valueMappings", "单个任务最多配置 20000 个数据项映射。"));
        if (value.StaticContext.Count > 512)
            found.Add(new AcquisitionValidationError("staticContext", "单个任务最多配置 512 个固定上下文。"));
        foreach (var (key, content) in value.StaticContext)
        {
            CheckLength(key, 128, "staticContext", "固定上下文键", found);
            CheckLength(content, 8192, $"staticContext.{key}", "固定上下文值", found);
        }
        foreach (var (mapping, index) in value.ContextMappings.Select((item, index) => (item, index)))
        {
            CheckLength(mapping.ContextKey, 128, $"contextMappings[{index}].contextKey", "上下文键", found);
            CheckLength(mapping.SourcePath, 2048, $"contextMappings[{index}].sourcePath", "上下文来源", found);
            CheckLength(mapping.Topic, 1024, $"contextMappings[{index}].topic", "上下文主题", found);
        }
        if (value.ProcessSpecification?.ParameterMappings.Count > 5_000)
            found.Add(new AcquisitionValidationError(
                "processSpecification.parameterMappings", "单个任务最多配置 5000 个控制参数映射。"));
        if (value.Mqtt is { } mqtt)
        {
            CheckLength(mqtt.Host, 255, "mqtt.host", "MQTT 主机名", found);
            CheckLength(mqtt.ProtocolVersion, 32, "mqtt.protocolVersion", "MQTT 协议版本", found);
            CheckLength(mqtt.ClientId, 256, "mqtt.clientId", "MQTT 客户端编号", found);
            CheckLength(mqtt.Username, 256, "mqtt.username", "MQTT 用户名", found);
            CheckLength(mqtt.PasswordSecretRef, 1024, "mqtt.passwordSecretRef", "MQTT 密码密钥引用", found);
            CheckLength(mqtt.CaCertificatePath, 2048, "mqtt.caCertificatePath", "MQTT CA 证书路径", found);
            CheckLength(mqtt.ClientCertificatePath, 2048, "mqtt.clientCertificatePath", "MQTT 客户端证书路径", found);
            CheckLength(mqtt.ClientCertificatePasswordSecretRef, 1024,
                "mqtt.clientCertificatePasswordSecretRef", "MQTT 证书密码密钥引用", found);
            if (mqtt.Topics.Count > 256)
                found.Add(new AcquisitionValidationError("mqtt.topics", "单个任务最多订阅 256 个 MQTT 主题。"));
            foreach (var (topic, index) in mqtt.Topics.Select((item, index) => (item, index)))
            {
                CheckLength(topic.Topic, 1024, $"mqtt.topics[{index}].topic", "MQTT 主题", found);
                CheckLength(topic.Channel, 128, $"mqtt.topics[{index}].channel", "MQTT 主题通道", found);
                CheckLength(topic.PayloadRoot, 2048, $"mqtt.topics[{index}].payloadRoot", "MQTT 报文根", found);
                if (topic.TopicVariables.Count > 64)
                    found.Add(new AcquisitionValidationError(
                        $"mqtt.topics[{index}].topicVariables", "单个主题最多提取 64 个上下文变量。"));
                foreach (var variable in topic.TopicVariables.Keys)
                    CheckLength(variable, 128, $"mqtt.topics[{index}].topicVariables", "MQTT 主题变量名", found);
            }
        }
        if (value.OpcUa is { } opcUa)
        {
            CheckLength(opcUa.EndpointUrl, 2048, "opcUa.endpointUrl", "OPC UA 端点", found);
            CheckLength(opcUa.SecurityMode, 64, "opcUa.securityMode", "OPC UA 安全模式", found);
            CheckLength(opcUa.SecurityPolicy, 256, "opcUa.securityPolicy", "OPC UA 安全策略", found);
            CheckLength(opcUa.AuthenticationType, 64, "opcUa.authenticationType", "OPC UA 认证类型", found);
            CheckLength(opcUa.Username, 256, "opcUa.username", "OPC UA 用户名", found);
            CheckLength(opcUa.PasswordSecretRef, 1024, "opcUa.passwordSecretRef", "OPC UA 密码密钥引用", found);
            CheckLength(opcUa.ClientCertificatePath, 2048, "opcUa.clientCertificatePath", "OPC UA 客户端证书路径", found);
            CheckLength(opcUa.ClientCertificatePasswordSecretRef, 1024,
                "opcUa.clientCertificatePasswordSecretRef", "OPC UA 证书密码密钥引用", found);
        }
        if (value.ModbusTcp is { } modbus)
            CheckLength(modbus.Host, 255, "modbusTcp.host", "Modbus 主机名", found);
        if (value.ModbusRtu is { } modbusRtu)
            CheckLength(modbusRtu.PortName, 260, "modbusRtu.portName", "Modbus RTU 串口名", found);
        if (value.ModbusAscii is { } modbusAscii)
            CheckLength(modbusAscii.PortName, 260, "modbusAscii.portName", "Modbus ASCII 串口名", found);
        if (value.MelsecA1E is { } melsec)
            CheckLength(melsec.Host, 255, "melsecA1E.host", "MELSEC 主机名", found);
        if (value.MelsecMc3E is { } melsecMc3E)
        {
            CheckLength(melsecMc3E.Host, 255, "melsecMc3E.host", "MELSEC MC 3E 主机名", found);
            CheckLength(melsecMc3E.Family, 64, "melsecMc3E.family", "MELSEC MC 3E 系列", found);
        }
        if (value.SiemensS7 is { } siemensS7)
        {
            CheckLength(siemensS7.Host, 255, "siemensS7.host", "Siemens S7 主机名", found);
            CheckLength(siemensS7.CpuType, 64, "siemensS7.cpuType", "Siemens S7 CPU 类型", found);
        }
        if (value.EtherNetIp is { } etherNetIp)
        {
            CheckLength(etherNetIp.Host, 255, "etherNetIp.host", "EtherNet/IP 主机名", found);
            CheckLength(etherNetIp.ControllerFamily, 64, "etherNetIp.controllerFamily", "EtherNet/IP 控制器系列", found);
            CheckLength(etherNetIp.Path, 128, "etherNetIp.path", "EtherNet/IP 路径", found);
        }
        if (value.OmronFinsTcp is { } omronFinsTcp)
            CheckLength(omronFinsTcp.Host, 255, "omronFinsTcp.host", "Omron FINS/TCP 主机名", found);
        if (value.OmronFinsUdp is { } omronFinsUdp)
            CheckLength(omronFinsUdp.Host, 255, "omronFinsUdp.host", "Omron FINS/UDP 主机名", found);
        if (value.ProcessSpecification is { } specification)
        {
            CheckLength(specification.EventType, 256, "processSpecification.eventType", "配方版本事件类型", found);
            CheckLength(specification.IdPath, 2048, "processSpecification.idPath", "配方版本编号来源", found);
            CheckLength(specification.VersionPath, 2048, "processSpecification.versionPath", "配方版本号来源", found);
            CheckLength(specification.NamePath, 2048, "processSpecification.namePath", "配方版本名称来源", found);
            CheckLength(specification.ParametersPath, 2048, "processSpecification.parametersPath", "工艺参数根", found);
        }
        if (value.Lifecycle is { } lifecycle)
        {
            CheckLength(lifecycle.Mode, 64, "lifecycle.mode", "生命周期模式", found);
            CheckLength(lifecycle.ActiveContextKey, 128, "lifecycle.activeContextKey", "生命周期激活上下文", found);
            CheckLength(lifecycle.ActiveCondition, 64, "lifecycle.activeCondition", "生命周期激活条件", found);
            CheckLength(lifecycle.ActiveValue, 1024, "lifecycle.activeValue", "生命周期激活值", found);
            CheckLength(lifecycle.StartedEventType, 256, "lifecycle.startedEventType", "开始事件类型", found);
            CheckLength(lifecycle.CompletedEventType, 256, "lifecycle.completedEventType", "完成事件类型", found);
            CheckLength(lifecycle.StepChangedEventType, 256, "lifecycle.stepChangedEventType", "阶段事件类型", found);
        }
        foreach (var (mapping, path) in value.ValueMappings.Select((item, index) => (item, $"valueMappings[{index}]"))
                     .Concat(value.ProcessSpecification?.ParameterMappings.Select(
                         (item, index) => (item, $"processSpecification.parameterMappings[{index}]")) ?? []))
        {
            CheckLength(mapping.DataItemCode, 128, $"{path}.dataItemCode", "数据项代码", found);
            CheckLength(mapping.SourcePath, 2048, $"{path}.sourcePath", "点位来源", found);
            CheckLength(mapping.SourceDataType, 64, $"{path}.sourceDataType", "源数据类型", found);
            CheckLength(mapping.SourceUnit, 128, $"{path}.sourceUnit", "源单位", found);
            CheckLength(mapping.QualityPath, 2048, $"{path}.qualityPath", "质量来源", found);
            CheckLength(mapping.DefaultValue, 8192, $"{path}.defaultValue", "默认值", found);
            CheckLength(mapping.ModbusArea, 64, $"{path}.modbusArea", "Modbus 寄存器区", found);
            CheckLength(mapping.ByteOrder, 64, $"{path}.byteOrder", "字节序", found);
            CheckLength(mapping.WordOrder, 64, $"{path}.wordOrder", "字序", found);
            CheckLength(mapping.MelsecDevice, 64, $"{path}.melsecDevice", "MELSEC 软元件", found);
            CheckLength(mapping.MelsecAddress, 128, $"{path}.melsecAddress", "MELSEC 地址", found);
            CheckLength(mapping.Topic, 1024, $"{path}.topic", "点位主题", found);
            if (mapping.AcceptedQualityValues.Count > 128)
                found.Add(new AcquisitionValidationError(
                    $"{path}.acceptedQualityValues", "单个点位最多配置 128 个可接受质量值。"));
            foreach (var quality in mapping.AcceptedQualityValues)
                CheckLength(quality, 512, $"{path}.acceptedQualityValues", "可接受质量值", found);
        }
        return found.Count == initialErrorCount;
    }

    private static void CheckLength(
        string? value,
        int maximum,
        string path,
        string label,
        List<AcquisitionValidationError> found)
    {
        if (value?.Length > maximum)
            found.Add(new AcquisitionValidationError(path, $"{label}不能超过 {maximum} 个字符。"));
    }

    private static void ValidateIdentity(IngestionTask value, List<AcquisitionValidationError> found)
    {
        if (!CodePattern().IsMatch(NormalizeCode(value.TaskId)))
            found.Add(new AcquisitionValidationError("taskId", "接入配置代码只能包含小写字母、数字、点、下划线和短横线。"));
        if (value.Version < 1)
            found.Add(new AcquisitionValidationError("version", "版本号必须大于等于 1。"));
        if (string.IsNullOrWhiteSpace(value.Name))
            found.Add(new AcquisitionValidationError("name", "配置名称不能为空。"));
        if (!ConfigurationStatuses.IsValid(value.Status?.Trim().ToLowerInvariant()))
            found.Add(new AcquisitionValidationError("status", "状态必须是 draft、published 或 retired。"));
        if (string.IsNullOrWhiteSpace(value.EdgeId))
            found.Add(new AcquisitionValidationError("edgeId", "必须选择执行采集的现场节点。"));
        if (string.IsNullOrWhiteSpace(value.SubjectId))
            found.Add(new AcquisitionValidationError("subjectId", "数据归属对象编号不能为空。"));
        if (!CodePattern().IsMatch(NormalizeCode(value.SubjectType)))
            found.Add(new AcquisitionValidationError("subjectType", "数据归属对象类型格式无效。"));
        if (value.EquipmentId is not null && string.IsNullOrWhiteSpace(value.EquipmentId))
            found.Add(new AcquisitionValidationError("equipmentId", "物理设备编号不能是空白。"));
        if (string.IsNullOrWhiteSpace(value.Source))
            found.Add(new AcquisitionValidationError("source", "事件来源不能为空。"));
        if (string.IsNullOrWhiteSpace(value.DataModelId))
            found.Add(new AcquisitionValidationError("dataModelId", "必须选择工艺数据模型。"));
        if (string.IsNullOrWhiteSpace(value.SampleEventType) ||
            !EventTypePattern().IsMatch(value.SampleEventType.Trim()))
            found.Add(new AcquisitionValidationError("sampleEventType", "采样事件类型格式无效，例如 process.sample。"));
    }

    private static void ValidateConnection(
        IngestionTask value,
        string protocol,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        var section = capability.ConnectionSection;
        switch (protocol)
        {
            case AcquisitionProtocols.HttpPolling:
                var method = value.HttpPolling.Method.Trim().ToLowerInvariant();
                if (!Uri.TryCreate(value.HttpPolling.BaseUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                    found.Add(new AcquisitionValidationError($"{section}.baseUrl", "设备地址必须是 HTTP 或 HTTPS 绝对地址。"));
                else if (!string.IsNullOrEmpty(uri.UserInfo) ||
                         !string.IsNullOrEmpty(uri.Query) ||
                         !string.IsNullOrEmpty(uri.Fragment))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.baseUrl", "设备基础地址不能包含用户名密码、查询参数或片段；请使用路径和密钥引用。"));
                if (string.IsNullOrWhiteSpace(value.HttpPolling.SnapshotPath))
                    found.Add(new AcquisitionValidationError($"{section}.snapshotPath", "快照路径不能为空。"));
                else if (Uri.TryCreate(value.HttpPolling.SnapshotPath, UriKind.Absolute, out var absolutePath) &&
                         absolutePath.Scheme is "http" or "https" ||
                         value.HttpPolling.SnapshotPath.StartsWith("//", StringComparison.Ordinal) ||
                         value.HttpPolling.SnapshotPath.StartsWith("\\\\", StringComparison.Ordinal) ||
                         value.HttpPolling.SnapshotPath.Contains('\r') ||
                         value.HttpPolling.SnapshotPath.Contains('\n'))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.snapshotPath",
                        "快照路径必须是相对于设备地址的路径，不能指定其他主机或包含换行符。"));
                if (method is not ("get" or "post"))
                    found.Add(new AcquisitionValidationError($"{section}.method", "请求方法必须是 get 或 post。"));
                if (method == "get" && !string.IsNullOrWhiteSpace(value.HttpPolling.RequestBody))
                    found.Add(new AcquisitionValidationError($"{section}.requestBody", "GET 请求不能配置请求体。"));
                ValidateHttpHeaders(value.HttpPolling.Headers, $"{section}.headers", allowSensitive: false, found);
                ValidateHttpHeaders(value.HttpPolling.HeaderSecretRefs, $"{section}.headerSecretRefs", allowSensitive: true, found);
                foreach (var (name, reference) in value.HttpPolling.HeaderSecretRefs)
                    ValidateSecretReference(reference, $"{section}.headerSecretRefs.{name}", found);
                foreach (var name in value.HttpPolling.Headers.Keys.Intersect(
                             value.HttpPolling.HeaderSecretRefs.Keys, StringComparer.OrdinalIgnoreCase))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.headers.{name}", "同一个请求头不能同时配置明文值和密钥引用。"));
                if (value.HttpPolling.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
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
                if (value.Mqtt.PayloadCompression is not ("none" or "gzip" or "deflate" or "brotli"))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.payloadCompression", "报文压缩格式必须是 none、gzip、deflate 或 brotli。"));
                if (value.Mqtt.PayloadEncoding is not ("utf-8" or "gbk" or "gb18030" or "big5"))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.payloadEncoding", "报文字符编码必须是 utf-8、gbk、gb18030 或 big5。"));
                if (value.Mqtt.KeepAliveSeconds is < 1 or > 65_535)
                    found.Add(new AcquisitionValidationError($"{section}.keepAliveSeconds", "保活时间必须是 1-65535 秒。"));
                if (value.Mqtt.SnapshotMaxAgeSeconds is < 0 or > 86_400)
                    found.Add(new AcquisitionValidationError(
                        $"{section}.snapshotMaxAgeSeconds", "快照最大陈旧时间必须是 0-86400 秒。"));
                if (value.Mqtt.Topics.Count > 1 && value.Mqtt.SnapshotMaxAgeSeconds <= 0)
                    found.Add(new AcquisitionValidationError(
                        $"{section}.snapshotMaxAgeSeconds", "订阅多个主题时必须设置大于 0 的快照最大陈旧时间。"));
                if (string.IsNullOrWhiteSpace(value.Mqtt.Username) &&
                    !string.IsNullOrWhiteSpace(value.Mqtt.PasswordSecretRef))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.passwordSecretRef", "配置密码密钥时必须同时配置用户名。"));
                ValidateOptionalSecretReference(
                    value.Mqtt.PasswordSecretRef,
                    $"{section}.passwordSecretRef",
                    found);
                ValidateOptionalSecretReference(
                    value.Mqtt.ClientCertificatePasswordSecretRef,
                    $"{section}.clientCertificatePasswordSecretRef",
                    found);
                if (value.Mqtt.Topics.Count == 0)
                {
                    found.Add(new AcquisitionValidationError($"{section}.topics", "至少需要一个订阅主题。"));
                    break;
                }

                var seenTopics = new HashSet<string>(StringComparer.Ordinal);
                var channels = new HashSet<string>(StringComparer.Ordinal);
                for (var index = 0; index < value.Mqtt.Topics.Count; index++)
                {
                    var topic = value.Mqtt.Topics[index];
                    if (!string.IsNullOrWhiteSpace(topic.Channel))
                    {
                        var channel = NormalizeCode(topic.Channel);
                        if (!CodePattern().IsMatch(channel))
                            found.Add(new AcquisitionValidationError(
                                $"{section}.topics[{index}].channel", "主题通道代码格式无效。"));
                        else if (!channels.Add(channel))
                            found.Add(new AcquisitionValidationError(
                                $"{section}.topics[{index}].channel", "主题通道代码不能重复。"));
                    }
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
                    foreach (var variable in topic.TopicVariables)
                    {
                        if (!CodePattern().IsMatch(NormalizeCode(variable.Key)))
                            found.Add(new AcquisitionValidationError(
                                $"{path}.topicVariables.{variable.Key}", "主题变量名格式无效。"));
                        if (variable.Value < 0)
                            found.Add(new AcquisitionValidationError(
                                $"{path}.topicVariables.{variable.Key}", "主题层级索引不能为负数。"));
                        else if (!string.IsNullOrWhiteSpace(topic.Topic) &&
                                 variable.Value >= topic.Topic.Split('/').Length)
                            found.Add(new AcquisitionValidationError(
                                $"{path}.topicVariables.{variable.Key}",
                                $"主题层级索引 {variable.Value} 超出过滤器的层级范围。"));
                    }
                }

                for (var left = 0; left < value.Mqtt.Topics.Count; left++)
                {
                    var first = value.Mqtt.Topics[left].Topic?.Trim();
                    if (!MqttTopicFilter.IsValid(first, out _)) continue;
                    for (var right = left + 1; right < value.Mqtt.Topics.Count; right++)
                    {
                        var second = value.Mqtt.Topics[right].Topic?.Trim();
                        if (!MqttTopicFilter.IsValid(second, out _) ||
                            !MqttTopicFilter.Intersects(first!, second!)) continue;
                        found.Add(new AcquisitionValidationError(
                            $"{section}.topics[{right}].topic",
                            $"订阅过滤器 {second} 与 {first} 会命中同一报文，无法确定报文根和主题通道。"));
                    }
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
                if (value.OpcUa.PublishingIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.publishingIntervalMs", "发布周期必须是 1-3600000ms。"));
                if (value.OpcUa.SamplingIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.samplingIntervalMs", "采样周期必须是 1-3600000ms。"));
                if (value.OpcUa.MaximumValueAgeMs is < 1 or > 86_400_000)
                    found.Add(new AcquisitionValidationError($"{section}.maximumValueAgeMs", "必需点位最大值龄必须是 1-86400000ms。"));
                if (value.OpcUa.MaximumTimestampSkewMs < 0 ||
                    value.OpcUa.MaximumTimestampSkewMs > value.OpcUa.MaximumValueAgeMs)
                    found.Add(new AcquisitionValidationError(
                        $"{section}.maximumTimestampSkewMs", "快照时间跨度必须在 0 到最大值龄之间。"));
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
                if (value.OpcUa.AuthenticationType == "username" &&
                    string.IsNullOrWhiteSpace(value.OpcUa.PasswordSecretRef))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.passwordSecretRef", "用户名认证必须引用现场密码密钥。"));
                if (value.OpcUa.AuthenticationType == "certificate" &&
                    string.IsNullOrWhiteSpace(value.OpcUa.ClientCertificatePath))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.clientCertificatePath", "证书认证需要配置客户端证书路径。"));
                ValidateOptionalSecretReference(
                    value.OpcUa.PasswordSecretRef,
                    $"{section}.passwordSecretRef",
                    found);
                ValidateOptionalSecretReference(
                    value.OpcUa.ClientCertificatePasswordSecretRef,
                    $"{section}.clientCertificatePasswordSecretRef",
                    found);
                if (value.Status == ConfigurationStatuses.Published && value.OpcUa.TrustServerCertificate)
                    found.Add(new AcquisitionValidationError(
                        $"{section}.trustServerCertificate",
                        "自动信任未登记服务器证书只允许用于草稿探查；发布前必须建立受信任证书链。"));
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
                if (value.ModbusTcp.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                if (value.ModbusTcp.AddressBase is not ("zero-based" or "one-based"))
                    found.Add(new AcquisitionValidationError($"{section}.addressBase", "地址起点必须是 0 基地址或 1 基地址。"));
                if (value.ModbusTcp.MaxMergeGap is < 0 or > 125)
                    found.Add(new AcquisitionValidationError($"{section}.maxMergeGap", "合并读取间隔必须在 0-125 之间。"));
                break;

            case AcquisitionProtocols.ModbusRtu:
                if (value.ModbusRtu is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 Modbus RTU 串口参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.ModbusRtu.PortName) || value.ModbusRtu.PortName.Any(char.IsControl))
                    found.Add(new AcquisitionValidationError($"{section}.portName", "串口名不能为空或包含控制字符。"));
                if (value.ModbusRtu.BaudRate is < 300 or > 4_000_000)
                    found.Add(new AcquisitionValidationError($"{section}.baudRate", "波特率必须在 300-4000000 之间。"));
                if (value.ModbusRtu.DataBits != 8)
                    found.Add(new AcquisitionValidationError($"{section}.dataBits", "Modbus RTU 固定使用 8 个数据位。"));
                if (value.ModbusRtu.Parity is not ("none" or "even" or "odd"))
                    found.Add(new AcquisitionValidationError($"{section}.parity", "校验位必须是 none、even 或 odd。"));
                if (value.ModbusRtu.StopBits is not ("one" or "two"))
                    found.Add(new AcquisitionValidationError($"{section}.stopBits", "停止位必须是 1 或 2。"));
                if (value.ModbusRtu.UnitId is < 1 or > 247)
                    found.Add(new AcquisitionValidationError($"{section}.unitId", "RTU 从站编号必须是 1-247，0 为广播地址，不能用于读取。"));
                if (value.ModbusRtu.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                if (value.ModbusRtu.AddressBase is not ("zero-based" or "one-based"))
                    found.Add(new AcquisitionValidationError($"{section}.addressBase", "地址起点必须是 0 基地址或 1 基地址。"));
                if (value.ModbusRtu.MaxMergeGap is < 0 or > 125)
                    found.Add(new AcquisitionValidationError($"{section}.maxMergeGap", "合并读取间隔必须在 0-125 之间。"));
                break;

            case AcquisitionProtocols.ModbusAscii:
                if (value.ModbusAscii is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 Modbus ASCII 串口参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.ModbusAscii.PortName) || value.ModbusAscii.PortName.Any(char.IsControl))
                    found.Add(new AcquisitionValidationError($"{section}.portName", "串口名不能为空或包含控制字符。"));
                if (value.ModbusAscii.BaudRate is < 300 or > 4_000_000)
                    found.Add(new AcquisitionValidationError($"{section}.baudRate", "波特率必须在 300-4000000 之间。"));
                if (value.ModbusAscii.DataBits != 7)
                    found.Add(new AcquisitionValidationError($"{section}.dataBits", "Modbus ASCII 使用 7 个数据位。"));
                if (value.ModbusAscii.Parity is not ("none" or "even" or "odd"))
                    found.Add(new AcquisitionValidationError($"{section}.parity", "校验位必须是 none、even 或 odd。"));
                if (value.ModbusAscii.StopBits is not ("one" or "two"))
                    found.Add(new AcquisitionValidationError($"{section}.stopBits", "停止位必须是 1 或 2。"));
                else if (value.ModbusAscii.Parity == "none" && value.ModbusAscii.StopBits != "two")
                    found.Add(new AcquisitionValidationError($"{section}.stopBits", "无校验时必须使用 2 个停止位。"));
                else if (value.ModbusAscii.Parity is "even" or "odd" && value.ModbusAscii.StopBits != "one")
                    found.Add(new AcquisitionValidationError($"{section}.stopBits", "启用奇偶校验时必须使用 1 个停止位。"));
                if (value.ModbusAscii.UnitId is < 1 or > 247)
                    found.Add(new AcquisitionValidationError($"{section}.unitId", "ASCII 从站编号必须是 1-247，0 为广播地址，不能用于读取。"));
                if (value.ModbusAscii.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                if (value.ModbusAscii.AddressBase is not ("zero-based" or "one-based"))
                    found.Add(new AcquisitionValidationError($"{section}.addressBase", "地址起点必须是 0 基地址或 1 基地址。"));
                if (value.ModbusAscii.MaxMergeGap is < 0 or > 125)
                    found.Add(new AcquisitionValidationError($"{section}.maxMergeGap", "合并读取间隔必须在 0-125 之间。"));
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
                if (value.MelsecA1E.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                if (value.MelsecA1E.MonitoringTimer == 0)
                    found.Add(new AcquisitionValidationError($"{section}.monitoringTimer", "PLC 监视定时器必须大于 0。"));
                if (value.MelsecA1E.WordOrderLayout != "A")
                    found.Add(new AcquisitionValidationError(
                        $"{section}.wordOrderLayout", "软元件字段顺序必须是 A（FX3U-ENET-ADP A-compatible 1E）。"));
                if (value.MelsecA1E.DataCode is not ("binary" or "ascii"))
                    found.Add(new AcquisitionValidationError($"{section}.dataCode", "通信数据码必须是二进制或 ASCII。"));
                if (value.MelsecA1E.MaxMergeGap is < 0 or > 256)
                    found.Add(new AcquisitionValidationError($"{section}.maxMergeGap", "合并读取间隔必须在 0-256 之间。"));
                break;

            case AcquisitionProtocols.MelsecMc3E:
                if (value.MelsecMc3E is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 MELSEC MC 3E / SLMP 连接参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.MelsecMc3E.Host))
                    found.Add(new AcquisitionValidationError($"{section}.host", "PLC 地址不能为空。"));
                if (value.MelsecMc3E.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "必须按目标 PLC 或网络模块工程显式填写 1-65535 之间的端口。"));
                if (!MelsecMc3EFamilies.IsSupported(value.MelsecMc3E.Family))
                    found.Add(new AcquisitionValidationError($"{section}.family", "PLC 系列必须是 q-l、iq-r 或 iq-f。"));
                if (value.MelsecMc3E.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                if (value.MelsecMc3E.MaxMergeGap is < 0 or > 256)
                    found.Add(new AcquisitionValidationError($"{section}.maxMergeGap", "连续地址合并间隔必须在 0-256 个字之间。"));
                break;

            case AcquisitionProtocols.SiemensS7:
                if (value.SiemensS7 is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 Siemens S7 连接参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.SiemensS7.Host))
                    found.Add(new AcquisitionValidationError($"{section}.host", "PLC 地址不能为空。"));
                if (value.SiemensS7.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "端口必须在 1-65535 之间。"));
                if (value.SiemensS7.CpuType is not ("S7200" or "S7200Smart" or "S7300" or "S7400" or "S71200" or "S71500" or "Logo0BA8"))
                    found.Add(new AcquisitionValidationError($"{section}.cpuType", "CPU 类型不在已登记的 Siemens S7 系列中。"));
                if (value.SiemensS7.Rack is < 0 or > 7)
                    found.Add(new AcquisitionValidationError($"{section}.rack", "机架号必须在 0-7 之间。"));
                if (value.SiemensS7.Slot is < 0 or > 31)
                    found.Add(new AcquisitionValidationError($"{section}.slot", "CPU 槽位必须在 0-31 之间。"));
                if (value.SiemensS7.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                if (value.SiemensS7.MaxMergeGap is < 0 or > 256)
                    found.Add(new AcquisitionValidationError($"{section}.maxMergeGap", "连续地址合并间隔必须在 0-256 字节之间。"));
                break;

            case AcquisitionProtocols.EtherNetIp:
                if (value.EtherNetIp is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 EtherNet/IP 连接参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.EtherNetIp.Host))
                    found.Add(new AcquisitionValidationError($"{section}.host", "PLC 地址不能为空。"));
                if (value.EtherNetIp.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "端口必须在 1-65535 之间。"));
                if (value.EtherNetIp.ControllerFamily is not ("control-logix" or "micro800"))
                    found.Add(new AcquisitionValidationError($"{section}.controllerFamily", "控制器系列必须是 ControlLogix/CompactLogix 或 Micro800。"));
                if (value.EtherNetIp.ControllerFamily == "control-logix" &&
                    !Regex.IsMatch(value.EtherNetIp.Path, @"^1,(?:[0-9]|[12][0-9]|3[01])$", RegexOptions.CultureInvariant))
                    found.Add(new AcquisitionValidationError($"{section}.path", "ControlLogix/CompactLogix 路径必须是 1,0 到 1,31 的直连背板槽位。"));
                if (value.EtherNetIp.ControllerFamily == "micro800" && !string.IsNullOrWhiteSpace(value.EtherNetIp.Path))
                    found.Add(new AcquisitionValidationError($"{section}.path", "Micro800 连接不使用路径；请留空。"));
                if (value.EtherNetIp.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                break;

            case AcquisitionProtocols.OmronFinsTcp:
                if (value.OmronFinsTcp is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 Omron FINS/TCP 连接参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.OmronFinsTcp.Host))
                    found.Add(new AcquisitionValidationError($"{section}.host", "PLC 地址不能为空。"));
                if (value.OmronFinsTcp.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "端口必须在 1-65535 之间。"));
                if (value.OmronFinsTcp.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                break;

            case AcquisitionProtocols.OmronFinsUdp:
                if (value.OmronFinsUdp is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 Omron FINS/UDP 连接参数。"));
                    break;
                }

                if (string.IsNullOrWhiteSpace(value.OmronFinsUdp.Host))
                    found.Add(new AcquisitionValidationError($"{section}.host", "PLC 地址不能为空。"));
                if (value.OmronFinsUdp.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "UDP 端口必须在 1-65535 之间。"));
                if (value.OmronFinsUdp.ClientNodeAddress is < 1 or > 254)
                    found.Add(new AcquisitionValidationError($"{section}.clientNodeAddress", "客户端 FINS 节点号必须在 1-254 之间。"));
                if (value.OmronFinsUdp.ServerNodeAddress is < 1 or > 254)
                    found.Add(new AcquisitionValidationError($"{section}.serverNodeAddress", "PLC FINS 节点号必须在 1-254 之间。"));
                if (value.OmronFinsUdp.ClientNodeAddress == value.OmronFinsUdp.ServerNodeAddress)
                    found.Add(new AcquisitionValidationError($"{section}.serverNodeAddress", "客户端与 PLC 的 FINS 节点号不能相同。"));
                if (value.OmronFinsUdp.PollIntervalMs is < 1 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "读取后等待时间必须是 1-3600000ms。"));
                break;

            case AcquisitionProtocols.MtConnect:
                if (value.MtConnect is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 MTConnect 连接参数。"));
                    break;
                }

                var baseUrl = value.MtConnect.BaseUrl;
                if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var mtConnectUri) ||
                    mtConnectUri.Scheme is not ("http" or "https") ||
                    string.IsNullOrWhiteSpace(mtConnectUri.Host))
                    found.Add(new AcquisitionValidationError($"{section}.baseUrl", "Agent 地址必须是 HTTP 或 HTTPS 绝对地址。"));
                else if (!string.IsNullOrEmpty(mtConnectUri.UserInfo) ||
                         !string.IsNullOrEmpty(mtConnectUri.Query) ||
                         !string.IsNullOrEmpty(mtConnectUri.Fragment) ||
                         baseUrl.Contains('?') || baseUrl.Contains('#'))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.baseUrl", "Agent 基础地址不能包含用户名密码、查询参数或片段；可保留反向代理路径前缀。"));
                if (baseUrl.Any(char.IsControl))
                    found.Add(new AcquisitionValidationError($"{section}.baseUrl", "Agent 地址不能包含控制字符。"));
                var selectedDevice = value.MtConnect.DeviceNameOrUuid;
                if (string.IsNullOrWhiteSpace(selectedDevice) ||
                    selectedDevice.Length > 256 ||
                    selectedDevice.Any(char.IsControl) ||
                    !string.Equals(selectedDevice, selectedDevice.Trim(), StringComparison.Ordinal) ||
                    selectedDevice is "." or ".." ||
                    selectedDevice.Contains('/') || selectedDevice.Contains('\\'))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.deviceNameOrUuid", "必须选择有效的设备名称或 UUID（最多 256 个字符，不能包含首尾空白、路径分隔符或控制字符）。"));
                if (value.MtConnect.PollIntervalMs is < 100 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "轮询间隔必须是 100-3600000ms。"));
                if (value.MtConnect.MaximumResponseBytes is < 1 or > 16 * 1024 * 1024)
                    found.Add(new AcquisitionValidationError($"{section}.maximumResponseBytes", "响应体上限必须是 1-16777216 字节。"));
                ValidateHttpHeaders(value.MtConnect.Headers, $"{section}.headers", allowSensitive: false, found);
                ValidateHttpHeaders(value.MtConnect.HeaderSecretRefs, $"{section}.headerSecretRefs", allowSensitive: true, found);
                foreach (var (name, reference) in value.MtConnect.HeaderSecretRefs)
                    ValidateSecretReference(reference, $"{section}.headerSecretRefs.{name}", found);
                foreach (var name in value.MtConnect.Headers.Keys.Intersect(
                             value.MtConnect.HeaderSecretRefs.Keys, StringComparer.OrdinalIgnoreCase))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.headers.{name}", "同一个请求头不能同时配置明文值和密钥引用。"));
                AddDuplicateHeaderErrors(value.MtConnect.Headers, $"{section}.headers", found);
                AddDuplicateHeaderErrors(value.MtConnect.HeaderSecretRefs, $"{section}.headerSecretRefs", found);
                if (value.MtConnect.Headers.Keys.Concat(value.MtConnect.HeaderSecretRefs.Keys)
                    .Any(static name => string.Equals(name, "Accept", StringComparison.OrdinalIgnoreCase)))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.headers.Accept", "MTConnect 固定使用 application/xml，不能配置自定义 Accept 请求头。"));
                break;

            case AcquisitionProtocols.BacnetIp:
                if (value.BacnetIp is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 BACnet/IP 连接参数。"));
                    break;
                }
                if (!TryParseUnicastIpv4(value.BacnetIp.Host, out _))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.host", "必须填写单播 IPv4 地址，不能使用主机名、广播、组播或本机地址。"));
                if (value.BacnetIp.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "UDP 端口必须在 1-65535 之间。"));
                if (value.BacnetIp.DeviceInstance > 4_194_302)
                    found.Add(new AcquisitionValidationError($"{section}.deviceInstance", "Device Instance 必须在 0-4194302 之间。"));
                if (value.BacnetIp.PollIntervalMs is < 100 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "轮询间隔必须是 100-3600000ms。"));
                break;

            case AcquisitionProtocols.BacnetMstp:
                if (value.BacnetMstp is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 BACnet MS/TP 连接参数。"));
                    break;
                }
                var mstp = value.BacnetMstp;
                if (!IsValidAcquisitionSerialPortName(mstp.PortName))
                    found.Add(new AcquisitionValidationError($"{section}.portName", "请填写 COMn、\\\\.\\COMn 或 /dev 下的串口路径。"));
                if (mstp.BaudRate is not (9600 or 19200 or 38400 or 76800 or 115200))
                    found.Add(new AcquisitionValidationError($"{section}.baudRate", "BACnet MS/TP 波特率必须是 9600、19200、38400、76800 或 115200。"));
                if (mstp.LocalStationAddress is null or < 0 or > 127)
                    found.Add(new AcquisitionValidationError($"{section}.localStationAddress", "现场 MS/TP 站地址必须在 0-127 之间，并且在总线上唯一。"));
                if (mstp.MaxMaster > 127)
                    found.Add(new AcquisitionValidationError($"{section}.maxMaster", "Max Master 必须在 0-127 之间。"));
                else if (mstp.LocalStationAddress is { } localAddress && localAddress > mstp.MaxMaster)
                    found.Add(new AcquisitionValidationError($"{section}.maxMaster", "Max Master 必须大于或等于本机 MS/TP 站地址。"));
                if (mstp.MaxInfoFrames == 0)
                    found.Add(new AcquisitionValidationError($"{section}.maxInfoFrames", "Max Info Frames 必须大于 0。"));
                if (mstp.DeviceInstance > 4_194_302)
                    found.Add(new AcquisitionValidationError($"{section}.deviceInstance", "Device Instance 必须在 0-4194302 之间。"));
                if (mstp.PollIntervalMs is < 100 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "轮询间隔必须是 100-3600000ms。"));
                break;

            case AcquisitionProtocols.Snmp:
                if (value.Snmp is null)
                {
                    found.Add(new AcquisitionValidationError(section, "缺少 SNMP 连接参数。"));
                    break;
                }

                if (!TryParseUnicastIpv4(value.Snmp.Host, out _))
                    found.Add(new AcquisitionValidationError(
                        $"{section}.host", "必须填写单播 IPv4 字面地址，不能使用主机名、广播、组播或本机地址。"));
                if (value.Snmp.Port is < 1 or > 65535)
                    found.Add(new AcquisitionValidationError($"{section}.port", "UDP 端口必须在 1-65535 之间。"));
                if (value.Snmp.PollIntervalMs is < 100 or > 3_600_000)
                    found.Add(new AcquisitionValidationError($"{section}.pollIntervalMs", "轮询间隔必须是 100-3600000ms。"));
                if (value.Snmp.Version is not ("v2c" or "v3"))
                    found.Add(new AcquisitionValidationError($"{section}.version", "SNMP 版本必须是 v2c 或 v3。"));
                if (!AcquisitionSelectors.TryParseSnmpOid(value.Snmp.BrowseRootOid, out _, out var browseOidError))
                    found.Add(new AcquisitionValidationError($"{section}.browseRootOid", browseOidError));
                ValidateOptionalSecretReference(value.Snmp.CommunitySecretRef, $"{section}.communitySecretRef", found);
                ValidateOptionalSecretReference(value.Snmp.AuthenticationSecretRef, $"{section}.authenticationSecretRef", found);
                ValidateOptionalSecretReference(value.Snmp.PrivacySecretRef, $"{section}.privacySecretRef", found);
                if (value.Snmp.Version == "v2c")
                {
                    if (string.IsNullOrWhiteSpace(value.Snmp.CommunitySecretRef))
                        found.Add(new AcquisitionValidationError($"{section}.communitySecretRef", "SNMP v2c 必须引用 Edge 现场 community 密钥。"));
                    if (!string.IsNullOrWhiteSpace(value.Snmp.UserName) ||
                        !string.IsNullOrWhiteSpace(value.Snmp.AuthenticationSecretRef) ||
                        !string.IsNullOrWhiteSpace(value.Snmp.PrivacySecretRef) ||
                        !string.IsNullOrWhiteSpace(value.Snmp.AuthenticationProtocol) ||
                        !string.IsNullOrWhiteSpace(value.Snmp.PrivacyProtocol) ||
                        !string.IsNullOrWhiteSpace(value.Snmp.ContextName))
                        found.Add(new AcquisitionValidationError($"{section}.version", "SNMP v2c 不能配置 v3 用户名、认证或隐私密钥。"));
                }
                else if (value.Snmp.Version == "v3")
                {
                    if (string.IsNullOrWhiteSpace(value.Snmp.UserName) ||
                        value.Snmp.UserName.Any(char.IsControl) ||
                        !string.Equals(value.Snmp.UserName, value.Snmp.UserName.Trim(), StringComparison.Ordinal))
                        found.Add(new AcquisitionValidationError($"{section}.userName", "SNMPv3 必须配置不含控制字符或首尾空格的用户名。"));
                    if (value.Snmp.SecurityLevel is not ("noAuthNoPriv" or "authNoPriv" or "authPriv"))
                        found.Add(new AcquisitionValidationError($"{section}.securityLevel", "安全级别必须是 noAuthNoPriv、authNoPriv 或 authPriv。"));
                    if (!string.IsNullOrWhiteSpace(value.Snmp.CommunitySecretRef))
                        found.Add(new AcquisitionValidationError($"{section}.communitySecretRef", "SNMP v3 不能配置 community 密钥。"));
                    if (value.Snmp.SecurityLevel == "noAuthNoPriv")
                    {
                        if (!string.IsNullOrWhiteSpace(value.Snmp.AuthenticationProtocol) ||
                            !string.IsNullOrWhiteSpace(value.Snmp.AuthenticationSecretRef) ||
                            !string.IsNullOrWhiteSpace(value.Snmp.PrivacyProtocol) ||
                            !string.IsNullOrWhiteSpace(value.Snmp.PrivacySecretRef))
                            found.Add(new AcquisitionValidationError($"{section}.securityLevel", "noAuthNoPriv 不能配置认证或隐私算法/密钥。"));
                    }
                    else if (value.Snmp.SecurityLevel is "authNoPriv" or "authPriv")
                    {
                        if (value.Snmp.AuthenticationProtocol is not ("sha256" or "sha384" or "sha512"))
                            found.Add(new AcquisitionValidationError($"{section}.authenticationProtocol", "认证算法必须是 SHA-256、SHA-384 或 SHA-512。"));
                        if (string.IsNullOrWhiteSpace(value.Snmp.AuthenticationSecretRef))
                            found.Add(new AcquisitionValidationError($"{section}.authenticationSecretRef", "认证级别必须引用认证密钥。"));
                        if (value.Snmp.SecurityLevel == "authNoPriv" &&
                            (!string.IsNullOrWhiteSpace(value.Snmp.PrivacyProtocol) ||
                             !string.IsNullOrWhiteSpace(value.Snmp.PrivacySecretRef)))
                            found.Add(new AcquisitionValidationError($"{section}.securityLevel", "authNoPriv 不能配置隐私算法或密钥。"));
                        if (value.Snmp.SecurityLevel == "authPriv")
                        {
                            if (value.Snmp.PrivacyProtocol is not ("aes128" or "aes192" or "aes256"))
                                found.Add(new AcquisitionValidationError($"{section}.privacyProtocol", "隐私算法必须是 AES-128、AES-192 或 AES-256。"));
                            if (string.IsNullOrWhiteSpace(value.Snmp.PrivacySecretRef))
                                found.Add(new AcquisitionValidationError($"{section}.privacySecretRef", "authPriv 必须引用隐私密钥。"));
                        }
                    }
                    if (value.Snmp.ContextName?.Any(char.IsControl) == true)
                        found.Add(new AcquisitionValidationError($"{section}.contextName", "Context Name 不能包含控制字符。"));
                }
                break;
        }
    }

    private static void ValidateTopicBindings(
        IngestionTask value,
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
        IngestionTask value,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        if (capability.SupportsConnectTimeout && value.Execution.TimeoutMs is < 1000 or > 300_000)
            found.Add(new AcquisitionValidationError("execution.timeoutMs", "连接与单次读取超时必须是 1000-300000ms。"));
        if (capability.SupportsReconnectDelay && value.Execution.ReconnectDelayMs is < 100 or > 300_000)
            found.Add(new AcquisitionValidationError("execution.reconnectDelayMs", "重连间隔必须是 100-300000ms。"));
        if (value.Execution.SourceIdentityStaleAfterMs is < 0 or > 86_400_000)
            found.Add(new AcquisitionValidationError(
                "execution.sourceIdentityStaleAfterMs", "源身份停滞阈值必须为 0-86400000ms。"));
        if (value.Execution.MaximumFutureTimestampSkewMs is < 0 or > 86_400_000)
            found.Add(new AcquisitionValidationError(
                "execution.maximumFutureTimestampSkewMs", "设备时间戳最大超前量必须为 0-86400000ms。"));
    }

    private static void ValidateLifecycle(
        AcquisitionLifecycleMapping? lifecycle,
        IReadOnlyList<AcquisitionContextMapping> contextMappings,
        List<AcquisitionValidationError> found)
    {
        if (lifecycle is null) return;
        if (!string.Equals(lifecycle.Mode?.Trim(), ProcessExecutionKinds.Discrete, StringComparison.OrdinalIgnoreCase))
            found.Add(new AcquisitionValidationError(
                "lifecycle.mode", $"过程执行模式必须是 {ProcessExecutionKinds.Discrete}。"));
        var activeCondition = lifecycle.ActiveCondition?.Trim().ToLowerInvariant();
        if (!AcquisitionLifecycleActiveConditions.IsSupported(activeCondition))
            found.Add(new AcquisitionValidationError(
                "lifecycle.activeCondition", "运行激活条件必须是 equals 或 nonzero。"));
        ValidateLifecycleEventType(lifecycle.StartedEventType, "lifecycle.startedEventType", found);
        ValidateLifecycleEventType(lifecycle.CompletedEventType, "lifecycle.completedEventType", found);
        ValidateLifecycleEventType(lifecycle.StepChangedEventType, "lifecycle.stepChangedEventType", found);

        var activeContextKey = CleanOptional(lifecycle.ActiveContextKey);
        if (activeContextKey is null)
        {
            found.Add(new AcquisitionValidationError(
                "lifecycle.activeContextKey", "离散过程执行必须指定运行激活状态上下文映射。"));
            return;
        }
        if (activeCondition == AcquisitionLifecycleActiveConditions.EqualTo &&
            string.IsNullOrWhiteSpace(lifecycle.ActiveValue))
            found.Add(new AcquisitionValidationError(
                "lifecycle.activeValue", "运行激活状态值不能为空。"));
        if (contextMappings.All(item => !string.Equals(
                item.ContextKey,
                activeContextKey,
                StringComparison.Ordinal)))
            found.Add(new AcquisitionValidationError(
                "lifecycle.activeContextKey", $"上下文映射中不存在运行激活状态键 {activeContextKey}。"));
    }

    private static void ValidateLifecycleEventType(
        string? eventType,
        string path,
        List<AcquisitionValidationError> found)
    {
        if (string.IsNullOrWhiteSpace(eventType) || !EventTypePattern().IsMatch(eventType.Trim()))
            found.Add(new AcquisitionValidationError(path, "事件类型格式无效。"));
    }

    private static void ValidateTimestamp(
        IngestionTask value,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        var mode = value.TimestampMode?.Trim().ToLowerInvariant();
        if (mode is not ("source" or "edge-received"))
        {
            found.Add(new AcquisitionValidationError("timestampMode", "时间戳模式必须是源数据时间或现场节点接收时间。"));
            return;
        }

        if (mode != "source" || !capability.SupportsSourceTimestamp || capability.UsesIntrinsicSourceTimestamp)
            return;

        var timestampEncoding = value.TimestampEncoding?.Trim().ToLowerInvariant();
        if (capability.Addressing == AcquisitionAddressingKinds.EthernetIpTag &&
            timestampEncoding == AcquisitionTimestampEncodings.Auto)
            timestampEncoding = AcquisitionTimestampEncodings.UnixMilliseconds;
        if (!AcquisitionTimestampEncodings.IsSupported(timestampEncoding))
            found.Add(new AcquisitionValidationError(
                "timestampEncoding", "时间戳编码必须是 auto、iso-8601、unix-s 或 unix-ms。"));

        if (string.IsNullOrWhiteSpace(value.TimestampPath))
        {
            found.Add(new AcquisitionValidationError("timestampPath", "使用设备时间时必须指定时间来源。"));
            return;
        }

        if (capability.Addressing == AcquisitionAddressingKinds.ModbusRegister)
        {
            if (!AcquisitionSelectors.TryParseModbus(value.TimestampPath.Trim(), out var point, out var modbusError))
                found.Add(new AcquisitionValidationError("timestampPath", modbusError));
            else
                ValidateRegisterTimestampWidth(point.DataType, timestampEncoding, "timestampPath", found);
        }
        if (capability.Addressing == AcquisitionAddressingKinds.MelsecDevice)
        {
            if (!AcquisitionSelectors.TryParseMelsec(value.TimestampPath.Trim(), out var point, out var melsecError))
                found.Add(new AcquisitionValidationError("timestampPath", melsecError));
            else
                ValidateRegisterTimestampWidth(point.DataType, timestampEncoding, "timestampPath", found);
        }
        if (capability.Addressing == AcquisitionAddressingKinds.MelsecMc3EDevice)
        {
            if (!AcquisitionSelectors.TryParseMelsecMc3E(
                    value.TimestampPath.Trim(), dataType: null, family: value.MelsecMc3E?.Family,
                    out var point, out var melsecMc3EError))
                found.Add(new AcquisitionValidationError("timestampPath", melsecMc3EError));
            else
                ValidateRegisterTimestampWidth(point.DataType, timestampEncoding, "timestampPath", found);
        }
        if (capability.Addressing == AcquisitionAddressingKinds.SiemensS7Address)
        {
            if (!AcquisitionSelectors.TryParseSiemensS7Address(
                    value.TimestampPath.Trim(), null, null, out var point, out var siemensError))
                found.Add(new AcquisitionValidationError("timestampPath", siemensError));
            else
                ValidateRegisterTimestampWidth(point.DataType, timestampEncoding, "timestampPath", found);
        }
        if (capability.Addressing == AcquisitionAddressingKinds.EthernetIpTag)
        {
            if (timestampEncoding is not (AcquisitionTimestampEncodings.UnixSeconds or AcquisitionTimestampEncodings.UnixMilliseconds))
                found.Add(new AcquisitionValidationError("timestampEncoding", "EtherNet/IP 设备时间只支持 unix-s 或 unix-ms 数值标签。"));
            if (!AcquisitionSelectors.TryParseEtherNetIpTag(value.TimestampPath.Trim(), out var tag, out var etherNetIpError))
                found.Add(new AcquisitionValidationError("timestampPath", etherNetIpError));
            else
            {
                var declaredType = value.ValueMappings
                    .Where(mapping => AcquisitionSelectors.TryParseEtherNetIpTag(
                        mapping.SourcePath, out var mappingTag, out _) &&
                        string.Equals(mappingTag, tag, StringComparison.Ordinal))
                    .Select(static mapping => mapping.SourceDataType)
                    .FirstOrDefault();
                var inferredType = timestampEncoding == AcquisitionTimestampEncodings.UnixSeconds ? "uint32" : "int64";
                ValidateRegisterTimestampWidth(
                    declaredType is null or "auto" ? inferredType : declaredType,
                    timestampEncoding,
                    "timestampPath",
                    found);
            }
        }
        if (capability.Addressing == AcquisitionAddressingKinds.OmronFinsAddress)
        {
            if (!AcquisitionSelectors.TryParseOmronFins(
                    value.TimestampPath.Trim(), dataType: null, wordOrder: null, out var point, out var finsError))
                found.Add(new AcquisitionValidationError("timestampPath", finsError));
            else
                ValidateRegisterTimestampWidth(point.DataType, timestampEncoding, "timestampPath", found);
        }
        if (capability.Addressing == AcquisitionAddressingKinds.SnmpOid)
        {
            if (!AcquisitionSelectors.TryParseSnmpOid(value.TimestampPath.Trim(), out var point, out var oidError))
                found.Add(new AcquisitionValidationError("timestampPath", oidError));
            else
            {
                var declaredType = value.ValueMappings
                    .Where(mapping => AcquisitionSelectors.TryParseSnmpOid(mapping.SourcePath, out var mapped, out _) &&
                                      string.Equals(mapped.Value, point.Value, StringComparison.Ordinal))
                    .Select(static mapping => mapping.SourceDataType)
                    .FirstOrDefault(static type => type is not null and not "auto");
                if (declaredType is not null && timestampEncoding != AcquisitionTimestampEncodings.Auto)
                    ValidateRegisterTimestampWidth(declaredType, timestampEncoding, "timestampPath", found);
            }
        }
    }

    private static void ValidateRegisterTimestampWidth(
        string dataType,
        string? encoding,
        string path,
        List<AcquisitionValidationError> found)
    {
        var normalizedEncoding = encoding == AcquisitionTimestampEncodings.Auto
            ? AcquisitionTimestampEncodings.UnixMilliseconds
            : encoding;
        var valid = normalizedEncoding switch
        {
            AcquisitionTimestampEncodings.UnixMilliseconds => dataType is "int64" or "uint64",
            AcquisitionTimestampEncodings.UnixSeconds => dataType is "int32" or "uint32" or "int64" or "uint64",
            AcquisitionTimestampEncodings.Iso8601 => dataType == "string",
            _ => true
        };
        if (!valid)
            found.Add(new AcquisitionValidationError(
                path,
                $"时间戳编码 {normalizedEncoding} 与点位数据类型 {dataType} 不匹配。"));
    }

    private static IReadOnlyList<AcquisitionContextMapping> NormalizeContextMappings(
        IngestionTask value,
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
            var source = NormalizeSelector(item.SourcePath, capability, value.MelsecMc3E?.Family);
            if (capability.Addressing == AcquisitionAddressingKinds.EthernetIpTag)
                found.Add(new AcquisitionValidationError(
                    $"{path}.sourcePath",
                    "EtherNet/IP 当前不支持未声明源数据类型的动态上下文标签；请使用固定上下文或改用已类型化的数据点。"));
            if (string.IsNullOrWhiteSpace(source))
                found.Add(new AcquisitionValidationError($"{path}.sourcePath", "设备来源不能为空。"));
            else
                ValidateSelectorSyntax(capability, source, $"{path}.sourcePath", found, value.MelsecMc3E?.Family);
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
        List<AcquisitionValidationError> found,
        string? melsecMc3EFamily = null)
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
            if (raw.Minimum.HasValue && !double.IsFinite(raw.Minimum.Value))
                found.Add(new AcquisitionValidationError($"{path}.minimum", "有效下限必须是有限数字。"));
            if (raw.Maximum.HasValue && !double.IsFinite(raw.Maximum.Value))
                found.Add(new AcquisitionValidationError($"{path}.maximum", "有效上限必须是有限数字。"));
            if (raw.Minimum.HasValue && raw.Maximum.HasValue && raw.Minimum > raw.Maximum)
                found.Add(new AcquisitionValidationError($"{path}.maximum", "有效上限不能小于有效下限。"));
            if (raw.OutOfRangeBehavior is not ("reject" or "clamp" or "omit"))
                found.Add(new AcquisitionValidationError(
                    $"{path}.outOfRangeBehavior", "越界处理必须是 reject、clamp 或 omit。"));
            if (raw.MissingValueBehavior is not ("inherit" or "reject" or "omit" or "use-default"))
                found.Add(new AcquisitionValidationError(
                    $"{path}.missingValueBehavior", "缺失处理必须是 inherit、reject、omit 或 use-default。"));
            if (raw.MissingValueBehavior == "use-default" && string.IsNullOrWhiteSpace(raw.DefaultValue))
                found.Add(new AcquisitionValidationError(
                    $"{path}.defaultValue", "缺失时使用默认值必须明确配置默认值。"));
            if (!string.IsNullOrWhiteSpace(raw.QualityPath))
            {
                var qualitySelector = capability.Addressing == AcquisitionAddressingKinds.MtConnectDataItem
                    ? raw.QualityPath
                    : raw.QualityPath.Trim();
                if (capability.Addressing == AcquisitionAddressingKinds.MtConnectDataItem)
                    found.Add(new AcquisitionValidationError(
                        $"{path}.qualityPath", "MTConnect 质量由观察值的 UNAVAILABLE 状态提供，首期不支持单独的质量 DataItem 路径。"));
                else if (capability.Addressing == AcquisitionAddressingKinds.EthernetIpTag)
                    found.Add(new AcquisitionValidationError(
                        $"{path}.qualityPath", "EtherNet/IP 当前不支持未声明类型的质量标签映射。"));
                else if (capability.Addressing == AcquisitionAddressingKinds.BacnetObject)
                    found.Add(new AcquisitionValidationError(
                        $"{path}.qualityPath", "BACnet/IP 首期不支持额外映射质量属性。"));
                else if (capability.Addressing == AcquisitionAddressingKinds.NodeId && raw.QualityPath != "$status")
                    found.Add(new AcquisitionValidationError(
                        $"{path}.qualityPath", "OPC UA 质量来源必须使用服务器状态标记 $status。"));
                else if (raw.QualityPath != "$status")
                    ValidateSelectorSyntax(
                        capability, qualitySelector, $"{path}.qualityPath", found, melsecMc3EFamily);
            }
            else if (raw.AcceptedQualityValues.Count > 0)
                found.Add(new AcquisitionValidationError(
                    $"{path}.qualityPath", "配置可接受质量值时必须指定质量字段来源。"));

            var registerAddressing = capability.Addressing is
                AcquisitionAddressingKinds.ModbusRegister or AcquisitionAddressingKinds.MelsecDevice or
                AcquisitionAddressingKinds.MelsecMc3EDevice or AcquisitionAddressingKinds.SiemensS7Address or AcquisitionAddressingKinds.EthernetIpTag or
                AcquisitionAddressingKinds.OmronFinsAddress or AcquisitionAddressingKinds.SnmpOid;
            if (!capability.SourceDataTypes.Contains(raw.SourceDataType))
                found.Add(new AcquisitionValidationError(
                    $"{path}.sourceDataType",
                    registerAddressing
                        ? $"{capability.DisplayName} 不支持数据类型 {raw.SourceDataType}。"
                        : $"{capability.DisplayName} 的设备数据类型由报文或服务器值决定，必须使用 auto。"));

            if (!capability.SupportsPerTopicMapping && !string.IsNullOrWhiteSpace(raw.Topic))
                found.Add(new AcquisitionValidationError(
                    $"{path}.topic", $"{capability.DisplayName} 不支持按主题绑定点位。"));

            var mapping = raw with
            {
                DataItemCode = code,
                SourceDataType = registerAddressing ? raw.SourceDataType : "auto",
                SourceUnit = CleanOptional(raw.SourceUnit),
                Topic = capability.SupportsPerTopicMapping ? CleanOptional(raw.Topic) : null,
                QualityPath = capability.Addressing is AcquisitionAddressingKinds.MtConnectDataItem or AcquisitionAddressingKinds.BacnetObject
                    ? null
                    : CleanOptional(NormalizeSelector(raw.QualityPath, capability, melsecMc3EFamily)),
                AcceptedQualityValues = raw.AcceptedQualityValues
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Select(static item => item.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                OutOfRangeBehavior = raw.OutOfRangeBehavior.Trim().ToLowerInvariant(),
                MissingValueBehavior = raw.MissingValueBehavior.Trim().ToLowerInvariant(),
                DefaultValue = CleanOptional(raw.DefaultValue)
            };
            mapping = NormalizeAddressing(mapping, protocol, capability, path, found, melsecMc3EFamily);
            result.Add(mapping);
        }

        return result;
    }

    private static AcquisitionValueMapping NormalizeAddressing(
        AcquisitionValueMapping mapping,
        string protocol,
        AcquisitionProtocolCapability capability,
        string path,
        List<AcquisitionValidationError> found,
        string? melsecMc3EFamily)
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
                            SourceByteLength = point.ByteLength,
                            SourceDataType = point.DataType,
                            ByteOrder = point.ByteOrder,
                            WordOrder = point.WordOrder,
                            BitIndex = point.BitIndex
                        }),
                        ModbusArea = point.Area,
                        ModbusAddress = point.Address,
                        ModbusQuantity = point.Quantity,
                        SourceByteLength = point.ByteLength,
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
                        SourceByteLength = point.ByteLength,
                        BitIndex = point.BitIndex,
                        ModbusArea = null,
                        ModbusAddress = null
                    };
                }

            case AcquisitionAddressingKinds.MelsecMc3EDevice:
                {
                    var selector = mapping.SourcePath?.Trim() ?? string.Empty;
                    if (!AcquisitionSelectors.TryParseMelsecMc3E(
                            selector, mapping.SourceDataType, melsecMc3EFamily, out var point, out var error))
                    {
                        found.Add(new AcquisitionValidationError($"{path}.sourcePath", error));
                        return mapping with { SourcePath = selector };
                    }

                    return mapping with
                    {
                        SourcePath = AcquisitionSelectors.FormatMelsecMc3E(point),
                        SourceDataType = point.DataType,
                        ModbusArea = null,
                        ModbusAddress = null,
                        ModbusQuantity = (ushort)Math.Max(1, point.WordCount),
                        SourceByteLength = null,
                        MelsecDevice = null,
                        MelsecAddress = null,
                        BitIndex = point.BitIndex
                    };
                }

            case AcquisitionAddressingKinds.SiemensS7Address:
                {
                    var source = mapping.SourcePath?.Trim() ?? string.Empty;
                    if (!AcquisitionSelectors.TryParseSiemensS7Address(
                            source, mapping.SourceDataType, mapping.SourceByteLength, out var point, out var error))
                    {
                        found.Add(new AcquisitionValidationError($"{path}.sourcePath", error));
                        return mapping with { SourcePath = source };
                    }

                    return mapping with
                    {
                        SourcePath = source.ToUpperInvariant(),
                        SourceDataType = point.DataType,
                        SourceByteLength = point.DataType == "string"
                            ? (ushort)(point.ByteCount - 2)
                            : null,
                        ModbusArea = null,
                        ModbusAddress = null,
                        ModbusQuantity = 1,
                        MelsecDevice = null,
                        MelsecAddress = null,
                        BitIndex = null
                    };
                }

            case AcquisitionAddressingKinds.EthernetIpTag:
                {
                    var source = mapping.SourcePath?.Trim() ?? string.Empty;
                    if (!AcquisitionSelectors.TryParseEtherNetIpTag(source, out var tag, out var error))
                    {
                        found.Add(new AcquisitionValidationError($"{path}.sourcePath", error));
                        return mapping with { SourcePath = source };
                    }

                    return mapping with
                    {
                        SourcePath = tag,
                        SourceByteLength = null,
                        ModbusArea = null,
                        ModbusAddress = null,
                        ModbusQuantity = 1,
                        MelsecDevice = null,
                        MelsecAddress = null,
                        BitIndex = null
                    };
                }

            case AcquisitionAddressingKinds.OmronFinsAddress:
                {
                    var source = mapping.SourcePath?.Trim() ?? string.Empty;
                    if (!AcquisitionSelectors.TryParseOmronFins(
                            source, mapping.SourceDataType, mapping.WordOrder, out var point, out var error))
                    {
                        found.Add(new AcquisitionValidationError($"{path}.sourcePath", error));
                        return mapping with { SourcePath = source };
                    }

                    return mapping with
                    {
                        SourcePath = AcquisitionSelectors.FormatOmronFins(point),
                        SourceDataType = point.DataType,
                        SourceByteLength = null,
                        ModbusArea = null,
                        ModbusAddress = null,
                        ModbusQuantity = 1,
                        MelsecDevice = null,
                        MelsecAddress = null,
                        BitIndex = point.BitIndex,
                        WordOrder = point.WordOrder
                    };
                }

            case AcquisitionAddressingKinds.MtConnectDataItem:
                {
                    var selector = mapping.SourcePath ?? string.Empty;
                    if (!AcquisitionSelectors.TryParseMtConnectDataItem(selector, out var point, out var error))
                    {
                        found.Add(new AcquisitionValidationError($"{path}.sourcePath", error));
                        return mapping with { SourcePath = selector };
                    }

                    return mapping with
                    {
                        SourcePath = AcquisitionSelectors.FormatMtConnectDataItem(point),
                        SourceDataType = "auto",
                        SourceByteLength = null,
                        ModbusArea = null,
                        ModbusAddress = null,
                        ModbusQuantity = 1,
                        MelsecDevice = null,
                        MelsecAddress = null,
                        BitIndex = null
                    };
                }

            case AcquisitionAddressingKinds.BacnetObject:
                {
                    var source = mapping.SourcePath?.Trim() ?? string.Empty;
                    if (!AcquisitionSelectors.TryParseBacnetObject(source, out var point, out var error))
                    {
                        found.Add(new AcquisitionValidationError($"{path}.sourcePath", error));
                        return mapping with { SourcePath = source };
                    }

                    return mapping with
                    {
                        SourcePath = AcquisitionSelectors.FormatBacnetObject(point),
                        SourceDataType = "auto",
                        SourceByteLength = null,
                        ModbusArea = null,
                        ModbusAddress = null,
                        ModbusQuantity = 1,
                        MelsecDevice = null,
                        MelsecAddress = null,
                        BitIndex = null,
                        QualityPath = null
                    };
                }

            case AcquisitionAddressingKinds.SnmpOid:
                {
                    var source = mapping.SourcePath?.Trim() ?? string.Empty;
                    if (!AcquisitionSelectors.TryParseSnmpOid(source, out var point, out var error))
                    {
                        found.Add(new AcquisitionValidationError($"{path}.sourcePath", error));
                        return mapping with { SourcePath = source };
                    }

                    return mapping with
                    {
                        SourcePath = AcquisitionSelectors.FormatSnmpOid(point),
                        SourceByteLength = null,
                        ModbusArea = null,
                        ModbusAddress = null,
                        ModbusQuantity = 1,
                        MelsecDevice = null,
                        MelsecAddress = null,
                        BitIndex = null,
                        ByteOrder = "big-endian",
                        WordOrder = "high-low"
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
                        ValidateSelectorSyntax(capability, source, $"{path}.sourcePath", found, melsecMc3EFamily);
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

    private static void ValidateEtherNetIpPointTypes(
        IngestionTask task,
        IReadOnlyList<AcquisitionValueMapping> mappings,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found)
    {
        if (capability.Addressing != AcquisitionAddressingKinds.EthernetIpTag)
            return;

        var configuredTags = mappings.Select(static mapping => mapping.SourcePath)
            .Concat(task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath)
                ? [task.TimestampPath]
                : [])
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Trim())
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (configuredTags > 256)
            found.Add(new AcquisitionValidationError(
                "valueMappings",
                $"EtherNet/IP 单任务最多读取 256 个不同标签，当前配置 {configuredTags} 个。"));

        foreach (var group in mappings.Select((mapping, index) => (mapping, index))
                     .GroupBy(item => item.mapping.SourcePath, StringComparer.Ordinal))
        {
            var types = group.Select(item => item.mapping.SourceDataType).Distinct(StringComparer.Ordinal).ToArray();
            if (types.Length <= 1)
                continue;
            foreach (var item in group)
                found.Add(new AcquisitionValidationError(
                    $"valueMappings[{item.index}].sourceDataType",
                    $"EtherNet/IP 标签 {group.Key} 在同一任务中不能声明多个源数据类型：{string.Join("、", types)}。"));
        }
    }

    private static void ValidateOmronFinsPointLimit(
        IngestionTask task,
        string protocol,
        List<AcquisitionValidationError> found)
    {
        if (protocol is not (AcquisitionProtocols.OmronFinsTcp or AcquisitionProtocols.OmronFinsUdp)) return;
        var protocolName = protocol == AcquisitionProtocols.OmronFinsUdp ? "Omron FINS/UDP" : "Omron FINS/TCP";
        var declarations = new List<(string Path, string? Selector, string? DataType, string? WordOrder)>();
        for (var index = 0; index < task.ValueMappings.Count; index++)
        {
            var mapping = task.ValueMappings[index];
            declarations.Add(($"valueMappings[{index}].sourcePath", mapping.SourcePath, mapping.SourceDataType, mapping.WordOrder));
            declarations.Add(($"valueMappings[{index}].qualityPath", mapping.QualityPath, null, null));
        }
        for (var index = 0; index < task.ContextMappings.Count; index++)
            declarations.Add(($"contextMappings[{index}].sourcePath", task.ContextMappings[index].SourcePath, null, null));
        if (task.TimestampMode == "source")
            declarations.Add(("timestampPath", task.TimestampPath, null, null));
        if (task.ProcessSpecification is { } specification)
        {
            declarations.Add(("processSpecification.idPath", specification.IdPath, null, null));
            declarations.Add(("processSpecification.versionPath", specification.VersionPath, null, null));
            declarations.Add(("processSpecification.namePath", specification.NamePath, null, null));
            for (var index = 0; index < specification.ParameterMappings.Count; index++)
            {
                var mapping = specification.ParameterMappings[index];
                declarations.Add(($"processSpecification.parameterMappings[{index}].sourcePath", mapping.SourcePath, mapping.SourceDataType, mapping.WordOrder));
                declarations.Add(($"processSpecification.parameterMappings[{index}].qualityPath", mapping.QualityPath, null, null));
            }
        }

        var distinctSelectors = new HashSet<string>(StringComparer.Ordinal);
        var pointsByAddress = new Dictionary<string, (AcquisitionSelectors.OmronFinsPoint Point, string Path)>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (string.IsNullOrWhiteSpace(declaration.Selector)) continue;
            if (!AcquisitionSelectors.TryParseOmronFins(
                    declaration.Selector, declaration.DataType, declaration.WordOrder, out var point, out _))
            {
                distinctSelectors.Add(declaration.Selector.Trim());
                continue;
            }

            var canonicalSelector = AcquisitionSelectors.FormatOmronFins(point);
            distinctSelectors.Add(canonicalSelector);
            var addressKey = point.BitIndex is { } bit
                ? $"{point.Area}:{point.Address}:bit:{bit}"
                : $"{point.Area}:{point.Address}:word";
            if (pointsByAddress.TryGetValue(addressKey, out var existing) &&
                (existing.Point.DataType != point.DataType || existing.Point.WordOrder != point.WordOrder))
            {
                found.Add(new AcquisitionValidationError(
                    declaration.Path,
                    $"{protocolName} 地址 {canonicalSelector} 与 {existing.Path} 的数据类型或多字字序冲突。"));
            }
            else
            {
                pointsByAddress.TryAdd(addressKey, (point, declaration.Path));
            }
        }

        var selectorCount = distinctSelectors.Count;
        if (selectorCount > 256)
            found.Add(new AcquisitionValidationError(
                "valueMappings",
                $"{protocolName} 单任务最多回读 256 个不同点位，当前配置 {selectorCount} 个。"));
    }

    private static void ValidateMtConnectPointLimit(
        IngestionTask task,
        string protocol,
        List<AcquisitionValidationError> found)
    {
        if (protocol != AcquisitionProtocols.MtConnect) return;

        const int maximumDataItems = 128;
        const int maximumFilterPathLength = 8 * 1024;
        var dataItemIds = new HashSet<string>(StringComparer.Ordinal);
        void AddSelector(string? selector)
        {
            if (AcquisitionSelectors.TryParseMtConnectDataItem(selector, out var point, out _))
                dataItemIds.Add(point.Id);
        }

        foreach (var mapping in task.ValueMappings)
            AddSelector(mapping.SourcePath);
        foreach (var mapping in task.ContextMappings)
            AddSelector(mapping.SourcePath);
        if (task.ProcessSpecification is { } specification)
        {
            AddSelector(specification.IdPath);
            AddSelector(specification.VersionPath);
            AddSelector(specification.NamePath);
            foreach (var mapping in specification.ParameterMappings)
                AddSelector(mapping.SourcePath);
        }

        if (dataItemIds.Count > maximumDataItems)
            found.Add(new AcquisitionValidationError(
                "valueMappings",
                $"MTConnect 单任务最多配置 {maximumDataItems} 个不同 DataItem ID，当前配置 {dataItemIds.Count} 个。"));

        // Keep the generated /current path? filter under the same bound enforced by the Edge transport.
        var pathExpression = $"//DataItem[{string.Join(" or ", dataItemIds.Select(static id => $"@id='{id}'"))}]";
        var filterPathLength = "current?path=".Length + Uri.EscapeDataString(pathExpression).Length;
        if (filterPathLength > maximumFilterPathLength)
            found.Add(new AcquisitionValidationError(
                "valueMappings",
                $"MTConnect DataItem 筛选路径预算不能超过 {maximumFilterPathLength} 个字符。"));
    }

    private static void ValidateBacnetPointLimit(
        IngestionTask task,
        string protocol,
        List<AcquisitionValidationError> found)
    {
        if (protocol is not (AcquisitionProtocols.BacnetIp or AcquisitionProtocols.BacnetMstp)) return;
        const int maximumObjects = 128;
        var selectors = new HashSet<string>(
            task.ValueMappings.Select(static mapping => mapping.SourcePath)
                .Concat(task.ContextMappings.Select(static mapping => mapping.SourcePath)),
            StringComparer.Ordinal);
        if (task.ProcessSpecification is { } specification)
        {
            selectors.UnionWith(new string?[]
            {
                specification.IdPath,
                specification.VersionPath,
                specification.NamePath
            }.OfType<string>());
            selectors.UnionWith(specification.ParameterMappings.Select(static mapping => mapping.SourcePath));
        }
        var count = selectors
            .Where(static selector => !string.IsNullOrWhiteSpace(selector))
            .Select(static selector => selector.Trim())
            .Count();
        if (count > maximumObjects)
            found.Add(new AcquisitionValidationError(
                "valueMappings",
                $"BACnet 单任务最多读取 {maximumObjects} 个不同对象，当前配置 {count} 个。"));
    }

    private static void ValidateSnmpPointLimit(
        IngestionTask task,
        string protocol,
        List<AcquisitionValidationError> found)
    {
        if (protocol != AcquisitionProtocols.Snmp) return;
        var selectors = task.ValueMappings.Select(static mapping => mapping.SourcePath)
            .Concat(task.ValueMappings.Select(static mapping => mapping.QualityPath))
            .Concat(task.ContextMappings.Select(static mapping => mapping.SourcePath))
            .ToList();
        if (task.TimestampMode == "source") selectors.Add(task.TimestampPath);
        if (!string.IsNullOrWhiteSpace(task.SequencePath)) selectors.Add(task.SequencePath);
        if (task.ProcessSpecification is { } specification)
        {
            selectors.Add(specification.IdPath);
            selectors.Add(specification.VersionPath);
            selectors.Add(specification.NamePath);
            selectors.AddRange(specification.ParameterMappings.Select(static mapping => mapping.SourcePath));
            selectors.AddRange(specification.ParameterMappings.Select(static mapping => mapping.QualityPath));
        }

        var count = selectors.Where(static selector => !string.IsNullOrWhiteSpace(selector))
            .Distinct(StringComparer.Ordinal)
            .Count();
        if (count > 128)
            found.Add(new AcquisitionValidationError(
                "valueMappings",
                $"SNMP 单任务最多读取 128 个不同 OID，当前配置 {count} 个。"));

        foreach (var group in task.ValueMappings.Select((mapping, index) => (mapping, index))
                     .GroupBy(static item => item.mapping.SourcePath, StringComparer.Ordinal))
        {
            var types = group.Select(static item => item.mapping.SourceDataType).Distinct(StringComparer.Ordinal).ToArray();
            if (types.Length <= 1) continue;
            foreach (var item in group)
                found.Add(new AcquisitionValidationError(
                    $"valueMappings[{item.index}].sourceDataType",
                    $"SNMP OID {group.Key} 在同一任务中不能声明多个源数据类型：{string.Join("、", types)}。"));
        }
    }

    private static bool TryParseUnicastIpv4(string host, out IPAddress address)
    {
        address = IPAddress.Any;
        var normalizedHost = host?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedHost) || normalizedHost.Any(char.IsControl) ||
            !IPAddress.TryParse(normalizedHost, out var parsed) ||
            !string.Equals(normalizedHost, parsed.ToString(), StringComparison.Ordinal) ||
            parsed.AddressFamily != AddressFamily.InterNetwork)
            return false;

        var bytes = parsed.GetAddressBytes();
        if (IPAddress.IsLoopback(parsed) || parsed.Equals(IPAddress.Any) || parsed.Equals(IPAddress.Broadcast) ||
            bytes[0] is 0 or >= 224 || bytes[0] == 169 && bytes[1] == 254)
            return false;

        address = parsed;
        return true;
    }

    private static void ValidateSelectorSyntax(
        AcquisitionProtocolCapability capability,
        string selector,
        string path,
        List<AcquisitionValidationError> found,
        string? melsecMc3EFamily = null)
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
            case AcquisitionAddressingKinds.MelsecMc3EDevice:
                if (!AcquisitionSelectors.TryParseMelsecMc3E(
                        selector, dataType: null, family: melsecMc3EFamily, out _, out var melsecMc3EError))
                    found.Add(new AcquisitionValidationError(path, melsecMc3EError));
                break;
            case AcquisitionAddressingKinds.SiemensS7Address:
                if (!AcquisitionSelectors.TryParseSiemensS7Address(
                        selector, dataType: null, sourceByteLength: null, out _, out var siemensError))
                    found.Add(new AcquisitionValidationError(path, siemensError));
                break;
            case AcquisitionAddressingKinds.EthernetIpTag:
                if (!AcquisitionSelectors.TryParseEtherNetIpTag(selector, out _, out var etherNetIpError))
                    found.Add(new AcquisitionValidationError(path, etherNetIpError));
                break;
            case AcquisitionAddressingKinds.OmronFinsAddress:
                if (!AcquisitionSelectors.TryParseOmronFins(
                        selector, dataType: null, wordOrder: null, out _, out var omronFinsError))
                    found.Add(new AcquisitionValidationError(path, omronFinsError));
                break;
            case AcquisitionAddressingKinds.MtConnectDataItem:
                if (!AcquisitionSelectors.TryParseMtConnectDataItem(selector, out _, out var mtConnectError))
                    found.Add(new AcquisitionValidationError(path, mtConnectError));
                break;
            case AcquisitionAddressingKinds.BacnetObject:
                if (!AcquisitionSelectors.TryParseBacnetObject(selector, out _, out var bacnetError))
                    found.Add(new AcquisitionValidationError(path, bacnetError));
                break;
            case AcquisitionAddressingKinds.SnmpOid:
                if (!AcquisitionSelectors.TryParseSnmpOid(selector, out _, out var snmpError))
                    found.Add(new AcquisitionValidationError(path, snmpError));
                break;
            case AcquisitionAddressingKinds.NodeId:
                if (!IsPlausibleNodeId(selector, out var nodeError))
                    found.Add(new AcquisitionValidationError(path, nodeError));
                break;
            case AcquisitionAddressingKinds.JsonPath:
                if (!JsonFieldSelector.IsValid(selector, out var jsonError))
                    found.Add(new AcquisitionValidationError(path, jsonError));
                break;
        }
    }

    private static bool IsValidAcquisitionSerialPortName(string? portName)
    {
        var value = portName?.Trim() ?? string.Empty;
        if (value.Length == 0 || value.Any(char.IsControl))
            return false;

        if (Regex.IsMatch(
                value,
                @"^(?:COM[1-9][0-9]{0,3}|\\\\\.\\COM[1-9][0-9]{0,3})$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return true;

        var segments = value.Split('/');
        return segments.Length >= 3 && segments[0].Length == 0 && segments[1] == "dev" &&
               segments.Skip(2).All(static segment => segment.Length > 0 && segment is not ("." or ".."));
    }

    private static AcquisitionProcessSpecificationMapping? NormalizeProcessSpecification(
        AcquisitionProcessSpecificationMapping? processSpecification,
        string protocol,
        AcquisitionProtocolCapability capability,
        List<AcquisitionValidationError> found,
        string? melsecMc3EFamily)
    {
        if (processSpecification is null) return null;
        if (capability.Addressing == AcquisitionAddressingKinds.MtConnectDataItem)
        {
            found.Add(new AcquisitionValidationError(
                "processSpecification",
                "MTConnect 首期不支持设备配方版本识别或参数对象映射。"));
            return processSpecification;
        }
        if (capability.Addressing == AcquisitionAddressingKinds.EthernetIpTag)
        {
            found.Add(new AcquisitionValidationError(
                "processSpecification",
                "EtherNet/IP 当前不支持源数据类型未显式声明的配方版本字段。"));
            return processSpecification;
        }
        var idPath = capability.Addressing == AcquisitionAddressingKinds.MtConnectDataItem
            ? processSpecification.IdPath ?? string.Empty
            : processSpecification.IdPath?.Trim() ?? string.Empty;
        var versionPath = capability.Addressing == AcquisitionAddressingKinds.MtConnectDataItem
            ? processSpecification.VersionPath ?? string.Empty
            : processSpecification.VersionPath?.Trim() ?? string.Empty;
        var namePath = capability.Addressing == AcquisitionAddressingKinds.MtConnectDataItem
            ? processSpecification.NamePath
            : processSpecification.NamePath?.Trim();
        if (string.IsNullOrWhiteSpace(processSpecification.IdPath))
            found.Add(new AcquisitionValidationError("processSpecification.idPath", "配方版本编号来源不能为空。"));
        else
            ValidateSelectorSyntax(capability, idPath, "processSpecification.idPath", found, melsecMc3EFamily);
        if (string.IsNullOrWhiteSpace(processSpecification.VersionPath))
            found.Add(new AcquisitionValidationError("processSpecification.versionPath", "配方版本号来源不能为空。"));
        else
            ValidateSelectorSyntax(capability, versionPath, "processSpecification.versionPath", found, melsecMc3EFamily);
        if (!string.IsNullOrWhiteSpace(processSpecification.NamePath))
            ValidateSelectorSyntax(capability, namePath ?? string.Empty, "processSpecification.namePath", found, melsecMc3EFamily);
        var processSpecificationEventType = processSpecification.EventType?.Trim() ?? string.Empty;
        if (!EventTypePattern().IsMatch(processSpecificationEventType))
            found.Add(new AcquisitionValidationError("processSpecification.eventType", "配方版本事件类型格式无效，例如 process.specification.applied。"));

        var trimmedParametersPath = processSpecification.ParametersPath?.Trim();
        var parametersPath = capability.SupportsControlParametersPath && !string.IsNullOrEmpty(trimmedParametersPath)
            ? trimmedParametersPath
            : ".";
        var parameters = NormalizeValueMappings(
            processSpecification.ParameterMappings, protocol, capability, "processSpecification.parameterMappings", found, melsecMc3EFamily);
        return processSpecification with
        {
            EventType = processSpecificationEventType,
            IdPath = NormalizeSelector(processSpecification.IdPath, capability, melsecMc3EFamily),
            VersionPath = NormalizeSelector(processSpecification.VersionPath, capability, melsecMc3EFamily),
            NamePath = CleanOptional(NormalizeSelector(processSpecification.NamePath, capability, melsecMc3EFamily)),
            ParametersPath = parametersPath,
            ParameterMappings = parameters
        };
    }

    private static Dictionary<string, string> NormalizeStaticContext(
        IngestionTask value,
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

    private static void ValidateEquipmentContext(
        IngestionTask value,
        IReadOnlyDictionary<string, string> staticContext,
        IReadOnlyList<AcquisitionContextMapping> contextMappings,
        List<AcquisitionValidationError> found)
    {
        var equipmentId = CleanOptional(value.EquipmentId);
        if (equipmentId is null)
            return;

        if (staticContext.TryGetValue("equipment_id", out var configuredEquipmentId) &&
            !string.Equals(configuredEquipmentId, equipmentId, StringComparison.Ordinal))
        {
            found.Add(new AcquisitionValidationError(
                "staticContext.equipment_id",
                "固定上下文中的设备编号必须与正式 EquipmentId 一致。"));
        }

        for (var index = 0; index < contextMappings.Count; index++)
        {
            if (!string.Equals(contextMappings[index].ContextKey, "equipment_id", StringComparison.Ordinal))
                continue;
            found.Add(new AcquisitionValidationError(
                $"contextMappings[{index}].contextKey",
                "设置正式 EquipmentId 后，不能再从采集报文动态映射 equipment_id。"));
            break;
        }
    }

    private static void ValidateAgainstModel(
        IngestionTask value,
        IReadOnlyList<AcquisitionValueMapping> valueMappings,
        AcquisitionProcessSpecificationMapping? processSpecification,
        ProcessDataModel model,
        List<AcquisitionValidationError> found)
    {
        if (!string.Equals(NormalizeCode(model.ModelId), NormalizeCode(value.DataModelId), StringComparison.Ordinal) ||
            model.Version != value.DataModelVersion)
        {
            found.Add(new AcquisitionValidationError(
                "dataModelId",
                $"任务引用 {value.DataModelId}@{value.DataModelVersion}，部署携带的数据模型却是 " +
                $"{model.ModelId}@{model.Version}。"));
            return;
        }
        if (value.Status == ConfigurationStatuses.Published &&
            model.Status != ConfigurationStatuses.Published)
        {
            found.Add(new AcquisitionValidationError("dataModelId", "发布任务只能绑定已发布的数据模型版本。"));
            return;
        }
        var modelItems = model.Acquisition?.DataItems?.OfType<ProcessDataItemDefinition>().ToArray() ?? [];
        var modelParameters = model.ControlParameters?.OfType<ControlParameterDefinition>().ToArray() ?? [];
        if (model.Acquisition?.DataItems is null || model.Acquisition.DataItems.Any(static item => item is null) ||
            model.ControlParameters is null || model.ControlParameters.Any(static item => item is null))
        {
            found.Add(new AcquisitionValidationError("dataModelId", "数据模型包含无效的 null 定义。"));
            return;
        }
        if (modelItems.Any(item => !CodePattern().IsMatch(NormalizeCode(item.Code))) ||
            modelParameters.Any(item => !CodePattern().IsMatch(NormalizeCode(item.Code))))
        {
            found.Add(new AcquisitionValidationError("dataModelId", "数据模型包含格式无效的数据项或控制参数代码。"));
            return;
        }
        if (modelItems.GroupBy(static item => item.Code, StringComparer.Ordinal).Any(static group => group.Count() > 1) ||
            modelParameters.GroupBy(static item => item.Code, StringComparer.Ordinal).Any(static group => group.Count() > 1))
        {
            found.Add(new AcquisitionValidationError("dataModelId", "数据模型包含重复的数据项或控制参数代码。"));
            return;
        }
        var dataItems = modelItems.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        var parameters = modelParameters.ToDictionary(static item => item.Code, StringComparer.Ordinal);
        for (var index = 0; index < valueMappings.Count; index++)
        {
            if (!dataItems.TryGetValue(valueMappings[index].DataItemCode, out var definition))
                found.Add(new AcquisitionValidationError(
                    $"valueMappings[{index}].dataItemCode",
                    $"数据项 {valueMappings[index].DataItemCode} 不属于所选工艺数据模型。"));
            else
            {
                var mapping = valueMappings[index];
                if (!definition.Nullable &&
                    (mapping.OutOfRangeBehavior == "omit" ||
                     mapping.MissingValueBehavior == "omit" ||
                     (!mapping.Required && mapping.MissingValueBehavior == "inherit")))
                    found.Add(new AcquisitionValidationError(
                        $"valueMappings[{index}].missingValueBehavior",
                        $"数据项 {definition.Code} 不允许空值，映射策略不能省略该值。"));
                if (mapping.MissingValueBehavior == "use-default" &&
                    !IsValidDefault(mapping.DefaultValue, definition.DataType))
                    found.Add(new AcquisitionValidationError(
                        $"valueMappings[{index}].defaultValue",
                        $"默认值不能转换为目标类型 {definition.DataType}。"));
                if (value.Status == ConfigurationStatuses.Published && !string.IsNullOrWhiteSpace(definition.Unit) &&
                    string.IsNullOrWhiteSpace(mapping.SourceUnit))
                    found.Add(new AcquisitionValidationError(
                        $"valueMappings[{index}].sourceUnit",
                        $"发布前必须声明设备值单位；平台目标单位为 {definition.Unit}。"));
                else if (value.Status == ConfigurationStatuses.Published && !string.IsNullOrWhiteSpace(definition.Unit) &&
                         !string.Equals(mapping.SourceUnit, definition.Unit, StringComparison.Ordinal) &&
                         mapping.Scale == 1 && mapping.Offset == 0)
                    found.Add(new AcquisitionValidationError(
                        $"valueMappings[{index}].sourceUnit",
                        $"设备单位 {mapping.SourceUnit} 与目标单位 {definition.Unit} 不同，必须配置明确换算。"));
            }
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
            var mapping = processSpecification.ParameterMappings[index];
            if (!parameters.TryGetValue(mapping.DataItemCode, out var definition))
                found.Add(new AcquisitionValidationError(
                    $"processSpecification.parameterMappings[{index}].dataItemCode",
                    $"控制参数 {mapping.DataItemCode} 不属于所选工艺数据模型。"));
            else if (value.Status == ConfigurationStatuses.Published && !string.IsNullOrWhiteSpace(definition.Unit))
            {
                if (string.IsNullOrWhiteSpace(mapping.SourceUnit))
                    found.Add(new AcquisitionValidationError(
                        $"processSpecification.parameterMappings[{index}].sourceUnit",
                        $"发布前必须声明控制参数设备值单位；平台目标单位为 {definition.Unit}。"));
                else if (!string.Equals(mapping.SourceUnit, definition.Unit, StringComparison.Ordinal) &&
                         mapping.Scale == 1 && mapping.Offset == 0)
                    found.Add(new AcquisitionValidationError(
                        $"processSpecification.parameterMappings[{index}].sourceUnit",
                        $"设备单位 {mapping.SourceUnit} 与目标单位 {definition.Unit} 不同，必须配置明确换算。"));
            }
        }
    }

    private static bool HasAnyAddress(AcquisitionValueMapping mapping)
        => !string.IsNullOrWhiteSpace(mapping.SourcePath) ||
           mapping.ModbusAddress.HasValue ||
           !string.IsNullOrWhiteSpace(mapping.MelsecAddress);

    private static bool IsValidDefault(string? value, string dataType)
    {
        if (value is null) return false;
        return dataType switch
        {
            "double" => double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
            "integer" => long.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out _),
            "boolean" => bool.TryParse(value, out _) || value is "0" or "1" or "on" or "off" or "ON" or "OFF",
            "string" => true,
            _ => false
        };
    }

    private static void ValidateHttpHeaders(
        IReadOnlyDictionary<string, string> headers,
        string path,
        bool allowSensitive,
        List<AcquisitionValidationError> found)
    {
        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Any(character =>
                    char.IsWhiteSpace(character) || char.IsControl(character) || character == ':'))
                found.Add(new AcquisitionValidationError($"{path}.{name}", "HTTP 请求头名称无效。"));
            if (string.IsNullOrWhiteSpace(value) || value.Contains('\r') || value.Contains('\n'))
                found.Add(new AcquisitionValidationError($"{path}.{name}", "HTTP 请求头值或密钥引用无效。"));
            if (ForbiddenHttpHeaders.Contains(name.Trim()))
                found.Add(new AcquisitionValidationError(
                    $"{path}.{name}",
                    $"HTTP 请求头 {name} 由传输层管理，不能在数据源配置中设置。"));
            if (!allowSensitive && SensitiveHttpHeaders.Contains(name.Trim()))
                found.Add(new AcquisitionValidationError(
                    $"{path}.{name}",
                    $"HTTP 请求头 {name} 可能包含凭据，必须改用密钥引用请求头。"));
        }
    }

    private static void ValidateOptionalSecretReference(
        string? reference,
        string path,
        List<AcquisitionValidationError> found)
    {
        if (!string.IsNullOrWhiteSpace(reference))
            ValidateSecretReference(reference, path, found);
    }

    private static void ValidateSecretReference(
        string? reference,
        string path,
        List<AcquisitionValidationError> found)
    {
        if (!AcquisitionSecretReferencePolicy.TryParseEnvironmentReference(reference, out _, out var error))
            found.Add(new AcquisitionValidationError(path, error));
    }

    private static readonly HashSet<string> ForbiddenHttpHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Content-Length", "Content-Type", "Host", "Keep-Alive",
        "Proxy-Connection", "TE", "Trailer", "Transfer-Encoding", "Upgrade"
    };

    private static readonly HashSet<string> SensitiveHttpHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Proxy-Authorization", "X-Api-Key", "Api-Key", "X-Auth-Token"
    };

    private static IReadOnlyDictionary<string, string> NormalizeHeaders(
        IReadOnlyDictionary<string, string> headers)
        => headers.ToDictionary(
            static pair => pair.Key.Trim(),
            static pair => pair.Value.Trim(),
            StringComparer.OrdinalIgnoreCase);

    private static void AddDuplicateHeaderErrors(
        IReadOnlyDictionary<string, string> headers,
        string path,
        List<AcquisitionValidationError> found)
    {
        foreach (var duplicate in headers.Keys
                     .GroupBy(static name => name.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
            found.Add(new AcquisitionValidationError(path, $"请求头名称 {duplicate.Key} 重复（忽略大小写）。"));
    }

    public static bool IsValidMqttTopicFilter(string topic, out string error)
        => MqttTopicFilter.IsValid(topic, out error);

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
            PayloadCompression = value.PayloadCompression.Trim().ToLowerInvariant(),
            PayloadEncoding = value.PayloadEncoding.Trim().ToLowerInvariant(),
            Topics = value.Topics
                .Where(static item => !string.IsNullOrWhiteSpace(item.Topic))
                .Select(static item => item with
                {
                    Channel = string.IsNullOrWhiteSpace(item.Channel) ? null : NormalizeCode(item.Channel),
                    Topic = item.Topic.Trim(),
                    PayloadRoot = CleanOptional(item.PayloadRoot),
                    TopicVariables = item.TopicVariables
                        .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key))
                        .ToDictionary(
                            static pair => NormalizeCode(pair.Key),
                            static pair => pair.Value,
                            StringComparer.Ordinal)
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

    private static ModbusRtuConnection? NormalizeModbusRtu(ModbusRtuConnection? value)
        => value is null ? null : value with
        {
            PortName = value.PortName.Trim(),
            Parity = value.Parity.Trim().ToLowerInvariant(),
            StopBits = value.StopBits.Trim().ToLowerInvariant(),
            AddressBase = value.AddressBase.Trim().ToLowerInvariant()
        };

    private static ModbusAsciiConnection? NormalizeModbusAscii(ModbusAsciiConnection? value)
        => value is null ? null : value with
        {
            PortName = value.PortName.Trim(),
            Parity = value.Parity.Trim().ToLowerInvariant(),
            StopBits = value.StopBits.Trim().ToLowerInvariant(),
            AddressBase = value.AddressBase.Trim().ToLowerInvariant()
        };

    private static McA1EConnection? NormalizeMelsecA1E(McA1EConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            DataCode = value.DataCode.Trim().ToLowerInvariant(),
            WordOrderLayout = value.WordOrderLayout.Trim().ToUpperInvariant()
        };

    private static Mc3EConnection? NormalizeMelsecMc3E(Mc3EConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            Family = value.Family.Trim().ToLowerInvariant()
        };

    private static SiemensS7Connection? NormalizeSiemensS7(SiemensS7Connection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            CpuType = value.CpuType.Trim()
        };

    private static EtherNetIpConnection? NormalizeEtherNetIp(EtherNetIpConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            ControllerFamily = value.ControllerFamily.Trim().ToLowerInvariant(),
            Path = value.Path.Trim()
        };

    private static OmronFinsTcpConnection? NormalizeOmronFinsTcp(OmronFinsTcpConnection? value)
        => value is null ? null : value with { Host = value.Host.Trim() };

    private static OmronFinsUdpConnection? NormalizeOmronFinsUdp(OmronFinsUdpConnection? value)
        => value is null ? null : value with { Host = value.Host.Trim() };

    private static MtConnectConnection? NormalizeMtConnect(MtConnectConnection? value)
        => value is null ? null : value with
        {
            BaseUrl = value.BaseUrl.Trim().TrimEnd('/'),
            Headers = NormalizeHeaders(value.Headers),
            HeaderSecretRefs = NormalizeHeaders(value.HeaderSecretRefs)
        };

    private static BacnetIpConnection? NormalizeBacnetIp(BacnetIpConnection? value)
        => value is null ? null : value with { Host = value.Host.Trim() };

    private static BacnetMstpConnection? NormalizeBacnetMstp(BacnetMstpConnection? value)
        => value is null ? null : value with { PortName = value.PortName.Trim() };

    private static SnmpConnection? NormalizeSnmp(SnmpConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            Version = value.Version.Trim().ToLowerInvariant(),
            BrowseRootOid = AcquisitionSelectors.TryParseSnmpOid(value.BrowseRootOid, out var root, out _)
                ? AcquisitionSelectors.FormatSnmpOid(root)
                : value.BrowseRootOid.Trim(),
            CommunitySecretRef = CleanOptional(value.CommunitySecretRef),
            UserName = CleanOptional(value.UserName),
            SecurityLevel = value.SecurityLevel.Trim(),
            AuthenticationProtocol = CleanOptional(value.AuthenticationProtocol?.Trim().ToLowerInvariant()),
            AuthenticationSecretRef = CleanOptional(value.AuthenticationSecretRef),
            PrivacyProtocol = CleanOptional(value.PrivacyProtocol?.Trim().ToLowerInvariant()),
            PrivacySecretRef = CleanOptional(value.PrivacySecretRef),
            ContextName = CleanOptional(value.ContextName)
        };

    private static AcquisitionLifecycleMapping? NormalizeLifecycle(AcquisitionLifecycleMapping? value)
        => value is null ? null : value with
        {
            Mode = value.Mode.Trim().ToLowerInvariant(),
            ActiveContextKey = CleanOptional(value.ActiveContextKey),
            ActiveCondition = value.ActiveCondition?.Trim().ToLowerInvariant() ?? string.Empty,
            ActiveValue = value.ActiveValue?.Trim() ?? string.Empty,
            StartedEventType = value.StartedEventType?.Trim() ?? string.Empty,
            CompletedEventType = value.CompletedEventType?.Trim() ?? string.Empty,
            StepChangedEventType = value.StepChangedEventType?.Trim() ?? string.Empty
        };

    private static string NormalizeTimestampEncoding(
        string? value,
        AcquisitionProtocolCapability capability)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        if (normalized != AcquisitionTimestampEncodings.Auto)
            return normalized ?? AcquisitionTimestampEncodings.Auto;
        if (capability.Addressing == AcquisitionAddressingKinds.SnmpOid)
            return AcquisitionTimestampEncodings.Auto;
        return capability.Addressing is AcquisitionAddressingKinds.ModbusRegister or AcquisitionAddressingKinds.MelsecDevice or AcquisitionAddressingKinds.MelsecMc3EDevice or AcquisitionAddressingKinds.SiemensS7Address or AcquisitionAddressingKinds.EthernetIpTag or AcquisitionAddressingKinds.OmronFinsAddress
            ? AcquisitionTimestampEncodings.UnixMilliseconds
            : AcquisitionTimestampEncodings.Iso8601;
    }

    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeSelector(
        string? value,
        AcquisitionProtocolCapability capability,
        string? melsecMc3EFamily = null)
    {
        var selector = capability.Addressing == AcquisitionAddressingKinds.MtConnectDataItem
            ? value ?? string.Empty
            : value?.Trim() ?? string.Empty;
        if (capability.Addressing == AcquisitionAddressingKinds.MtConnectDataItem &&
            AcquisitionSelectors.TryParseMtConnectDataItem(selector, out var mtConnectPoint, out _))
            return AcquisitionSelectors.FormatMtConnectDataItem(mtConnectPoint);
        if (capability.Addressing == AcquisitionAddressingKinds.BacnetObject &&
            AcquisitionSelectors.TryParseBacnetObject(selector, out var bacnetPoint, out _))
            return AcquisitionSelectors.FormatBacnetObject(bacnetPoint);
        if (capability.Addressing == AcquisitionAddressingKinds.SnmpOid &&
            AcquisitionSelectors.TryParseSnmpOid(selector, out var snmpPoint, out _))
            return AcquisitionSelectors.FormatSnmpOid(snmpPoint);
        if (capability.Addressing == AcquisitionAddressingKinds.SiemensS7Address)
            return selector.ToUpperInvariant();
        if (capability.Addressing == AcquisitionAddressingKinds.MelsecMc3EDevice &&
            AcquisitionSelectors.TryParseMelsecMc3E(
                selector, dataType: null, family: melsecMc3EFamily, out var mc3EPoint, out _))
            return AcquisitionSelectors.FormatMelsecMc3E(mc3EPoint);
        return capability.Addressing == AcquisitionAddressingKinds.OmronFinsAddress &&
               AcquisitionSelectors.TryParseOmronFins(selector, null, null, out var point, out _)
            ? AcquisitionSelectors.FormatOmronFins(point)
            : selector;
    }

    private static string? CleanOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
