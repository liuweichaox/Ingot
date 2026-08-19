using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ingot.Domain.Events;

/// <summary>
///     生产事件信封的规范化与内容指纹。哈希不包含 Seq（传输位置）和 PayloadHash（自身），
///     因此同一业务事件重放到相同 Edge outbox 时保持稳定。
/// </summary>
public static class ProductionEventIntegrity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ProductionEvent Seal(ProductionEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var normalized = evt with
        {
            QualityFlags = evt.QualityFlags
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            PayloadHash = string.Empty
        };
        return normalized with { PayloadHash = ComputePayloadHash(normalized) };
    }

    public static string ComputePayloadHash(ProductionEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", evt.SchemaVersion);
            writer.WriteString("eventId", evt.EventId);
            writer.WriteString("eventType", evt.EventType);
            writer.WriteNumber("eventTypeVersion", evt.EventTypeVersion);
            writer.WriteString("occurredAt", evt.OccurredAt.ToUniversalTime().ToString("O"));
            writer.WriteString("recordedAt", evt.RecordedAt.ToUniversalTime().ToString("O"));
            writer.WriteString("source", evt.Source);
            writer.WritePropertyName("subject");
            writer.WriteStartObject();
            writer.WriteString("id", evt.Subject.Id);
            writer.WriteString("type", evt.Subject.Type);
            writer.WriteEndObject();
            if (evt.ExecutionId is null)
                writer.WriteNull("executionId");
            else
                writer.WriteString("executionId", evt.ExecutionId);
            writer.WritePropertyName("appliedConfiguration");
            if (evt.AppliedConfiguration is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                writer.WriteStartObject();
                writer.WriteString("id", evt.AppliedConfiguration.Id);
                writer.WriteString("kind", evt.AppliedConfiguration.Kind);
                writer.WriteNumber("version", evt.AppliedConfiguration.Version);
                writer.WriteEndObject();
            }
            writer.WritePropertyName("qualityFlags");
            writer.WriteStartArray();
            foreach (var flag in evt.QualityFlags.Order(StringComparer.Ordinal))
                writer.WriteStringValue(flag);
            writer.WriteEndArray();
            writer.WritePropertyName("context");
            writer.WriteStartObject();
            foreach (var pair in evt.Context.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                writer.WriteString(pair.Key, pair.Value);
            writer.WriteEndObject();
            writer.WritePropertyName("data");
            WriteCanonical(writer, JsonSerializer.SerializeToElement(evt.Data, JsonOptions));
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    public static bool HasValidPayloadHash(ProductionEvent evt)
        => string.Equals(
            evt.PayloadHash,
            ComputePayloadHash(evt),
            StringComparison.Ordinal);

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(static value => value.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidDataException($"不支持的 JSON 值类型：{element.ValueKind}。");
        }
    }
}
