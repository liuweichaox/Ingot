using System.Globalization;
using System.Text.Json;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed record AcquisitionMappingResult(
    ProductionEvent Sample,
    ProductionEvent? RecipeApplied,
    string? RecipeIdentity);

public static class HttpPollingSnapshotMapper
{
    public static AcquisitionMappingResult Map(
        JsonElement snapshot,
        HttpPollingAcquisitionOptions options,
        string normalizedSource,
        string? previousRecipeIdentity)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateMappingOptions(options);

        var occurredAt = options.TimestampMode == "edge-received"
            ? DateTimeOffset.UtcNow
            : ReadTimestamp(snapshot, options.TimestampPath);
        var context = new Dictionary<string, string>(options.StaticContext, StringComparer.Ordinal);
        foreach (var mapping in options.ContextFields)
        {
            if (!TryResolve(snapshot, mapping.SourcePath, out var value) ||
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
            if (!TryResolve(snapshot, field.SourcePath, out var raw) ||
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

        var sampleData = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["values"] = values
        };
        if (!string.IsNullOrWhiteSpace(options.SequencePath) &&
            TryResolve(snapshot, options.SequencePath, out var sequence) &&
            sequence.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            sampleData["sourceSequence"] = ConvertValue(sequence, "integer", options.SequencePath);
        }

        string? recipeIdentity = null;
        ProductionEvent? recipeEvent = null;
        var correlationId = ResolveCorrelationId(context, options.Lifecycle);
        if (options.Recipe is not null)
        {
            var recipeId = RequiredScalar(snapshot, options.Recipe.IdPath);
            var recipeVersion = RequiredScalar(snapshot, options.Recipe.VersionPath);
            recipeIdentity = correlationId is null
                ? $"{recipeId}@{recipeVersion}"
                : $"{recipeId}@{recipeVersion}|{correlationId}";
            context["recipe_id"] = recipeId;
            context["recipe_version"] = recipeVersion;

            if (!string.Equals(recipeIdentity, previousRecipeIdentity, StringComparison.Ordinal))
            {
                if (!TryResolve(snapshot, options.Recipe.ParametersPath, out var parameters) ||
                    parameters.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"设备快照中的配方参数必须是对象：{options.Recipe.ParametersPath}。");
                }
                var resolvedParameters = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var field in options.Recipe.ParameterFields)
                {
                    if (!TryResolve(parameters, field.SourcePath, out var raw) ||
                        raw.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                    {
                        if (field.Required)
                            throw new InvalidDataException($"设备配方缺少必填参数：{field.SourcePath}。");
                        resolvedParameters[field.Code] = null;
                        continue;
                    }
                    resolvedParameters[field.Code] = TransformValue(
                        ConvertValue(raw, field.DataType, field.SourcePath),
                        field.Scale,
                        field.Offset);
                }
                string? recipeName = null;
                if (!string.IsNullOrWhiteSpace(options.Recipe.NamePath) &&
                    TryResolve(snapshot, options.Recipe.NamePath, out var nameValue) &&
                    nameValue.ValueKind == JsonValueKind.String)
                {
                    recipeName = nameValue.GetString();
                }
                var recipeData = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["recipeId"] = recipeId,
                    ["recipeVersion"] = ScalarValue(snapshot, options.Recipe.VersionPath),
                    ["resolvedParameters"] = resolvedParameters
                };
                if (!string.IsNullOrWhiteSpace(recipeName))
                    recipeData["recipeName"] = recipeName;

                recipeEvent = ProductionEvent.Create(
                    options.Recipe.EventType,
                    occurredAt,
                    normalizedSource,
                    new ObjectRef(options.SubjectType, options.SubjectId),
                    correlationId,
                    context: context,
                    data: recipeData);
            }
        }

        var sample = ProductionEvent.Create(
            options.SampleEventType,
            occurredAt,
            normalizedSource,
            new ObjectRef(options.SubjectType, options.SubjectId),
            correlationId,
            context: context,
            data: sampleData);
        return new AcquisitionMappingResult(sample, recipeEvent, recipeIdentity);
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
        if (options.PollIntervalMs < 100)
            throw new InvalidOperationException("Acquisition:PollIntervalMs 不能小于 100ms。");
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
        if (options.Recipe is not null)
        {
            if (options.Recipe.ParameterFields.Count == 0)
                throw new InvalidOperationException("配置 Recipe 时，Recipe:ParameterFields 至少需要一个参数映射。");
            foreach (var field in options.Recipe.ParameterFields)
            {
                if (string.IsNullOrWhiteSpace(field.SourcePath) || string.IsNullOrWhiteSpace(field.Code))
                    throw new InvalidOperationException("配方参数的 SourcePath 和 Code 不能为空。");
                if (field.DataType is not ("double" or "integer" or "boolean" or "string"))
                    throw new InvalidOperationException($"配方参数 {field.Code} 的 DataType 不受支持：{field.DataType}。");
            }
        }
        if (options.Lifecycle is not null)
        {
            if (options.Lifecycle.Mode != "discrete-cycle")
                throw new InvalidOperationException($"不支持的运行边界模式：{options.Lifecycle.Mode}。");
            if (string.IsNullOrWhiteSpace(options.Lifecycle.CorrelationIdContextKey))
                throw new InvalidOperationException("周期运行必须配置关联号上下文键。");
            if (!options.ContextFields.Any(item =>
                    item.Key == options.Lifecycle.CorrelationIdContextKey))
            {
                throw new InvalidOperationException(
                    $"周期运行缺少关联号上下文映射：{options.Lifecycle.CorrelationIdContextKey}。");
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

    private static string? ResolveCorrelationId(
        IReadOnlyDictionary<string, string> context,
        LifecycleFieldMapping? lifecycle)
    {
        if (lifecycle is null ||
            !context.TryGetValue(lifecycle.CorrelationIdContextKey, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        return value.Trim();
    }
}
