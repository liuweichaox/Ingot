// 实现边缘采集组件 MqttPayloadDecoder，保持协议解析、凭据和领域事件边界分离。

using System.Buffers;
using System.IO.Compression;
using System.Text;
using Ingot.Contracts.Acquisition;
using MQTTnet;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class MqttPayloadDecoder
{
    private const int MaximumDecodedBytes = AcquisitionJsonLimits.MaximumPayloadBytes;

    public static byte[] Decode(ReadOnlySequence<byte> payload, MqttConnection connection)
        => Decode(payload.IsSingleSegment ? payload.First : payload.ToArray(), connection);

    public static byte[] Decode(ReadOnlyMemory<byte> payload, MqttConnection connection)
    {
        var bytes = Decompress(payload, connection.PayloadCompression);
        if (connection.PayloadEncoding == "utf-8") return bytes;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var sourceEncoding = connection.PayloadEncoding switch
        {
            "gbk" => StrictEncoding(936),
            "gb18030" => StrictEncoding(54936),
            "big5" => StrictEncoding(950),
            _ => throw new InvalidDataException($"不支持 MQTT 报文字符编码 {connection.PayloadEncoding}。")
        };
        var converted = Encoding.Convert(sourceEncoding, Encoding.UTF8, bytes);
        if (converted.Length > MaximumDecodedBytes)
            throw new InvalidDataException("MQTT 报文转为 UTF-8 后超过 16MiB 安全上限。");
        return converted;
    }

    private static Encoding StrictEncoding(int codePage)
        => Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    private static byte[] Decompress(ReadOnlyMemory<byte> payload, string compression)
    {
        if (payload.Length > MaximumDecodedBytes)
            throw new InvalidDataException("MQTT 报文超过 16MiB 安全上限。");
        if (compression == "none")
        {
            return payload.ToArray();
        }
        using var input = new MemoryStream(payload.ToArray(), writable: false);
        using Stream decompressor = compression switch
        {
            "gzip" => new GZipStream(input, CompressionMode.Decompress),
            "deflate" => new DeflateStream(input, CompressionMode.Decompress),
            "brotli" => new BrotliStream(input, CompressionMode.Decompress),
            _ => throw new InvalidDataException($"不支持 MQTT 报文压缩格式 {compression}。")
        };
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        while (true)
        {
            var read = decompressor.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            if (output.Length + read > MaximumDecodedBytes)
                throw new InvalidDataException("MQTT 报文解压后超过 16MiB 安全上限。");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}

internal static class MqttTopicVariableResolver
{
    public static IReadOnlyDictionary<string, string> Resolve(
        MqttTopicSubscription? subscription,
        string topic)
    {
        if (subscription is null || subscription.TopicVariables.Count == 0)
            return new Dictionary<string, string>();
        var levels = topic.Split('/', StringSplitOptions.None);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var variable in subscription.TopicVariables)
        {
            if (variable.Value >= levels.Length)
                throw new InvalidDataException(
                    $"MQTT 主题 {topic} 不包含变量 {variable.Key} 配置的第 {variable.Value} 层。");
            values[variable.Key] = levels[variable.Value];
        }
        return values;
    }
}

internal static class MqttSubscriptionGuard
{
    public static void EnsureAccepted(MqttClientSubscribeResult result, string requestedTopic)
    {
        ArgumentNullException.ThrowIfNull(result);
        var rejected = result.Items.FirstOrDefault(static item =>
            item.ResultCode is not (MqttClientSubscribeResultCode.GrantedQoS0 or
                MqttClientSubscribeResultCode.GrantedQoS1 or
                MqttClientSubscribeResultCode.GrantedQoS2));
        if (rejected is not null)
            throw new InvalidOperationException(
                $"MQTT 服务器拒绝订阅 {requestedTopic}：{rejected.ResultCode}"
                + (string.IsNullOrWhiteSpace(result.ReasonString) ? "。" : $"（{result.ReasonString}）。"));
        if (result.Items.Count == 0)
            throw new InvalidOperationException($"MQTT 服务器没有确认订阅 {requestedTopic}。");
    }
}
