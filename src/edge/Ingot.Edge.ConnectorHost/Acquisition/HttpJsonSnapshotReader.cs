using System.Buffers;
using System.Text.Json;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class HttpJsonSnapshotReader
{
    internal const int MaximumPayloadBytes = AcquisitionJsonLimits.MaximumPayloadBytes;

    public static async Task<JsonElement> ReadAsync(
        HttpContent content,
        CancellationToken ct,
        int maximumPayloadBytes = MaximumPayloadBytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (maximumPayloadBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));

        if (content.Headers.ContentLength is { } declaredLength && declaredLength > maximumPayloadBytes)
            throw new InvalidDataException(
                $"HTTP JSON 响应声明长度 {declaredLength} 字节，超过 {maximumPayloadBytes} 字节上限。");

        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var initialCapacity = content.Headers.ContentLength is { } contentLength &&
                              contentLength > 0 &&
                              contentLength <= maximumPayloadBytes
            ? checked((int)contentLength)
            : 0;
        await using var buffer = new MemoryStream(initialCapacity);
        var rented = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            var total = 0;
            while (true)
            {
                var remaining = maximumPayloadBytes - total;
                var read = await source.ReadAsync(
                    rented.AsMemory(0, Math.Min(rented.Length, remaining + 1)), ct).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > maximumPayloadBytes)
                    throw new InvalidDataException(
                        $"HTTP JSON 响应超过 {maximumPayloadBytes} 字节上限。");
                await buffer.WriteAsync(rented.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }

        buffer.Position = 0;
        using var document = await JsonDocument.ParseAsync(
            buffer,
            AcquisitionJsonLimits.DocumentOptions,
            ct).ConfigureAwait(false);
        return document.RootElement.Clone();
    }
}
