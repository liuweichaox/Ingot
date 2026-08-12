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
            MelsecA1E = protocol == AcquisitionProtocols.MelsecA1E
                ? new McA1EConnection { Host = "localhost" }
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
            HttpPolling = value.HttpPolling ?? new HttpPollingConnection(),
            Mqtt = value.Mqtt,
            OpcUa = value.OpcUa,
            ModbusTcp = value.ModbusTcp,
            MelsecA1E = value.MelsecA1E,
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
            MelsecA1E = protocol == AcquisitionProtocols.MelsecA1E ? NormalizeMelsecA1E(candidate.MelsecA1E) : null,
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
            AcquisitionProtocols.MelsecA1E => new AcquisitionValueMapping
            {
                DataItemCode = "validation",
                SourcePath = "D:0:int16",
                SourceDataType = "int16",
                MelsecDevice = "D",
                MelsecAddress = "0"
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
