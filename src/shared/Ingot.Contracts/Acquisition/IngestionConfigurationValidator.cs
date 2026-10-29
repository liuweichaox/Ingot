
// 校验和规范化可复用采集模板、数据源实例及任务绑定边界。

using Ingot.Contracts.ProcessConfiguration;

namespace Ingot.Contracts.Acquisition;

public static partial class IngestionTaskValidator
{
    public static bool TryValidateTemplate(
        IngestionTaskTemplate? value,
        ProcessDataModel? model,
        out IngestionTaskTemplate? normalized,
        out IReadOnlyList<AcquisitionValidationError> errors)
    {
        normalized = null;
        if (value is null)
        {
            errors = [new AcquisitionValidationError(string.Empty, "任务模板不能为空。")];
            return false;
        }

        var found = new List<AcquisitionValidationError>();
        if (!CodePattern().IsMatch(NormalizeCode(value.TemplateId)))
            found.Add(new AcquisitionValidationError("templateId", "模板代码格式无效。"));
        if (value.Version < 1)
            found.Add(new AcquisitionValidationError("version", "版本号必须大于等于 1。"));
        if (string.IsNullOrWhiteSpace(value.Name))
            found.Add(new AcquisitionValidationError("name", "模板名称不能为空。"));
        if (!ConfigurationStatuses.IsValid(value.Status?.Trim().ToLowerInvariant()))
            found.Add(new AcquisitionValidationError("status", "状态必须是 draft、published 或 retired。"));
        if (!AcquisitionProtocols.IsSupported(value.Protocol?.Trim().ToLowerInvariant()))
            found.Add(new AcquisitionValidationError("protocol", "任务模板引用了未登记的采集协议。"));
        if (string.IsNullOrWhiteSpace(value.DataModelId))
            found.Add(new AcquisitionValidationError("dataModelId", "任务模板必须绑定标准数据模型。"));
        if (value.DataModelVersion < 1)
            found.Add(new AcquisitionValidationError("dataModelVersion", "数据模型版本必须大于等于 1。"));
        if (found.Count > 0)
        {
            errors = found;
            return false;
        }

        var protocol = value.Protocol!.Trim().ToLowerInvariant();
        var candidate = new IngestionTask
        {
            TaskId = value.TemplateId,
            Version = value.Version,
            Name = value.Name,
            TemplateId = value.TemplateId,
            TemplateVersion = value.Version,
            Status = value.Status!,
            EdgeId = "template-validation",
            Protocol = protocol,
            DataModelId = value.DataModelId,
            DataModelVersion = value.DataModelVersion,
            Source = $"template/{value.TemplateId}",
            SubjectId = "template-validation",
            HttpPolling = new HttpPollingConnection { BaseUrl = "http://localhost" },
            Mqtt = protocol == AcquisitionProtocols.Mqtt
                ? new MqttConnection
                {
                    Host = "localhost",
                    Topics = TemplateTopics(value),
                    SnapshotMaxAgeSeconds = TemplateTopics(value).Count > 1 ? 60 : 0
                }
                : null,
            OpcUa = protocol == AcquisitionProtocols.OpcUa
                ? new OpcUaConnection { EndpointUrl = "opc.tcp://localhost:4840" }
                : null,
            ModbusTcp = protocol == AcquisitionProtocols.ModbusTcp
                ? new ModbusTcpConnection { Host = "localhost" }
                : null,
            ModbusRtu = protocol == AcquisitionProtocols.ModbusRtu
                ? new ModbusRtuConnection { PortName = "COM1" }
                : null,
            ModbusAscii = protocol == AcquisitionProtocols.ModbusAscii
                ? new ModbusAsciiConnection { PortName = "COM1" }
                : null,
            MelsecA1E = protocol == AcquisitionProtocols.MelsecA1E
                ? new McA1EConnection { Host = "localhost" }
                : null,
            MelsecMc3E = protocol == AcquisitionProtocols.MelsecMc3E
                ? new Mc3EConnection { Host = "localhost", Port = 1, Family = MelsecMc3EFamilies.IqR }
                : null,
            SiemensS7 = protocol == AcquisitionProtocols.SiemensS7
                ? new SiemensS7Connection { Host = "127.0.0.1" }
                : null,
            EtherNetIp = protocol == AcquisitionProtocols.EtherNetIp
                ? new EtherNetIpConnection { Host = "127.0.0.1" }
                : null,
            OmronFinsTcp = protocol == AcquisitionProtocols.OmronFinsTcp
                ? new OmronFinsTcpConnection { Host = "127.0.0.1" }
                : null,
            OmronFinsUdp = protocol == AcquisitionProtocols.OmronFinsUdp
                ? new OmronFinsUdpConnection { Host = "127.0.0.1", ClientNodeAddress = 1, ServerNodeAddress = 2 }
                : null,
            MtConnect = protocol == AcquisitionProtocols.MtConnect
                ? new MtConnectConnection
                {
                    BaseUrl = "http://localhost",
                    DeviceNameOrUuid = "template-validation"
                }
                : null,
            BacnetIp = protocol == AcquisitionProtocols.BacnetIp
                ? new BacnetIpConnection { Host = "192.168.0.1" }
                : null,
            BacnetMstp = protocol == AcquisitionProtocols.BacnetMstp
                ? new BacnetMstpConnection { PortName = "COM1", LocalStationAddress = 127 }
                : null,
            Snmp = protocol == AcquisitionProtocols.Snmp
                ? new SnmpConnection
                {
                    Host = "192.168.0.1",
                    CommunitySecretRef = "env:SNMP_TEMPLATE_VALIDATION"
                }
                : null,
            Execution = value.Execution,
            TimestampMode = value.TimestampMode,
            TimestampPath = value.TimestampPath,
            TimestampEncoding = value.TimestampEncoding,
            SequencePath = value.SequencePath,
            SampleEventType = value.SampleEventType,
            StaticContext = value.StaticContext,
            ContextMappings = value.ContextMappings,
            ValueMappings = value.ValueMappings,
            ProcessSpecification = value.ProcessSpecification,
            Lifecycle = value.Lifecycle
        };
        if (!TryValidate(candidate, model, out var normalizedTask, out errors))
            return false;

        normalized = value with
        {
            TemplateId = normalizedTask!.TaskId,
            Name = normalizedTask.Name,
            Status = normalizedTask.Status,
            Protocol = normalizedTask.Protocol,
            DataModelId = normalizedTask.DataModelId,
            Execution = normalizedTask.Execution,
            TimestampMode = normalizedTask.TimestampMode,
            TimestampPath = normalizedTask.TimestampPath,
            TimestampEncoding = normalizedTask.TimestampEncoding,
            SequencePath = normalizedTask.SequencePath,
            SampleEventType = normalizedTask.SampleEventType,
            StaticContext = normalizedTask.StaticContext,
            ContextMappings = normalizedTask.ContextMappings,
            ValueMappings = normalizedTask.ValueMappings,
            ProcessSpecification = normalizedTask.ProcessSpecification,
            Lifecycle = normalizedTask.Lifecycle,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return true;
    }

    public static bool TryValidateDataSource(
        DataSourceInstance? value,
        out DataSourceInstance? normalized,
        out IReadOnlyList<AcquisitionValidationError> errors)
    {
        normalized = null;
        var found = new List<AcquisitionValidationError>();
        if (value is null)
        {
            errors = [new AcquisitionValidationError(string.Empty, "数据源不能为空。")];
            return false;
        }

        var protocol = value.Protocol?.Trim().ToLowerInvariant();
        if (!AcquisitionProtocols.IsSupported(protocol) ||
            !AcquisitionProtocolCapabilities.TryGet(protocol, out var capability))
        {
            errors = [new AcquisitionValidationError("protocol", "数据源协议不在已登记的驱动列表中。")];
            return false;
        }
        if (!CodePattern().IsMatch(NormalizeCode(value.DataSourceId)))
            found.Add(new AcquisitionValidationError("dataSourceId", "数据源代码格式无效。"));
        if (value.Version < 1)
            found.Add(new AcquisitionValidationError("version", "版本号必须大于等于 1。"));
        if (string.IsNullOrWhiteSpace(value.Name))
            found.Add(new AcquisitionValidationError("name", "数据源名称不能为空。"));
        if (!ConfigurationStatuses.IsValid(value.Status?.Trim().ToLowerInvariant()))
            found.Add(new AcquisitionValidationError("status", "状态必须是 draft、published 或 retired。"));
        if (string.IsNullOrWhiteSpace(value.EdgeId))
            found.Add(new AcquisitionValidationError("edgeId", "必须选择承载数据源的现场节点。"));
        if (string.IsNullOrWhiteSpace(value.SourceKey))
            found.Add(new AcquisitionValidationError("sourceKey", "事件来源键不能为空。"));
        if (string.IsNullOrWhiteSpace(value.SubjectId))
            found.Add(new AcquisitionValidationError("subjectId", "数据归属对象不能为空。"));

        var candidate = new IngestionTask
        {
            TaskId = string.IsNullOrWhiteSpace(value.DataSourceId) ? "invalid" : value.DataSourceId,
            Name = string.IsNullOrWhiteSpace(value.Name) ? "invalid" : value.Name,
            Status = value.Status ?? string.Empty,
            EdgeId = value.EdgeId ?? string.Empty,
            Protocol = protocol!,
            DataModelId = "source-validation",
            Source = value.SourceKey ?? string.Empty,
            SubjectType = value.SubjectType,
            SubjectId = value.SubjectId ?? string.Empty,
            DeviceProfile = IngestionTaskValidator.NormalizeDeviceProfile(value.DeviceProfile, "deviceProfile", found),
            HttpPolling = value.HttpPolling ?? new HttpPollingConnection(),
            Mqtt = value.Mqtt,
            OpcUa = value.OpcUa,
            ModbusTcp = value.ModbusTcp,
            ModbusRtu = value.ModbusRtu,
            ModbusAscii = value.ModbusAscii,
            MelsecA1E = value.MelsecA1E,
            MelsecMc3E = value.MelsecMc3E,
            SiemensS7 = value.SiemensS7,
            EtherNetIp = value.EtherNetIp,
            OmronFinsTcp = value.OmronFinsTcp,
            OmronFinsUdp = value.OmronFinsUdp,
            MtConnect = value.MtConnect,
            BacnetIp = value.BacnetIp,
            BacnetMstp = value.BacnetMstp,
            Snmp = value.Snmp,
            ValueMappings = [ValidationMapping(protocol!)]
        };
        candidate = SanitizeNullCollectionsAndMembers(candidate, found);
        ValidateStructuralLimits(candidate, found);
        ValidateConnection(candidate, protocol!, capability, found);
        if (found.Count > 0)
        {
            errors = found;
            return false;
        }

        normalized = value with
        {
            DataSourceId = NormalizeCode(value.DataSourceId),
            Name = value.Name.Trim(),
            Status = value.Status!.Trim().ToLowerInvariant(),
            EdgeId = value.EdgeId!.Trim(),
            Protocol = protocol!,
            SourceKey = value.SourceKey!.Trim().TrimStart('/'),
            SubjectType = string.IsNullOrWhiteSpace(value.SubjectType)
                ? "equipment"
                : value.SubjectType.Trim().ToLowerInvariant(),
            SubjectId = value.SubjectId!.Trim(),
            DeviceProfile = candidate.DeviceProfile,
            HttpPolling = protocol != AcquisitionProtocols.HttpPolling
                ? null
                : candidate.HttpPolling with
                {
                    BaseUrl = candidate.HttpPolling.BaseUrl.Trim().TrimEnd('/'),
                    SnapshotPath = candidate.HttpPolling.SnapshotPath.Trim(),
                    Method = candidate.HttpPolling.Method.Trim().ToLowerInvariant(),
                    ContentType = CleanOptional(candidate.HttpPolling.ContentType),
                    RequestBody = CleanOptional(candidate.HttpPolling.RequestBody),
                    Headers = NormalizeHeaders(candidate.HttpPolling.Headers),
                    HeaderSecretRefs = NormalizeHeaders(candidate.HttpPolling.HeaderSecretRefs)
                },
            Mqtt = protocol == AcquisitionProtocols.Mqtt ? NormalizeMqtt(candidate.Mqtt) : null,
            OpcUa = protocol == AcquisitionProtocols.OpcUa ? NormalizeOpcUa(candidate.OpcUa) : null,
            ModbusTcp = protocol == AcquisitionProtocols.ModbusTcp ? NormalizeModbusTcp(candidate.ModbusTcp) : null,
            ModbusRtu = protocol == AcquisitionProtocols.ModbusRtu ? NormalizeModbusRtu(candidate.ModbusRtu) : null,
            ModbusAscii = protocol == AcquisitionProtocols.ModbusAscii ? NormalizeModbusAscii(candidate.ModbusAscii) : null,
            MelsecA1E = protocol == AcquisitionProtocols.MelsecA1E ? NormalizeMelsecA1E(candidate.MelsecA1E) : null,
            MelsecMc3E = protocol == AcquisitionProtocols.MelsecMc3E ? NormalizeMelsecMc3E(candidate.MelsecMc3E) : null,
            SiemensS7 = protocol == AcquisitionProtocols.SiemensS7 ? NormalizeSiemensS7(candidate.SiemensS7) : null,
            EtherNetIp = protocol == AcquisitionProtocols.EtherNetIp ? NormalizeEtherNetIp(candidate.EtherNetIp) : null,
            OmronFinsTcp = protocol == AcquisitionProtocols.OmronFinsTcp ? NormalizeOmronFinsTcp(candidate.OmronFinsTcp) : null,
            OmronFinsUdp = protocol == AcquisitionProtocols.OmronFinsUdp ? NormalizeOmronFinsUdp(candidate.OmronFinsUdp) : null,
            MtConnect = protocol == AcquisitionProtocols.MtConnect ? NormalizeMtConnect(candidate.MtConnect) : null,
            BacnetIp = protocol == AcquisitionProtocols.BacnetIp ? NormalizeBacnetIp(candidate.BacnetIp) : null,
            BacnetMstp = protocol == AcquisitionProtocols.BacnetMstp ? NormalizeBacnetMstp(candidate.BacnetMstp) : null,
            Snmp = protocol == AcquisitionProtocols.Snmp ? NormalizeSnmp(candidate.Snmp) : null,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        errors = [];
        return true;
    }

    public static bool TryValidateBinding(
        IngestionTaskBinding? value,
        out IngestionTaskBinding? normalized,
        out IReadOnlyList<AcquisitionValidationError> errors)
    {
        normalized = null;
        var found = new List<AcquisitionValidationError>();
        if (value is null)
        {
            errors = [new AcquisitionValidationError(string.Empty, "任务绑定不能为空。")];
            return false;
        }
        if (!CodePattern().IsMatch(NormalizeCode(value.TaskId)))
            found.Add(new AcquisitionValidationError("taskId", "任务代码格式无效。"));
        if (value.Version < 1)
            found.Add(new AcquisitionValidationError("version", "版本号必须大于等于 1。"));
        if (string.IsNullOrWhiteSpace(value.Name))
            found.Add(new AcquisitionValidationError("name", "任务名称不能为空。"));
        if (!ConfigurationStatuses.IsValid(value.Status?.Trim().ToLowerInvariant()))
            found.Add(new AcquisitionValidationError("status", "状态必须是 draft、published 或 retired。"));
        if (!CodePattern().IsMatch(NormalizeCode(value.TemplateId)))
            found.Add(new AcquisitionValidationError("templateId", "模板代码格式无效。"));
        if (value.TemplateVersion < 1)
            found.Add(new AcquisitionValidationError("templateVersion", "模板版本必须大于等于 1。"));
        if (!CodePattern().IsMatch(NormalizeCode(value.DataSourceId)))
            found.Add(new AcquisitionValidationError("dataSourceId", "数据源代码格式无效。"));
        if (value.DataSourceVersion < 1)
            found.Add(new AcquisitionValidationError("dataSourceVersion", "数据源版本必须大于等于 1。"));
        var hasSubjectType = value.SubjectType is not null;
        var hasSubjectId = value.SubjectId is not null;
        if (hasSubjectType != hasSubjectId)
        {
            found.Add(new AcquisitionValidationError(
                "subjectId",
                "SubjectType 和 SubjectId 必须同时提供，或同时留空以沿用数据源默认归属对象。"));
        }
        if (hasSubjectType)
        {
            if (string.IsNullOrWhiteSpace(value.SubjectType) ||
                !CodePattern().IsMatch(NormalizeCode(value.SubjectType)))
                found.Add(new AcquisitionValidationError("subjectType", "归属对象类型格式无效。"));
            if (value.SubjectType?.Length > 128)
                found.Add(new AcquisitionValidationError("subjectType", "归属对象类型不能超过 128 个字符。"));
            if (string.IsNullOrWhiteSpace(value.SubjectId))
                found.Add(new AcquisitionValidationError("subjectId", "归属对象编号不能为空或空白。"));
            if (value.SubjectId?.Length > 256)
                found.Add(new AcquisitionValidationError("subjectId", "归属对象编号不能超过 256 个字符。"));
        }
        if (value.EquipmentId is not null && string.IsNullOrWhiteSpace(value.EquipmentId))
            found.Add(new AcquisitionValidationError("equipmentId", "物理设备编号不能是空白。"));
        if (value.EquipmentId?.Length > 256)
            found.Add(new AcquisitionValidationError("equipmentId", "物理设备编号不能超过 256 个字符。"));
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
            TemplateId = NormalizeCode(value.TemplateId),
            DataSourceId = NormalizeCode(value.DataSourceId),
            SubjectType = value.SubjectType is null ? null : NormalizeCode(value.SubjectType),
            SubjectId = value.SubjectId?.Trim(),
            EquipmentId = value.EquipmentId?.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        errors = [];
        return true;
    }

    private static IReadOnlyList<MqttTopicSubscription> TemplateTopics(IngestionTaskTemplate value)
    {
        var topics = (value.ValueMappings ?? []).OfType<AcquisitionValueMapping>().Select(static item => item.Topic)
            .Concat((value.ContextMappings ?? []).OfType<AcquisitionContextMapping>().Select(static item => item.Topic))
            .Concat((value.ProcessSpecification?.ParameterMappings ?? []).OfType<AcquisitionValueMapping>()
                .Select(static item => item.Topic))
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Select(static item => new MqttTopicSubscription { Topic = item!, Qos = 0 })
            .ToArray();
        return topics.Length > 0
            ? topics
            : [new MqttTopicSubscription { Topic = "ingot/template-validation", Qos = 0 }];
    }

    private static AcquisitionValueMapping ValidationMapping(string protocol)
        => protocol switch
        {
            AcquisitionProtocols.ModbusTcp => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "holding-register:0:int16:big-endian:high-low",
                SourceDataType = "int16",
                ModbusArea = "holding-register",
                ModbusAddress = 0
            },
            AcquisitionProtocols.ModbusRtu => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "holding-register:0:uint16",
                SourceDataType = "uint16",
                ModbusArea = "holding-register",
                ModbusAddress = 0,
                ModbusQuantity = 1,
                Scale = 1
            },
            AcquisitionProtocols.ModbusAscii => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "holding-register:0:uint16",
                SourceDataType = "uint16",
                ModbusArea = "holding-register",
                ModbusAddress = 0,
                ModbusQuantity = 1,
                Scale = 1
            },
            AcquisitionProtocols.MelsecA1E => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "D:0:int16",
                SourceDataType = "int16",
                MelsecDevice = "D",
                MelsecAddress = "0"
            },
            AcquisitionProtocols.MelsecMc3E => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "D:0:int16",
                SourceDataType = "int16"
            },
            AcquisitionProtocols.SiemensS7 => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "DB1.DBW0",
                SourceDataType = "int16"
            },
            AcquisitionProtocols.EtherNetIp => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "Program:MainProgram.Tag",
                SourceDataType = "int32"
            },
            AcquisitionProtocols.OmronFinsTcp => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "DM:0:int16",
                SourceDataType = "int16"
            },
            AcquisitionProtocols.OmronFinsUdp => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "DM:0:int16",
                SourceDataType = "int16"
            },
            AcquisitionProtocols.MtConnect => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "availability",
                SourceDataType = "auto"
            },
            AcquisitionProtocols.BacnetIp => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "analog-input:1",
                SourceDataType = "auto"
            },
            AcquisitionProtocols.BacnetMstp => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "analog-input:1",
                SourceDataType = "auto"
            },
            AcquisitionProtocols.Snmp => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "1.3.6.1.2.1.1.1.0",
                SourceDataType = "auto"
            },
            AcquisitionProtocols.OpcUa => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "i=1",
                SourceDataType = "auto"
            },
            _ => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "value",
                SourceDataType = "auto"
            }
        };
}
