using System;
using System.Globalization;
using System.Text.Json;

namespace Ingot.Domain.Models;

/// <summary>
///     统一约束数据值类型，避免消息在内存与存储之间流转时退化成不可控对象。
/// </summary>
public static class DataValueNormalizer
{
    public static object? Normalize(object? value)
    {
        return value switch
        {
            null => null,
            JsonElement jsonElement => NormalizeJsonElement(jsonElement),
            string => value,
            char c => c.ToString(),
            bool => value,
            byte => value,
            sbyte => value,
            short => value,
            ushort => value,
            int => value,
            uint => value,
            long => value,
            ulong => value,
            float number when float.IsFinite(number) => value,
            double number when double.IsFinite(number) => value,
            decimal => value,
            DateTime dateTime => dateTime,
            DateTimeOffset dateTimeOffset => dateTimeOffset,
            Guid guid => guid.ToString(),
            Enum enumValue => enumValue.ToString(),
            _ => value.ToString()
        };
    }

    private static object? NormalizeJsonElement(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => NormalizeJsonNumber(value),
            JsonValueKind.Array or JsonValueKind.Object => value.GetRawText(),
            _ => value.ToString()
        };
    }

    private static object NormalizeJsonNumber(JsonElement value)
    {
        if (value.TryGetInt64(out var longValue))
            return longValue;

        if (value.TryGetUInt64(out var unsignedLongValue))
            return unsignedLongValue;

        if (value.TryGetDecimal(out var decimalValue))
            return decimalValue;

        if (value.TryGetDouble(out var doubleValue))
            return doubleValue;

        return decimal.Parse(value.GetRawText(), CultureInfo.InvariantCulture);
    }
}
