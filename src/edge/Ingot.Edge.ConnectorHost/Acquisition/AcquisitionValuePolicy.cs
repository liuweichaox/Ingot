
using System.Globalization;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class AcquisitionValuePolicy
{
    public static object? Resolve(
        IReadOnlyDictionary<string, object?> raw,
        AcquisitionValueMapping mapping,
        string targetType)
    {
        if (!raw.TryGetValue(mapping.SourcePath, out var value) || value is null)
            return Missing(mapping, targetType);

        if (!string.IsNullOrWhiteSpace(mapping.QualityPath))
        {
            var qualityKey = mapping.QualityPath == "$status"
                ? $"$status:{mapping.SourcePath}"
                : mapping.QualityPath;
            if (!raw.TryGetValue(qualityKey, out var quality) || quality is null)
                throw new InvalidDataException($"采集源缺少质量字段：{mapping.QualityPath}。");
            if (mapping.AcceptedQualityValues.Count > 0 &&
                !mapping.AcceptedQualityValues.Contains(
                    Convert.ToString(quality, CultureInfo.InvariantCulture) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"采集点位 {mapping.SourcePath} 的质量值 {quality} 不在允许范围内。");
            }
        }

        return ConvertAndBound(value, mapping, targetType);
    }

    public static object? Missing(AcquisitionValueMapping mapping, string targetType)
    {
        var behavior = mapping.MissingValueBehavior == "inherit"
            ? mapping.Required ? "reject" : "omit"
            : mapping.MissingValueBehavior;
        return behavior switch
        {
            "omit" => null,
            "use-default" => ConvertDefault(mapping, targetType),
            _ => throw new InvalidDataException($"采集源缺少必填数据项：{mapping.SourcePath}。")
        };
    }

    public static object? ConvertAndBound(
        object value,
        AcquisitionValueMapping mapping,
        string targetType)
    {
        object converted;
        try
        {
            converted = targetType switch
            {
                "double" => Convert.ToDouble(value, CultureInfo.InvariantCulture) * mapping.Scale + mapping.Offset,
                "integer" when mapping.Scale == 1 && mapping.Offset == 0 =>
                    Convert.ToInt64(value, CultureInfo.InvariantCulture),
                "integer" => ConvertInteger(value, mapping),
                "boolean" => ConvertBoolean(value),
                "string" => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                _ => throw new InvalidDataException($"目标数据类型不受支持：{targetType}。")
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            throw new InvalidDataException($"采集值无法转换为 {targetType}：{value}。", exception);
        }

        if (converted is not double and not float and not long and not int and not short and not decimal)
            return converted;
        var numeric = Convert.ToDouble(converted, CultureInfo.InvariantCulture);
        if (!double.IsFinite(numeric))
            throw new InvalidDataException(
                $"采集点位 {mapping.SourcePath} 的换算值不是有限数字：{numeric}。");
        var below = mapping.Minimum.HasValue && numeric < mapping.Minimum.Value;
        var above = mapping.Maximum.HasValue && numeric > mapping.Maximum.Value;
        if (!below && !above) return converted;
        return mapping.OutOfRangeBehavior switch
        {
            "omit" => null,
            "clamp" => Clamp(converted, numeric, mapping.Minimum, mapping.Maximum),
            _ => throw new InvalidDataException(
                $"采集点位 {mapping.SourcePath} 的换算值 {numeric:G15} 超出有效范围。")
        };
    }

    private static object ConvertScalar(string value, string targetType)
        => targetType switch
        {
            "double" => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
            "integer" => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            "boolean" => ConvertBoolean(value),
            "string" => value,
            _ => throw new InvalidDataException($"目标数据类型不受支持：{targetType}。")
        };

    private static object ConvertDefault(AcquisitionValueMapping mapping, string targetType)
    {
        try
        {
            return ConvertScalar(mapping.DefaultValue!, targetType);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            throw new InvalidDataException(
                $"采集点位 {mapping.SourcePath} 的默认值无法转换为 {targetType}。", exception);
        }
    }

    private static long ConvertInteger(object value, AcquisitionValueMapping mapping)
    {
        var scaled = Convert.ToDouble(value, CultureInfo.InvariantCulture) * mapping.Scale + mapping.Offset;
        if (!double.IsFinite(scaled) || scaled != Math.Truncate(scaled))
            throw new InvalidDataException(
                $"采集点位 {mapping.SourcePath} 的换算结果 {scaled:G15} 不是整数。请调整倍率、偏移或目标数据类型。");
        return checked((long)scaled);
    }

    private static bool ConvertBoolean(object value)
    {
        if (value is string text)
        {
            if (text.Equals("1", StringComparison.Ordinal) || text.Equals("on", StringComparison.OrdinalIgnoreCase))
                return true;
            if (text.Equals("0", StringComparison.Ordinal) || text.Equals("off", StringComparison.OrdinalIgnoreCase))
                return false;
        }
        if (value is sbyte or byte or short or ushort or int or uint or long or ulong)
            return Convert.ToInt64(value, CultureInfo.InvariantCulture) switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException($"布尔采集值只能是 true/false、on/off 或 0/1：{value}。")
            };
        return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    }

    private static object Clamp(object converted, double numeric, double? minimum, double? maximum)
    {
        var bounded = Math.Clamp(numeric, minimum ?? double.MinValue, maximum ?? double.MaxValue);
        return converted is long or int or short
            ? checked((long)Math.Round(bounded, MidpointRounding.AwayFromZero))
            : bounded;
    }
}
