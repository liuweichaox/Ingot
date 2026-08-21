
using System.Globalization;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed record AcquisitionMappingResult(
    ProductionEvent Sample,
    ProductionEvent? ProcessSpecificationApplied,
    string? ProcessSpecificationIdentity);

public static class HttpPollingSnapshotMapper
{
    public static AcquisitionMappingResult Map(
        JsonElement snapshot,
        HttpPollingAcquisitionOptions options,
        string normalizedSource,
        string? previousProcessSpecificationIdentity,
        IReadOnlyDictionary<string, JsonElement>? topicSnapshots = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateMappingOptions(options);

        var receivedAt = DateTimeOffset.UtcNow;
        var occurredAt = options.TimestampMode == "edge-received"
            ? receivedAt
            : ReadTimestamp(
                snapshot,
                options.TimestampPath,
                options.TimestampEncoding,
                receivedAt,
                options.MaximumFutureTimestampSkewMs);
        var context = new Dictionary<string, string>(options.StaticContext, StringComparer.Ordinal);
        foreach (var mapping in options.ContextFields)
        {
            var source = SourceRoot(snapshot, mapping.Topic, topicSnapshots);
            if (!TryResolve(source, mapping.SourcePath, out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (mapping.Required)
                    throw new InvalidDataException($"设备快照缺少必填上下文字段：{mapping.SourcePath}。");
                continue;
            }
            context[mapping.Key] = ScalarText(value, mapping.SourcePath);
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in options.Fields)
        {
            var source = SourceRoot(snapshot, field.Topic, topicSnapshots);
            values[field.Code] = ResolveMappedValue(source, field);
        }
        var stageField = options.Fields.SingleOrDefault(item => item.Category == "stage");
        if (stageField is not null &&
            values.TryGetValue(stageField.Code, out var stageValue) &&
            stageValue is not null)
        {
            context["stage_number"] =
                Convert.ToString(stageValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var sampleData = AcquisitionSampleMetadata.CreateQuality(values, receivedAt);
        sampleData["values"] = values;
        if (!string.IsNullOrWhiteSpace(options.SequencePath) &&
            TryResolve(snapshot, options.SequencePath, out var sequence) &&
            sequence.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            sampleData["sourceSequence"] = ConvertValue(sequence, "integer", options.SequencePath);
        }
        if (options.TimestampMode == "source")
            sampleData["sourceTimestamp"] = occurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        string? processSpecificationIdentity = null;
        ProductionEvent? processSpecificationEvent = null;
        if (options.ProcessSpecification is not null)
        {
            var processSpecificationId = RequiredScalar(snapshot, options.ProcessSpecification.IdPath);
            var processSpecificationVersion = RequiredScalar(snapshot, options.ProcessSpecification.VersionPath);
            processSpecificationIdentity = $"{processSpecificationId}@{processSpecificationVersion}";
            context["process_specification_id"] = processSpecificationId;
            context["process_specification_version"] = processSpecificationVersion;

            if (!string.Equals(processSpecificationIdentity, previousProcessSpecificationIdentity, StringComparison.Ordinal))
            {
                if (!TryResolve(snapshot, options.ProcessSpecification.ParametersPath, out var aggregateParameters) ||
                    aggregateParameters.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"设备快照中的控制参数必须是对象：{options.ProcessSpecification.ParametersPath}。");
                }
                var resolvedParameters = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var field in options.ProcessSpecification.ParameterFields)
                {
                    var parameters = aggregateParameters;
                    if (!string.IsNullOrWhiteSpace(field.Topic))
                    {
                        if (!TryResolve(
                                SourceRoot(snapshot, field.Topic, topicSnapshots),
                                options.ProcessSpecification.ParametersPath,
                                out parameters) ||
                            parameters.ValueKind != JsonValueKind.Object)
                        {
                            parameters = default;
                        }
                    }

                    resolvedParameters[field.Code] = ResolveMappedValue(parameters, field);
                }
                string? processSpecificationName = null;
                if (!string.IsNullOrWhiteSpace(options.ProcessSpecification.NamePath) &&
                    TryResolve(snapshot, options.ProcessSpecification.NamePath, out var nameValue) &&
                    nameValue.ValueKind == JsonValueKind.String)
                {
                    processSpecificationName = nameValue.GetString();
                }
                var processSpecificationData = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["processSpecificationId"] = processSpecificationId,
                    ["processSpecificationVersion"] = ScalarValue(snapshot, options.ProcessSpecification.VersionPath),
                    ["resolvedParameters"] = resolvedParameters
                };
                if (!string.IsNullOrWhiteSpace(processSpecificationName))
                    processSpecificationData["processSpecificationName"] = processSpecificationName;

                processSpecificationEvent = ProductionEvent.Create(
                    options.ProcessSpecification.EventType,
                    occurredAt,
                    normalizedSource,
                    new ObjectRef(options.SubjectType, options.SubjectId),
                    executionId: null,
                    context: context,
                    data: processSpecificationData,
                    appliedConfiguration: options.AppliedConfiguration);
            }
        }

        var sample = ProductionEvent.Create(
            options.SampleEventType,
            occurredAt,
            normalizedSource,
            new ObjectRef(options.SubjectType, options.SubjectId),
            executionId: null,
            context: context,
            data: sampleData,
            appliedConfiguration: options.AppliedConfiguration,
            qualityFlags: values.Any(static pair => pair.Value is null) ? ["missing_value"] : []);
        return new AcquisitionMappingResult(sample, processSpecificationEvent, processSpecificationIdentity);
    }

