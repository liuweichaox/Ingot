// 向 HTTP 响应写入 Server-Sent Events，含 keep-alive 与游标推进。
using System.Text.Json;

namespace Ingot.Platform.Api.Events;

public sealed class SseStreamWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpResponse _response;
    private long _cursor;

    public SseStreamWriter(HttpResponse response, long initialCursor)
    {
        _response = response;
        _cursor = initialCursor;
    }

    public long Cursor => _cursor;

    public void Begin()
    {
        _response.StatusCode = StatusCodes.Status200OK;
        _response.ContentType = "text/event-stream";
        _response.Headers.CacheControl = "no-cache";
        _response.Headers.Connection = "keep-alive";
    }

    public async Task WriteDataAsync<T>(long id, T payload, CancellationToken ct)
    {
        await _response.WriteAsync($"id: {id}\n", ct).ConfigureAwait(false);
        await _response.WriteAsync(
                $"data: {JsonSerializer.Serialize(payload, JsonOptions)}\n\n",
                ct)
            .ConfigureAwait(false);
        _cursor = id;
    }

    public async Task WriteKeepAliveAsync(CancellationToken ct)
        => await _response.WriteAsync(": keep-alive\n\n", ct).ConfigureAwait(false);

    public Task FlushAsync(CancellationToken ct)
        => _response.Body.FlushAsync(ct);

    public static async Task WriteProblemAsync(
        HttpResponse response,
        int statusCode,
        object problem,
        CancellationToken ct)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/problem+json";
        await response.WriteAsJsonAsync(problem, ct).ConfigureAwait(false);
    }
}
