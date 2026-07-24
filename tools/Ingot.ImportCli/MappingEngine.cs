using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ingot.Domain.Events;

namespace Ingot.ImportCli;

/// <summary>字段来源：取列（column）或取常量（value）。</summary>
internal sealed record FieldSource
{
    [JsonPropertyName("column")] public string? Column { get; init; }
    [JsonPropertyName("value")] public string? Value { get; init; }
    [JsonPropertyName("format")] public string? Format { get; init; }
    /// <summary>数值列时间戳缺少时区时的偏移，如 "+08:00"；缺省按 UTC。</summary>
    [JsonPropertyName("utcOffset")] public string? UtcOffset { get; init; }
    /// <summary>values 数据项类型：number | integer | boolean | string，缺省 number。</summary>
    [JsonPropertyName("type")] public string? Type { get; init; }
}

internal sealed record ImportMapping
{
    [JsonPropertyName("edgeId")] public required string EdgeId { get; init; }
    [JsonPropertyName("eventType")] public required FieldSource EventType { get; init; }
    [JsonPropertyName("occurredAt")] public required FieldSource OccurredAt { get; init; }
    [JsonPropertyName("subjectType")] public FieldSource SubjectType { get; init; } = new() { Value = "asset" };
    [JsonPropertyName("subjectId")] public required FieldSource SubjectId { get; init; }
    [JsonPropertyName("correlationId")] public FieldSource? CorrelationId { get; init; }
    [JsonPropertyName("context")] public Dictionary<string, FieldSource> Context { get; init; } = new(StringComparer.Ordinal);
    [JsonPropertyName("values")] public Dictionary<string, FieldSource> Values { get; init; } = new(StringComparer.Ordinal);
}

