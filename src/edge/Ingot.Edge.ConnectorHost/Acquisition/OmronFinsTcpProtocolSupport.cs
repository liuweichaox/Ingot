// Reads only declared Omron PLC memory points through the framed FINS/TCP service.
using System.Buffers.Binary;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal sealed class OmronFinsTcpSession(Stream stream)
{
    private static readonly byte[] Magic = "FINS"u8.ToArray();
    private byte _serviceId;

    public byte ClientNode { get; private set; }

    public byte ServerNode { get; private set; }

    public static async Task<OmronFinsTcpSession> CreateAsync(Stream stream, CancellationToken ct)
    {
        var session = new OmronFinsTcpSession(stream);
        await session.SendTransportFrameAsync(0, new byte[4], ct).ConfigureAwait(false);
        var response = await session.ReadTransportFrameAsync(ct).ConfigureAwait(false);
        if (response.Command != 1 || response.ErrorCode != 0 || response.Payload.Length != 8)
            throw new IOException("Omron FINS/TCP 节点握手未成功；请确认 PLC 已启用 FINS/TCP 服务。");

        var clientNode = BinaryPrimitives.ReadUInt32BigEndian(response.Payload.AsSpan(0, 4));
        var serverNode = BinaryPrimitives.ReadUInt32BigEndian(response.Payload.AsSpan(4, 4));
        if (clientNode is 0 or > 254 || serverNode is 0 or > 254 || clientNode == serverNode)
            throw new IOException("Omron FINS/TCP 返回了无效的客户端或 PLC 节点编号。");

        session.ClientNode = (byte)clientNode;
        session.ServerNode = (byte)serverNode;
        return session;
    }

    public async Task<byte[]> ReadMemoryAreaAsync(
        byte areaCode,
        ushort address,
        byte bitIndex,
        ushort quantity,
        CancellationToken ct)
    {
        if (quantity is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Omron FINS 单次读取数量必须在 1-128 之间。");

        var finsFrame = new byte[18];
        finsFrame[0] = 0x80;
        finsFrame[1] = 0;
        finsFrame[2] = 2;
        finsFrame[3] = 0;
        finsFrame[4] = ServerNode;
        finsFrame[5] = 0;
        finsFrame[6] = 0;
        finsFrame[7] = ClientNode;
        finsFrame[8] = 0;
        finsFrame[9] = unchecked(++_serviceId);
        finsFrame[10] = 0x01;
        finsFrame[11] = 0x01;
        finsFrame[12] = areaCode;
        BinaryPrimitives.WriteUInt16BigEndian(finsFrame.AsSpan(13, 2), address);
        finsFrame[15] = bitIndex;
        BinaryPrimitives.WriteUInt16BigEndian(finsFrame.AsSpan(16, 2), quantity);

        await SendTransportFrameAsync(2, finsFrame, ct).ConfigureAwait(false);
        var response = await ReadTransportFrameAsync(ct).ConfigureAwait(false);
        if (response.Command != 2)
            throw new IOException($"Omron FINS/TCP 返回了未预期的传输命令：0x{response.Command:X8}。");
        if (response.ErrorCode != 0)
            throw new IOException($"Omron FINS/TCP 传输层错误：0x{response.ErrorCode:X8}。");
        if (response.Payload.Length < 14)
            throw new InvalidDataException("Omron FINS 响应短于最小帧长度。");

        var finsResponse = response.Payload;
        if (finsResponse[9] != _serviceId || finsResponse[10] != 0x01 || finsResponse[11] != 0x01)
            throw new InvalidDataException("Omron FINS 响应与请求的 SID 或 MEMORY AREA READ 命令不匹配。");
        var endCode = BinaryPrimitives.ReadUInt16BigEndian(finsResponse.AsSpan(12, 2));
        if (endCode != 0)
            throw new IOException($"Omron PLC 拒绝了 FINS 读取请求，结束码 0x{endCode:X4}。");

        var data = finsResponse.AsSpan(14).ToArray();
        if (data.Length == 0)
            throw new InvalidDataException("Omron FINS 成功响应中没有点位数据。");
        return data;
    }

    private async Task SendTransportFrameAsync(uint command, ReadOnlyMemory<byte> payload, CancellationToken ct)
    {
        var frame = new byte[16 + payload.Length];
        Magic.CopyTo(frame, 0);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4, 4), checked((uint)(8 + payload.Length)));
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8, 4), command);
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(12, 4), 0);
        payload.CopyTo(frame.AsMemory(16));
        await stream.WriteAsync(frame, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task<TransportResponse> ReadTransportFrameAsync(CancellationToken ct)
    {
        var prefix = new byte[8];
        await stream.ReadExactlyAsync(prefix, ct).ConfigureAwait(false);
        if (!prefix.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("Omron FINS/TCP 响应头不是 FINS。");

        var length = BinaryPrimitives.ReadUInt32BigEndian(prefix.AsSpan(4, 4));
        if (length is < 8 or > 4096)
            throw new InvalidDataException($"Omron FINS/TCP 响应长度无效：{length}。");

        var body = new byte[(int)length];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        return new TransportResponse(
            BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(0, 4)),
            BinaryPrimitives.ReadUInt32BigEndian(body.AsSpan(4, 4)),
            body.AsSpan(8).ToArray());
    }

    private sealed record TransportResponse(uint Command, uint ErrorCode, byte[] Payload);
}

