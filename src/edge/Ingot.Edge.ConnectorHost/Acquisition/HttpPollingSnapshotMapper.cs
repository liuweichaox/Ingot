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

        var occurredAt = options.TimestampMode == "edge-received"
            ? DateTimeOffset.UtcNow
            : ReadTimestamp(snapshot, options.TimestampPath);
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
            if (!TryResolve(source, field.SourcePath, out var raw) ||
                raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                if (field.Required)
                    throw new InvalidDataException($"设备快照缺少必填采集字段：{field.SourcePath}。");
                values[field.Code] = null;
                continue;
            }
            values[field.Code] = TransformValue(
                ConvertValue(raw, field.DataType, field.SourcePath),
                field.Scale,
                field.Offset);
        }
        var stageField = options.Fields.SingleOrDefault(item => item.Category == "stage");
        if (stageField is not null &&
            values.TryGetValue(stageField.Code, out var stageValue) &&
            stageValue is not null)
        {
            context["stage_number"] =
                Convert.ToString(stageValue, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var sampleData = AcquisitionSampleMetadata.CreateQuality(values, DateTimeOffset.UtcNow);
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

                    if (!TryResolve(parameters, field.SourcePath, out var raw) ||
                        raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    {
                        if (field.Required)
                            throw new InvalidDataException($"设备工艺规范缺少必填参数：{field.SourcePath}。");
                        resolvedParameters[field.Code] = null;
                        continue;
                    }
                    resolvedParameters[field.Code] = TransformValue(
                        ConvertValue(raw, field.DataType, field.SourcePath),
                        field.Scale,
                        field.Offset);
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
                    data: processSpecificationData);
            }
        }

        var sample = ProductionEvent.Create(
            options.SampleEventType,
            occurredAt,
            normalizedSource,
            new ObjectRef(options.SubjectType, options.SubjectId),
            executionId: null,
            context: context,
            data: sampleData);
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

    private static DateTimeOffset ReadTimestamp(JsonElement root, string path)
    {
        var value = RequiredScalar(root, path);
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp)
            ? timestamp
            : throw new InvalidDataException($"设备时间戳格式无效：{path}={value}。");
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

    private static object TransformValue(object value, double scale, double offset)
        => value switch
        {
            double number => number * scale + offset,
            long number when scale == 1 && offset == 0 => number,
            long number => number * scale + offset,
            _ => value
        };

    private static bool TryResolve(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                return false;
        }
        return true;
    }

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
