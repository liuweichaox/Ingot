using System.Security.Cryptography;
using System.Text;
using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Connector.Host.Controllers;

/// <summary>
/// Protocol-neutral ingress for user-owned source adapters. Adapters translate their source
/// protocol into Ingot production events; this host never loads a PLC/device SDK.
/// </summary>
[ApiController]
[Route("api/v1/connector-events")]
public sealed class ConnectorEventsController(
    IEventSink sink,
    IEdgeIdentityProvider identity,
    IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] IReadOnlyList<ProductionEvent>? events, CancellationToken ct)
    {
        if (!IsAuthorized())
            return Unauthorized(new { error = "连接器访问凭据无效。" });
        var maxBatchSize = Math.Clamp(configuration.GetValue("ConnectorHost:MaxBatchSize", 1000), 1, 10_000);
        if (events is not { Count: > 0 })
            return BadRequest(new { error = "事件批次不能为空。" });
        if (events.Count > maxBatchSize)
            return StatusCode(StatusCodes.Status413PayloadTooLarge, new { error = $"事件批次不得超过 {maxBatchSize} 条。" });

        var edgeId = identity.GetEdgeId();
        var sourcePrefix = $"edge/{edgeId}/";
        var accepted = new List<object>(events.Count);
        foreach (var incoming in events)
        {
            if (incoming is null || string.IsNullOrWhiteSpace(incoming.Source))
                return BadRequest(new { error = "事件 Source 不能为空。" });
            var incomingSource = incoming.Source.Trim();
            if (incomingSource.StartsWith("edge/", StringComparison.OrdinalIgnoreCase) &&
                !incomingSource.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = $"事件 Source 不能声明其他边缘节点；当前前缀为 {sourcePrefix}。", eventId = incoming.EventId });
            }
            var normalizedSource = incomingSource.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase)
                ? incomingSource
                : $"{sourcePrefix}{incomingSource.TrimStart('/')}";
            var normalized = incoming with
            {
                Seq = 0,
                RecordedAt = DateTimeOffset.UtcNow,
                Source = normalizedSource
            };
            if (!ProductionEventValidator.TryValidate(normalized, requirePersistedSequence: false, out var error))
                return BadRequest(new { error, eventId = incoming.EventId });
            var persisted = await sink.EmitAsync(normalized, ct).ConfigureAwait(false);
            accepted.Add(new { persisted.EventId, persisted.Seq });
        }
        return Accepted(new { count = accepted.Count, events = accepted });
    }

    private bool IsAuthorized()
    {
        var expected = configuration["ConnectorHost:IngestToken"];
        var authorization = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        var actual = authorization["Bearer ".Length..].Trim();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }
}