/// <summary>CSV → ProductionEvent 的确定性映射。无外部依赖，规则与生产事件规范一致：不推测缺失值。</summary>
internal static class MappingEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ImportMapping LoadMapping(string json)
        => JsonSerializer.Deserialize<ImportMapping>(json, JsonOptions)
           ?? throw new InvalidDataException("映射文件为空或格式不正确。");

    /// <summary>RFC4180 风格 CSV 解析：支持引号包裹、内嵌逗号/引号/换行。首行为表头。</summary>
    public static IEnumerable<Dictionary<string, string>> ReadCsv(TextReader reader)
    {
        var header = ReadRecord(reader);
        if (header is null || header.Count == 0)
            yield break;
        while (ReadRecord(reader) is { } record)
        {
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
                continue;
            var row = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < header.Count; i++)
                row[header[i].Trim()] = i < record.Count ? record[i] : string.Empty;
            yield return row;
        }
    }

    public static ProductionEvent BuildEvent(
        Dictionary<string, string> row,
        ImportMapping mapping,
        long seq,
        string sourceFileTag)
    {
        var eventType = RequireString(row, mapping.EventType, "eventType");
        var occurredAt = ParseTimestamp(RequireString(row, mapping.OccurredAt, "occurredAt"), mapping.OccurredAt);
        var subjectId = RequireString(row, mapping.SubjectId, "subjectId");
        var subjectType = RequireString(row, mapping.SubjectType, "subjectType");

        var context = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, source) in mapping.Context)
        {
            var value = Resolve(row, source);
            if (!string.IsNullOrWhiteSpace(value))
                context[key] = value.Trim();
        }

        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (mapping.Values.Count > 0)
        {
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (code, source) in mapping.Values)
            {
                var raw = Resolve(row, source);
                if (string.IsNullOrWhiteSpace(raw))
                    continue; // 缺失值不猜测、不写入
                values[code] = ConvertValue(raw.Trim(), source.Type ?? "number", code);
            }
            data["values"] = values;
        }

        var correlationId = mapping.CorrelationId is null ? null : Resolve(row, mapping.CorrelationId);
        return new ProductionEvent
        {
            EventId = Guid.CreateVersion7().ToString("D"),
            EventType = eventType.Trim(),
            EventTypeVersion = 1,
            OccurredAt = occurredAt,
            RecordedAt = DateTimeOffset.UtcNow,
            Source = $"edge/{mapping.EdgeId}/import/{sourceFileTag}",
            Subject = new ObjectRef(subjectType.Trim(), subjectId.Trim()),
            Context = context,
            Data = data,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim(),
            Seq = seq
        };
    }

    internal static DateTimeOffset ParseTimestamp(string raw, FieldSource source)
    {
        raw = raw.Trim();
        DateTimeOffset parsed;
        if (!string.IsNullOrWhiteSpace(source.Format))
        {
            // 先按带时区解析；格式不含时区时按 DateTime 解析再套用 utcOffset。
            if (DateTimeOffset.TryParseExact(raw, source.Format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsed))
                return parsed.ToUniversalTime();
            if (DateTime.TryParseExact(raw, source.Format, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var local))
                return ApplyOffset(local, source.UtcOffset);
            throw new FormatException($"时间戳 '{raw}' 不符合格式 '{source.Format}'。");
        }
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out parsed))
            return parsed.ToUniversalTime();
        throw new FormatException($"无法解析时间戳 '{raw}'（可在映射中指定 format）。");
    }

    private static DateTimeOffset ApplyOffset(DateTime local, string? utcOffset)
    {
        var offset = string.IsNullOrWhiteSpace(utcOffset)
            ? TimeSpan.Zero
            : ParseOffset(utcOffset);
        return new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), offset).ToUniversalTime();
    }

    private static TimeSpan ParseOffset(string text)
    {
        text = text.Trim();
        var negative = text.StartsWith('-');
        var body = text.TrimStart('+', '-');
        if (!TimeSpan.TryParseExact(body, @"hh\:mm", CultureInfo.InvariantCulture, out var span))
            throw new FormatException($"utcOffset '{text}' 无效，应形如 +08:00。");
        return negative ? -span : span;
    }

    internal static object ConvertValue(string raw, string type, string code) => type switch
    {
        "number" => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : throw new FormatException($"数据项 {code} 的值 '{raw}' 不是数值。"),
        "integer" => long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
            ? i
            : throw new FormatException($"数据项 {code} 的值 '{raw}' 不是整数。"),
        "boolean" => raw.ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new FormatException($"数据项 {code} 的值 '{raw}' 不是布尔值。")
        },
        "string" => raw,
        _ => throw new FormatException($"数据项 {code} 的类型 '{type}' 无效（number|integer|boolean|string）。")
    };

    private static string Resolve(Dictionary<string, string> row, FieldSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.Value))
            return source.Value;
        if (string.IsNullOrWhiteSpace(source.Column))
            return string.Empty;
        return row.TryGetValue(source.Column.Trim(), out var value) ? value : string.Empty;
    }

    private static string RequireString(Dictionary<string, string> row, FieldSource source, string field)
    {
        var value = Resolve(row, source);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException(
                $"字段 {field} 为空（column='{source.Column}', value='{source.Value}'）。");
        return value;
    }

    private static List<string>? ReadRecord(TextReader reader)
    {
        if (reader.Peek() < 0)
            return null;
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        while (true)
        {
            var read = reader.Read();
            if (read < 0)
                break;
            var ch = (char)read;
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (reader.Peek() == '"') { current.Append('"'); reader.Read(); }
                    else inQuotes = false;
                }
                else current.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': inQuotes = true; break;
                case ',': fields.Add(current.ToString()); current.Clear(); break;
                case '\r':
                    if (reader.Peek() == '\n') reader.Read();
                    fields.Add(current.ToString());
                    return fields;
                case '\n':
                    fields.Add(current.ToString());
                    return fields;
                default: current.Append(ch); break;
            }
        }
        fields.Add(current.ToString());
        return fields;
    }
}
