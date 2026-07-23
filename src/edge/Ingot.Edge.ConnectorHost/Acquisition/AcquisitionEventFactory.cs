using System.Globalization;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public static class AcquisitionEventFactory
{
    public static ProductionEvent CreateSample(
        AcquisitionDeployment deployment,
        string normalizedSource,
        IReadOnlyDictionary<string, object?> rawValues,
        DateTimeOffset occurredAt)
    {
        var profile = deployment.Profile;
        var definitions = deployment.DataModel.Acquisition.DataItems
            .ToDictionary(item => item.Code, StringComparer.Ordinal);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var mapping in profile.ValueMappings)
        {
            if (!rawValues.TryGetValue(mapping.DataItemCode, out var raw) || raw is null)
            {
                if (mapping.Required)
                    throw new InvalidDataException($"采集值缺少必填数据项：{mapping.DataItemCode}。");
                values[mapping.DataItemCode] = null;
                continue;
            }
            values[mapping.DataItemCode] = ConvertValue(
                raw,
                definitions[mapping.DataItemCode].DataType,
                mapping.Scale,
                mapping.Offset);
        }

        var context = new Dictionary<string, string>(profile.StaticContext, StringComparer.Ordinal)
        {
            ["acquisition_profile_id"] = profile.ProfileId,
            ["acquisition_profile_version"] = profile.Version.ToString(CultureInfo.InvariantCulture),
            ["data_model_id"] = profile.DataModelId,
            ["data_model_version"] = profile.DataModelVersion.ToString(CultureInfo.InvariantCulture)
        };
        return ProductionEvent.Create(
            profile.SampleEventType,
            occurredAt,
            normalizedSource,
            new ObjectRef(profile.SubjectType, profile.SubjectId),
            context: context,
            data: new Dictionary<string, object?> { ["values"] = values });
    }

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