internal static class OmronFinsTcpProtocolSupport
{
    private const int MaximumPointsPerTask = 256;
    private const int MaximumWordsPerRead = 128;

    public static IReadOnlyDictionary<string, AcquisitionSelectors.OmronFinsPoint> BuildSelectors(
        AcquisitionDeployment deployment)
    {
        var selectors = new Dictionary<string, AcquisitionSelectors.OmronFinsPoint>(StringComparer.Ordinal);

        void Add(string? selector, string? dataType = null, string? wordOrder = null)
        {
            if (string.IsNullOrWhiteSpace(selector)) return;
            if (!AcquisitionSelectors.TryParseOmronFins(selector, dataType, wordOrder, out var point, out var error))
                throw new InvalidOperationException(error);
            var normalized = AcquisitionSelectors.FormatOmronFins(point);
            if (selectors.TryGetValue(normalized, out var existing))
            {
                if (existing != point)
                    throw new InvalidOperationException($"Omron FINS 点位 {normalized} 被配置为不兼容的数据类型或字序。");
                return;
            }
            selectors.Add(normalized, point);
        }

        var task = deployment.Task;
        foreach (var mapping in task.ValueMappings)
        {
            Add(mapping.SourcePath, mapping.SourceDataType, mapping.WordOrder);
            Add(mapping.QualityPath);
        }
        foreach (var mapping in task.ContextMappings)
            Add(mapping.SourcePath);
        if (task.TimestampMode == "source")
            Add(task.TimestampPath);
        if (task.ProcessSpecification is { } specification)
        {
            Add(specification.IdPath);
            Add(specification.VersionPath);
            Add(specification.NamePath);
            foreach (var mapping in specification.ParameterMappings)
            {
                Add(mapping.SourcePath, mapping.SourceDataType, mapping.WordOrder);
                Add(mapping.QualityPath);
            }
        }

        if (selectors.Count is < 1 or > MaximumPointsPerTask)
            throw new InvalidOperationException($"Omron FINS/TCP 单任务必须包含 1-{MaximumPointsPerTask} 个已配置点位。");
        return selectors;
    }

    public static async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        OmronFinsTcpSession session,
        IReadOnlyDictionary<string, AcquisitionSelectors.OmronFinsPoint> selectors,
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
                ct).ConfigureAwait(false);
            values[selector.Key] = bytes[0] != 0;
        }

        foreach (var read in BuildWordReadPlan(selectors))
        {
            var bytes = await session.ReadMemoryAreaAsync(
                read.AreaCode,
                read.StartAddress,
                0,
                checked((ushort)(read.EndAddressExclusive - read.StartAddress)),
                ct).ConfigureAwait(false);
            var expectedBytes = checked((read.EndAddressExclusive - read.StartAddress) * 2);
            if (bytes.Length != expectedBytes)
                throw new InvalidDataException($"Omron FINS 字读取返回 {bytes.Length} 字节，预期 {expectedBytes} 字节。");

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
            "float64" => (object)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data)),
            _ => throw new InvalidOperationException($"Omron FINS 不支持标量类型 {point.DataType}。")
        };
    }

    private sealed record WordRead(
        byte AreaCode,
        ushort StartAddress,
        int EndAddressExclusive,
        IReadOnlyList<KeyValuePair<string, AcquisitionSelectors.OmronFinsPoint>> Points);
}
