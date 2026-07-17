using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ingot.Contracts.Events;

namespace Ingot.Central.Api.Webhooks;

public static class CloudEventMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static byte[] Serialize(CentralProductionEvent item, string typePrefix)
    {
        ArgumentNullException.ThrowIfNull(item);
        var evt = item.Event;
        var prefix = string.IsNullOrWhiteSpace(typePrefix) ? "com.ingot" : typePrefix.Trim().TrimEnd('.');
        var envelope = new
        {
            specversion = "1.0",
            id = evt.EventId,
            source = evt.Source,
            type = $"{prefix}.{evt.EventType}",
            subject = evt.Subject.Id,
            time = evt.OccurredAt,
            datacontenttype = "application/json",
            ingotedgeid = item.EdgeId,
            ingotseq = evt.Seq,
            ingotingestid = item.IngestId,
            data = new
            {
                subject = evt.Subject,
                context = evt.Context,
                payload = evt.Data,
                correlationId = evt.CorrelationId,
                recordedAt = evt.RecordedAt,
                eventTypeVersion = evt.EventTypeVersion
            }
        };
        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }

    public static string ComputeSignature(ReadOnlySpan<byte> body, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(key, body);
        return $"sha256={Convert.ToHexStringLower(hash)}";
    }
}
