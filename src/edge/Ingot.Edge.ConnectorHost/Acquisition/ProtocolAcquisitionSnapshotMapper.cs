using System.Globalization;
using Ingot.Contracts.Acquisition;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
/// 将已经由 OPC UA 或 Modbus TCP 读取的标量按平台配置映射为统一事件。
/// 字典键始终是配置中的 SourcePath，因此协议点位不进入领域模型。
/// </summary>
public static class ProtocolAcquisitionSnapshotMapper
{
    public static AcquisitionMappingResult Map(
        AcquisitionDeployment deployment,
        IReadOnlyDictionary<string, object?> raw,
        string normalizedSource,
        string? previousRecipeIdentity,
        DateTimeOffset occurredAt)
    {
        var profile = deployment.Profile;
        var dataItems = deployment.DataModel.Acquisition.DataItems
            .ToDictionary(item => item.Code, StringComparer.Ordinal);
        var context = new Dictionary<string, string>(profile.StaticContext, StringComparer.Ordinal)
        {
            ["acquisition_profile_id"] = profile.ProfileId,
            ["acquisition_profile_version"] = profile.Version.ToString(CultureInfo.InvariantCulture),
            ["data_model_id"] = profile.DataModelId,
            ["data_model_version"] = profile.DataModelVersion.ToString(CultureInfo.InvariantCulture)
        };
        foreach (var mapping in profile.ContextMappings)
        {
            if (!raw.TryGetValue(mapping.SourcePath, out var value) || value is null)
            {
                if (mapping.Required)
                    throw new InvalidDataException($"采集源缺少必填上下文：{mapping.SourcePath}。");
                continue;
            }
            context[mapping.ContextKey] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var mapping in profile.ValueMappings)
        {
            if (!raw.TryGetValue(mapping.SourcePath, out var value) || value is null)
            {
                if (mapping.Required)
                    throw new InvalidDataException($"采集源缺少必填数据项：{mapping.SourcePath}。");
                values[mapping.DataItemCode] = null;
                continue;
            }
            values[mapping.DataItemCode] = ConvertValue(
                value,
                dataItems[mapping.DataItemCode].DataType,
                mapping.Scale,
                mapping.Offset);
        }

        string? correlationId = null;
        if (profile.Lifecycle is not null &&
            context.TryGetValue(profile.Lifecycle.CorrelationIdContextKey, out var rawCorrelationId) &&
            !string.IsNullOrWhiteSpace(rawCorrelationId))
        {
            correlationId = rawCorrelationId.Trim();
        }

        string? recipeIdentity = null;
        ProductionEvent? recipeApplied = null;
        if (profile.Recipe is not null)
        {
            var recipe = profile.Recipe;
            var recipeId = RequiredScalar(raw, recipe.IdPath);
            var recipeVersion = RequiredScalar(raw, recipe.VersionPath);
            recipeIdentity = correlationId is null
                ? $"{recipeId}@{recipeVersion}"
                : $"{recipeId}@{recipeVersion}|{correlationId}";
            context["recipe_id"] = recipeId;
            context["recipe_version"] = recipeVersion;
            if (!string.Equals(recipeIdentity, previousRecipeIdentity, StringComparison.Ordinal))
            {
                var definitions = deployment.DataModel.RecipeParameters
                    .ToDictionary(item => item.Code, StringComparer.Ordinal);
                var resolved = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var mapping in recipe.ParameterMappings)
                {
                    if (!raw.TryGetValue(mapping.SourcePath, out var value) || value is null)
                    {
                        if (mapping.Required)
                            throw new InvalidDataException($"采集源缺少必填配方参数：{mapping.SourcePath}。");
                        resolved[mapping.DataItemCode] = null;
                        continue;
                    }
                    resolved[mapping.DataItemCode] = ConvertValue(
                        value,
                        definitions[mapping.DataItemCode].DataType,
                        mapping.Scale,
                        mapping.Offset);
                }
                var data = new Dictionary<string, object?>
                {
                    ["recipeId"] = recipeId,
                    ["recipeVersion"] = ScalarValue(raw[recipe.VersionPath]!),
                    ["resolvedParameters"] = resolved
                };
                if (!string.IsNullOrWhiteSpace(recipe.NamePath) &&
                    raw.TryGetValue(recipe.NamePath, out var name) &&
                    name is not null)
                {
                    data["recipeName"] = Convert.ToString(name, CultureInfo.InvariantCulture);
                }
                recipeApplied = ProductionEvent.Create(
                    recipe.EventType,
                    occurredAt,
                    normalizedSource,
                    new ObjectRef(profile.SubjectType, profile.SubjectId),
                    correlationId,
                    context,
                    data);
            }
        }

        var sample = ProductionEvent.Create(
            profile.SampleEventType,
            occurredAt,
            normalizedSource,
            new ObjectRef(profile.SubjectType, profile.SubjectId),
            correlationId,
            context,
            new Dictionary<string, object?> { ["values"] = values });
        return new AcquisitionMappingResult(sample, recipeApplied, recipeIdentity);
    }

    private static string RequiredScalar(IReadOnlyDictionary<string, object?> raw, string sourcePath)
    {
        if (!raw.TryGetValue(sourcePath, out var value) || value is null)
            throw new InvalidDataException($"采集源缺少必填字段：{sourcePath}。");
        return Convert.ToString(value, CultureInfo.InvariantCulture)
               ?? throw new InvalidDataException($"采集字段不是标量：{sourcePath}。");
    }

    private static object ScalarValue(object value)
        => value is byte or sbyte or short or ushort or int or uint or long or ulong
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : value;

    private static object ConvertValue(object raw, string targetType, double scale, double offset)
    {
        try
        {
            return targetType switch
            {
                "double" => Convert.ToDouble(raw, CultureInfo.InvariantCulture) * scale + offset,
                "integer" when scale == 1 && offset == 0 => Convert.ToInt64(raw, CultureInfo.InvariantCulture),
                "integer" => Convert.ToDouble(raw, CultureInfo.InvariantCulture) * scale + offset,
                "boolean" => Convert.ToBoolean(raw, CultureInfo.InvariantCulture),
                "string" => Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => throw new InvalidDataException($"目标数据类型不受支持：{targetType}。")
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException($"采集值无法转换为 {targetType}：{raw}。", exception);
        }
    }
}
