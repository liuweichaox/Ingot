// Reads declared Omron PLC memory points through direct, connected FINS/UDP unicast.
using System.Buffers.Binary;
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal sealed class OmronFinsUdpSession : IDisposable
{
    private readonly UdpClient _client;
    private readonly byte _clientNodeAddress;
    private readonly byte _serverNodeAddress;
    private byte _serviceId;

    public OmronFinsUdpSession(UdpClient client, int clientNodeAddress, int serverNodeAddress)
    {
        if (clientNodeAddress is < 1 or > 254 || serverNodeAddress is < 1 or > 254 ||
            clientNodeAddress == serverNodeAddress)
        {
            client.Dispose();
            throw new ArgumentOutOfRangeException(nameof(clientNodeAddress), "FINS 客户端和 PLC 节点号必须分别在 1-254 且互不相同。");
        }

        _client = client;
        _clientNodeAddress = (byte)clientNodeAddress;
        _serverNodeAddress = (byte)serverNodeAddress;
    }

    public async Task<byte[]> ReadMemoryAreaAsync(
        byte areaCode,
        ushort address,
        byte bitIndex,
        ushort quantity,
        int timeoutMs,
        CancellationToken ct)
    {
        if (quantity is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Omron FINS 单次读取数量必须在 1-128 之间。");

        var serviceId = unchecked(++_serviceId);
        var command = new byte[18];
        command[0] = 0x80;
        command[1] = 0;
        command[2] = 2;
        command[3] = 0;
        command[4] = _serverNodeAddress;
        command[5] = 0;
        command[6] = 0;
        command[7] = _clientNodeAddress;
        command[8] = 0;
        command[9] = serviceId;
        command[10] = 0x01;
        command[11] = 0x01;
        command[12] = areaCode;
        BinaryPrimitives.WriteUInt16BigEndian(command.AsSpan(13, 2), address);
        command[15] = bitIndex;
        BinaryPrimitives.WriteUInt16BigEndian(command.AsSpan(16, 2), quantity);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Math.Max(1, timeoutMs));
        try
        {
            await _client.SendAsync(command, timeout.Token).ConfigureAwait(false);
            var response = await _client.ReceiveAsync(timeout.Token).ConfigureAwait(false);
            return ValidateReadResponse(response.Buffer, serviceId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"Omron FINS/UDP 读取节点 {_serverNodeAddress} 的地址 {address} 超过 {Math.Max(1, timeoutMs)}ms。");
        }
    }

    private byte[] ValidateReadResponse(byte[] response, byte serviceId)
    {
        if (response.Length is < 14 or > 4096)
            throw new InvalidDataException($"Omron FINS/UDP 响应长度无效：{response.Length} 字节。");

        if ((response[0] & 0xC0) != 0xC0 || response[1] != 0 || response[2] != 2 ||
            response[9] != serviceId || response[3] != 0 || response[4] != _clientNodeAddress || response[5] != 0 ||
            response[6] != 0 || response[7] != _serverNodeAddress || response[8] != 0 ||
            response[10] != 0x01 || response[11] != 0x01)
            throw new InvalidDataException("Omron FINS/UDP 响应与请求的节点、SID 或 MEMORY AREA READ 命令不匹配。");

        var endCode = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(12, 2));
        if (endCode != 0)
            throw new IOException($"Omron PLC 拒绝了 FINS/UDP 读取请求，结束码 0x{endCode:X4}。");

        var data = response.AsSpan(14).ToArray();
        if (data.Length == 0)
            throw new InvalidDataException("Omron FINS/UDP 成功响应中没有点位数据。");
        return data;
    }

    public void Dispose() => _client.Dispose();
}

internal static class OmronFinsUdpProtocolSupport
{
    private const int MaximumWordsPerRead = 128;

    public static IReadOnlyDictionary<string, AcquisitionSelectors.OmronFinsPoint> BuildSelectors(
        AcquisitionDeployment deployment)
        => OmronFinsTcpProtocolSupport.BuildSelectors(deployment);

