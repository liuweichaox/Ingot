using System.IO.Compression;
using System.Text;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class MqttPayloadDecoderTests
{
    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("brotli")]
    public void DecompressesSupportedPayloads(string compression)
    {
        var raw = Encoding.UTF8.GetBytes("{\"value\":42}");
        var encoded = Compress(raw, compression);

        var decoded = MqttPayloadDecoder.Decode(
            encoded,
            new MqttConnection { PayloadCompression = compression });

        Assert.Equal(raw, decoded);
    }

    [Fact]
    public void ConvertsConfiguredLegacyEncodingToUtf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var source = Encoding.GetEncoding(936).GetBytes("温度");

        var decoded = MqttPayloadDecoder.Decode(
            source,
            new MqttConnection { PayloadEncoding = "gbk" });

        Assert.Equal("温度", Encoding.UTF8.GetString(decoded));
    }

    [Fact]
    public void RejectsMalformedLegacyTextInsteadOfSilentlyReplacingBytes()
    {
        Assert.Throws<DecoderFallbackException>(() => MqttPayloadDecoder.Decode(
            new byte[] { 0x81 },
            new MqttConnection { PayloadEncoding = "gbk" }));
    }

    [Fact]
    public void ResolvesTopicVariablesByLevel()
    {
        var subscription = new MqttTopicSubscription
        {
            Topic = "plant/+/telemetry",
            TopicVariables = new Dictionary<string, int> { ["equipment"] = 1 }
        };

        var values = MqttTopicVariableResolver.Resolve(subscription, "plant/press-01/telemetry");

        Assert.Equal("press-01", values["equipment"]);
    }

    private static byte[] Compress(byte[] source, string compression)
    {
        using var output = new MemoryStream();
        using (Stream compressor = compression switch
               {
                   "gzip" => new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true),
                   "deflate" => new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true),
                   "brotli" => new BrotliStream(output, CompressionLevel.Fastest, leaveOpen: true),
                   _ => throw new ArgumentOutOfRangeException(nameof(compression))
               })
            compressor.Write(source);
        return output.ToArray();
    }
}
