using System.Net;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class HttpJsonSnapshotReaderTests
{
    [Fact]
    public async Task ReadAsync_ParsesBoundedJson()
    {
        using var content = new StringContent("{\"items\":[1,2],\"ok\":true}");

        var result = await HttpJsonSnapshotReader.ReadAsync(content, CancellationToken.None, 1024);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.Equal(2, result.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ReadAsync_RejectsDeclaredOversizeBeforeReadingBody()
    {
        using var content = new UnreadableContent();
        content.Headers.ContentLength = 1025;

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => HttpJsonSnapshotReader.ReadAsync(content, CancellationToken.None, 1024));

        Assert.Contains("超过", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_RejectsChunkedOversize()
    {
        using var content = new ByteArrayContent(new byte[1025]);
        content.Headers.ContentLength = null;

        await Assert.ThrowsAsync<InvalidDataException>(
            () => HttpJsonSnapshotReader.ReadAsync(content, CancellationToken.None, 1024));
    }

    [Fact]
    public async Task ReadAsync_RejectsExcessiveJsonDepth()
    {
        using var content = new StringContent(new string('[', 65) + "0" + new string(']', 65));

        await Assert.ThrowsAnyAsync<System.Text.Json.JsonException>(
            () => HttpJsonSnapshotReader.ReadAsync(content, CancellationToken.None, 2048));
    }

    private sealed class UnreadableContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new InvalidOperationException("Body should not be read.");

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