    public static void ValidateOptions(HttpPollingAcquisitionOptions options)
    {
        if (!Uri.TryCreate(options.DeviceBaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("Acquisition:DeviceBaseUrl 必须是 HTTP 或 HTTPS 绝对地址。");
        }
        if (string.IsNullOrWhiteSpace(options.SnapshotPath))
            throw new InvalidOperationException("Acquisition:SnapshotPath 不能为空。");
        if (options.PollIntervalMs < 1)
            throw new InvalidOperationException("Acquisition:PollIntervalMs 必须大于 0ms。");
        ValidateMappingOptions(options);
    }

    private static void ValidateMappingOptions(HttpPollingAcquisitionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SubjectType) || string.IsNullOrWhiteSpace(options.SubjectId))
            throw new InvalidOperationException("Acquisition:SubjectType 和 SubjectId 不能为空。");
        if (options.Fields.Count == 0)
            throw new InvalidOperationException("Acquisition:Fields 至少需要一个采集字段。");
        var duplicateCode = options.Fields.GroupBy(static item => item.Code, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicateCode is not null)
            throw new InvalidOperationException($"Acquisition:Fields 包含重复稳定代码：{duplicateCode}。");
        foreach (var field in options.Fields)
        {
            if (string.IsNullOrWhiteSpace(field.SourcePath) || string.IsNullOrWhiteSpace(field.Code))
                throw new InvalidOperationException("采集字段的 SourcePath 和 Code 不能为空。");
            if (field.DataType is not ("double" or "integer" or "boolean" or "string"))
                throw new InvalidOperationException($"采集字段 {field.Code} 的 DataType 不受支持：{field.DataType}。");
        }
        if (options.ProcessSpecification is not null)
        {
            if (options.ProcessSpecification.ParameterFields.Count == 0)
                throw new InvalidOperationException("配置 ProcessSpecification 时，ProcessSpecification:ParameterFields 至少需要一个参数映射。");
            foreach (var field in options.ProcessSpecification.ParameterFields)
            {
                if (string.IsNullOrWhiteSpace(field.SourcePath) || string.IsNullOrWhiteSpace(field.Code))
                    throw new InvalidOperationException("控制参数的 SourcePath 和 Code 不能为空。");
                if (field.DataType is not ("double" or "integer" or "boolean" or "string"))
                    throw new InvalidOperationException($"控制参数 {field.Code} 的 DataType 不受支持：{field.DataType}。");
            }
        }
        if (options.Lifecycle is not null)
        {
            if (options.Lifecycle.Mode != ProcessExecutionKinds.Discrete)
                throw new InvalidOperationException($"不支持的运行边界模式：{options.Lifecycle.Mode}。");
            if (string.IsNullOrWhiteSpace(options.Lifecycle.ActiveContextKey))
            {
                throw new InvalidOperationException("离散过程执行必须配置生产状态上下文键，由 Edge 自动生成执行标识。");
            }
            if (!string.IsNullOrWhiteSpace(options.Lifecycle.ActiveContextKey) &&
                !options.ContextFields.Any(item =>
                    item.Key == options.Lifecycle.ActiveContextKey))
            {
                throw new InvalidOperationException(
                    $"离散过程执行缺少激活状态上下文映射：{options.Lifecycle.ActiveContextKey}。");
            }
        }
    }

    private static DateTimeOffset ReadTimestamp(
        JsonElement root,
        string path,
        string encoding,
        DateTimeOffset receivedAt,
        int maximumFutureSkewMs)
    {
        var value = RequiredScalar(root, path);
        return AcquisitionTimestampParser.Parse(value, encoding, path, receivedAt, maximumFutureSkewMs);
    }

    private static string RequiredScalar(JsonElement root, string path)
    {
        if (!TryResolve(root, path, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            throw new InvalidDataException($"设备快照缺少必填字段：{path}。");
        }
        return ScalarText(value, path);
    }

    private static object ScalarValue(JsonElement root, string path)
    {
        if (!TryResolve(root, path, out var value))
            throw new InvalidDataException($"设备快照缺少必填字段：{path}。");
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
            _ => throw new InvalidDataException($"设备字段必须是标量：{path}。")
        };
    }

    private static string ScalarText(JsonElement value, string path) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => throw new InvalidDataException($"设备字段必须是标量：{path}。")
    };

    private static object ConvertValue(JsonElement value, string dataType, string path)
    {
        try
        {
            return dataType switch
            {
                "double" when value.ValueKind == JsonValueKind.Number => value.GetDouble(),
                "integer" when value.ValueKind == JsonValueKind.Number => value.GetInt64(),
                "boolean" when value.ValueKind is JsonValueKind.True or JsonValueKind.False => value.GetBoolean(),
                "string" when value.ValueKind == JsonValueKind.String => value.GetString()!,
                _ => throw new InvalidDataException($"设备字段 {path} 不符合配置类型 {dataType}。")
            };
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"设备字段 {path} 不符合配置类型 {dataType}。", exception);
        }
    }

    private static object? ResolveMappedValue(JsonElement root, ValueFieldMapping field)
    {
        var mapping = new Ingot.Contracts.Acquisition.AcquisitionValueMapping
        {
            DataItemCode = field.Code,
            SourcePath = field.SourcePath,
            Required = field.Required,
            Scale = field.Scale,
            Offset = field.Offset,
            QualityPath = field.QualityPath,
            AcceptedQualityValues = field.AcceptedQualityValues,
            Minimum = field.Minimum,
            Maximum = field.Maximum,
            OutOfRangeBehavior = field.OutOfRangeBehavior,
            MissingValueBehavior = field.MissingValueBehavior,
            DefaultValue = field.DefaultValue
        };
        var raw = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (TryResolve(root, field.SourcePath, out var value) &&
            value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            raw[field.SourcePath] = ConvertValue(value, field.DataType, field.SourcePath);
        if (!string.IsNullOrWhiteSpace(field.QualityPath) &&
            TryResolve(root, field.QualityPath, out var quality) &&
            quality.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            raw[field.QualityPath] = ScalarText(quality, field.QualityPath);
        return AcquisitionValuePolicy.Resolve(raw, mapping, field.DataType);
    }

    private static bool TryResolve(JsonElement root, string path, out JsonElement value)
        => JsonElementPathResolver.TryResolve(root, path, out value);

    private static JsonElement SourceRoot(
        JsonElement aggregate,
        string? topic,
        IReadOnlyDictionary<string, JsonElement>? topicSnapshots)
        => string.IsNullOrWhiteSpace(topic) ||
           topicSnapshots is null ||
           !topicSnapshots.TryGetValue(topic, out var topicRoot)
            ? string.IsNullOrWhiteSpace(topic) ? aggregate : default
            : topicRoot;

}
