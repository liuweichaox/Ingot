using System.Globalization;
using System.Text.Json;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class JsonElementPathResolver
{
    public static bool TryResolve(JsonElement root, string? path, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path) || path.Trim() == ".") return true;
        var text = path.Trim();
        if (text.StartsWith("/", StringComparison.Ordinal))
            return TryPointer(text, ref value);

        foreach (var rawSegment in text.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bracket = rawSegment.IndexOf('[');
            var property = bracket < 0 ? rawSegment : rawSegment[..bracket];
            if (property.Length > 0 &&
                (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out value)))
                return false;
            while (bracket >= 0)
            {
                var close = rawSegment.IndexOf(']', bracket + 1);
                if (close < 0 || !int.TryParse(
                        rawSegment.AsSpan(bracket + 1, close - bracket - 1),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var index) ||
                    index < 0 || value.ValueKind != JsonValueKind.Array || index >= value.GetArrayLength())
                    return false;
                value = value[index];
                bracket = rawSegment.IndexOf('[', close + 1);
                if (bracket < 0 && close != rawSegment.Length - 1) return false;
            }
        }
        return true;
    }

    private static bool TryPointer(string path, ref JsonElement value)
    {
        foreach (var raw in path.Split('/').Skip(1))
        {
            var segment = raw.Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
            if (value.ValueKind == JsonValueKind.Object)
            {
                if (!value.TryGetProperty(segment, out value)) return false;
                continue;
            }
            if (value.ValueKind != JsonValueKind.Array ||
                !int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                index < 0 || index >= value.GetArrayLength())
                return false;
            value = value[index];
        }
        return true;
    }
}