    public static async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        OmronFinsUdpSession session,
        IReadOnlyDictionary<string, AcquisitionSelectors.OmronFinsPoint> selectors,
        int timeoutMs,
        CancellationToken ct)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var selector in selectors.Where(static item => item.Value.BitIndex.HasValue))
        {
            var point = selector.Value;
            var bytes = await session.ReadMemoryAreaAsync(
                point.BitAreaCode,
                point.Address,
                (byte)point.BitIndex!.Value,
                1,
                timeoutMs,
                ct).ConfigureAwait(false);
            if (bytes.Length != 1)
                throw new InvalidDataException($"Omron FINS/UDP 位读取返回 {bytes.Length} 字节，预期 1 字节。");
            values[selector.Key] = bytes[0] != 0;
        }

        foreach (var read in BuildWordReadPlan(selectors))
        {
            var quantity = checked((ushort)(read.EndAddressExclusive - read.StartAddress));
            var bytes = await session.ReadMemoryAreaAsync(
                read.AreaCode,
                read.StartAddress,
                0,
                quantity,
                timeoutMs,
                ct).ConfigureAwait(false);
            var expectedBytes = checked(quantity * 2);
            if (bytes.Length != expectedBytes)
                throw new InvalidDataException($"Omron FINS/UDP 字读取返回 {bytes.Length} 字节，预期 {expectedBytes} 字节。");

            foreach (var (path, point) in read.Points)
            {
                var offset = checked((point.Address - read.StartAddress) * 2);
                values[path] = Decode(bytes.AsSpan(offset, point.WordCount * 2), point);
            }
        }

        return values;
    }

    private static IReadOnlyList<WordRead> BuildWordReadPlan(
        IReadOnlyDictionary<string, AcquisitionSelectors.OmronFinsPoint> selectors)
    {
        var reads = new List<WordRead>();
        foreach (var area in selectors.Where(static item => !item.Value.BitIndex.HasValue)
                     .GroupBy(static item => item.Value.WordAreaCode)
                     .OrderBy(static group => group.Key))
        {
            var points = area.OrderBy(static item => item.Value.Address).ThenBy(static item => item.Value.WordCount).ToArray();
            var pending = new List<KeyValuePair<string, AcquisitionSelectors.OmronFinsPoint>>();
            var start = 0;
            var end = 0;
            foreach (var item in points)
            {
                var point = item.Value;
                var pointEnd = point.Address + point.WordCount;
                var exceedsRequestLimit = pending.Count > 0 && pointEnd - start > MaximumWordsPerRead;
                if (pending.Count > 0 && (point.Address > end || exceedsRequestLimit))
                {
                    reads.Add(new WordRead(area.Key, (ushort)start, end, pending.ToArray()));
                    pending.Clear();
                }
                if (pending.Count == 0)
                {
                    start = point.Address;
                    end = pointEnd;
                }
                else
                {
                    end = Math.Max(end, pointEnd);
                }
                pending.Add(item);
            }
            if (pending.Count > 0)
                reads.Add(new WordRead(area.Key, (ushort)start, end, pending.ToArray()));
        }

        return reads;
    }

    private static object Decode(ReadOnlySpan<byte> bytes, AcquisitionSelectors.OmronFinsPoint point)
    {
        Span<byte> ordered = stackalloc byte[8];
        if (bytes.Length > ordered.Length)
            throw new InvalidDataException("Omron FINS 点位超过支持的 64 位标量长度。");
        for (var word = 0; word < point.WordCount; word++)
        {
            var sourceWord = point.WordOrder == "high-low" ? word : point.WordCount - word - 1;
            bytes.Slice(sourceWord * 2, 2).CopyTo(ordered.Slice(word * 2, 2));
        }
        var data = ordered[..bytes.Length];
        // Box each scalar before the switch can promote mixed numeric arms to double.
        return point.DataType switch
        {
            "int16" => (object)BinaryPrimitives.ReadInt16BigEndian(data),
            "uint16" => (object)BinaryPrimitives.ReadUInt16BigEndian(data),
            "int32" => (object)BinaryPrimitives.ReadInt32BigEndian(data),
            "uint32" => (object)BinaryPrimitives.ReadUInt32BigEndian(data),
            "float32" => (object)BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data)),
            "int64" => (object)BinaryPrimitives.ReadInt64BigEndian(data),
            "uint64" => (object)BinaryPrimitives.ReadUInt64BigEndian(data),
            _ => throw new InvalidOperationException($"Omron FINS/UDP 不支持标量类型 {point.DataType}。")
        };
    }

    private sealed record WordRead(
        byte AreaCode,
        ushort StartAddress,
        int EndAddressExclusive,
        IReadOnlyList<KeyValuePair<string, AcquisitionSelectors.OmronFinsPoint>> Points);
}
