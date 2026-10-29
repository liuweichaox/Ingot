// Encodes binary MC 3E requests and reads only explicitly configured MELSEC devices.
using System.Buffers.Binary;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>Contains the independent binary 3E frame and configured-point read plan.</summary>
internal static class MelsecMc3EProtocolSupport
{
    private const int MaximumPointsPerTask = 256;
    private const int MaximumWordsPerRead = 256;
    private const int MaximumBitsPerRead = 256;
    private const int MaximumResponseLength = 4096;
    private const ushort ReadDeviceCommand = 0x0401;

    public static IReadOnlyDictionary<string, AcquisitionSelectors.MelsecMc3EPoint> BuildSelectors(
        AcquisitionDeployment deployment)
    {
        var selectors = new Dictionary<string, AcquisitionSelectors.MelsecMc3EPoint>(StringComparer.Ordinal);
        var connection = deployment.Task.MelsecMc3E
            ?? throw new InvalidOperationException("MELSEC MC 3E 连接配置不能为空。");

        void Add(string? selector, string? dataType = null)
        {
            if (string.IsNullOrWhiteSpace(selector)) return;
            if (!AcquisitionSelectors.TryParseMelsecMc3E(
                    selector, dataType, connection.Family, out var point, out var error))
                throw new InvalidOperationException(error);

            var normalized = AcquisitionSelectors.FormatMelsecMc3E(point);
            if (selectors.TryGetValue(normalized, out var existing))
            {
                if (existing != point)
                    throw new InvalidOperationException($"MELSEC MC 3E 点位 {normalized} 被配置为不兼容的数据类型。");
                return;
            }
            selectors.Add(normalized, point);
        }

        var task = deployment.Task;
        foreach (var mapping in task.ValueMappings)
        {
            Add(mapping.SourcePath, mapping.SourceDataType);
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
                Add(mapping.SourcePath, mapping.SourceDataType);
                Add(mapping.QualityPath);
            }
        }

        if (selectors.Count is < 1 or > MaximumPointsPerTask)
            throw new InvalidOperationException(
                $"MELSEC MC 3E 单任务必须包含 1-{MaximumPointsPerTask} 个已配置点位。");
        return selectors;
    }

    public static IReadOnlyList<Mc3ERead> BuildReadPlan(
        IReadOnlyDictionary<string, AcquisitionSelectors.MelsecMc3EPoint> selectors,
        int maxMergeGap)
    {
        var reads = new List<Mc3ERead>();
        var groups = selectors
            .GroupBy(static item => (item.Value.Device.DeviceCode, item.Value.UsesBitRead))
            .OrderBy(static group => group.Key.DeviceCode)
            .ThenBy(static group => group.Key.UsesBitRead);
        foreach (var group in groups)
        {
            var bitRead = group.Key.UsesBitRead;
            var limit = bitRead ? MaximumBitsPerRead : MaximumWordsPerRead;
            var ordered = group.OrderBy(static item => item.Value.Address).ToArray();
            var index = 0;
            while (index < ordered.Length)
            {
                var start = (ulong)ordered[index].Value.Address;
                var end = start + (uint)Span(ordered[index].Value, bitRead);
                var points = new List<KeyValuePair<string, AcquisitionSelectors.MelsecMc3EPoint>>
                {
                    ordered[index]
                };
                var next = index + 1;

                while (maxMergeGap > 0 && next < ordered.Length)
                {
                    var candidate = ordered[next].Value;
                    var candidateAddress = (ulong)candidate.Address;
                    var gap = candidateAddress > end ? candidateAddress - end : 0;
                    var candidateEnd = candidateAddress + (uint)Span(candidate, bitRead);
                    if (gap > (uint)maxMergeGap || candidateEnd - start > (uint)limit)
                        break;
                    points.Add(ordered[next]);
                    if (candidateEnd > end) end = candidateEnd;
                    next++;
                }

                reads.Add(new Mc3ERead(
                    group.Key.DeviceCode,
                    bitRead,
                    checked((uint)start),
                    checked((int)(end - start)),
                    points));
                index = next;
            }
        }
        return reads;
    }

    public static async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        Stream stream,
        Mc3EConnection connection,
        IReadOnlyList<Mc3ERead> plan,
        CancellationToken ct)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var read in plan)
        {
            var request = BuildReadFrame(connection, read);
            await stream.WriteAsync(request, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            var responseData = await ReadResponseDataAsync(stream, read, ct).ConfigureAwait(false);
            if (read.BitRead)
            {
                var bits = DecodeBitPayload(responseData, read.Count);
                foreach (var (path, point) in read.Points)
                    snapshot[path] = bits[checked((int)(point.Address - read.Start))];
                continue;
            }

            foreach (var (path, point) in read.Points)
            {
                var wordOffset = checked((int)(point.Address - read.Start));
                var bytes = responseData.AsSpan(wordOffset * 2, point.WordCount * 2);
                snapshot[path] = Decode(bytes, point);
            }
        }

        return snapshot;
    }

    private static byte[] BuildReadFrame(Mc3EConnection connection, Mc3ERead read)
    {
        if (read.Count is < 1 or > MaximumWordsPerRead)
            throw new InvalidOperationException(
                $"MELSEC MC 3E 单次读取数量必须在 1-{MaximumWordsPerRead} 之间，实际为 {read.Count}。");
        var isIqR = string.Equals(connection.Family, MelsecMc3EFamilies.IqR, StringComparison.Ordinal);
        if (!isIqR && connection.Family is not (MelsecMc3EFamilies.QL or MelsecMc3EFamilies.IqF))
            throw new InvalidOperationException($"MELSEC MC 3E PLC 系列无效：{connection.Family}。");
        var maximumAddress = isIqR ? uint.MaxValue : 0x00FF_FFFFu;
        if ((ulong)read.Start + (uint)(read.Count - 1) > maximumAddress)
            throw new InvalidOperationException(
                isIqR
                    ? "MELSEC iQ-R MC 3E 设备地址超出四字节地址字段范围。"
                    : "MELSEC Q/L-compatible MC 3E 设备地址超出三字节地址字段范围。");

        // iQ-R widens both device fields and uses distinct 0401 read subcommands.
        // Q/L and iQ-F use the QnA-compatible three-byte-address layout.
        var requestDataLength = isIqR ? (ushort)14 : (ushort)12;
        var request = new byte[isIqR ? 23 : 21];
        request[0] = 0x50;
        request[1] = 0x00;
        request[2] = connection.NetworkNumber;
        request[3] = connection.PcNumber;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(4, 2), connection.ModuleIoNumber);
        request[6] = connection.StationNumber;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(7, 2), requestDataLength);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(9, 2), connection.MonitoringTimer);
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(11, 2), ReadDeviceCommand);
        var subcommand = isIqR
            ? read.BitRead ? (ushort)0x0003 : (ushort)0x0002
            : read.BitRead ? (ushort)0x0001 : (ushort)0x0000;
        BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(13, 2), subcommand);
        if (isIqR)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(request.AsSpan(15, 4), read.Start);
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(19, 2), read.DeviceCode);
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(21, 2), checked((ushort)read.Count));
        }
        else
        {
            request[15] = (byte)read.Start;
            request[16] = (byte)(read.Start >> 8);
            request[17] = (byte)(read.Start >> 16);
            request[18] = checked((byte)read.DeviceCode);
            BinaryPrimitives.WriteUInt16LittleEndian(request.AsSpan(19, 2), checked((ushort)read.Count));
        }
        return request;
    }

    private static async Task<byte[]> ReadResponseDataAsync(
        Stream stream,
        Mc3ERead read,
        CancellationToken ct)
    {
        var header = new byte[9];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        if (header[0] != 0xD0 || header[1] != 0x00)
            throw new InvalidDataException("MELSEC MC 3E binary 响应子头不是 D0 00。");

        var responseLength = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(7, 2));
        if (responseLength is < 2 or > MaximumResponseLength)
            throw new InvalidDataException($"MELSEC MC 3E 响应长度无效：{responseLength}。");

        var body = new byte[responseLength];
        await stream.ReadExactlyAsync(body, ct).ConfigureAwait(false);
        var endCode = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(0, 2));
        if (endCode != 0)
            throw new IOException($"MELSEC PLC 拒绝了 MC 3E 只读请求，结束码 0x{endCode:X4}。");

        var data = body.AsSpan(2).ToArray();
        var expectedLength = read.BitRead ? (read.Count + 1) / 2 : checked(read.Count * 2);
        if (data.Length != expectedLength)
            throw new InvalidDataException(
                $"MELSEC MC 3E 返回 {data.Length} 字节，预期 {expectedLength} 字节。");
        return data;
    }

    private static bool[] DecodeBitPayload(ReadOnlySpan<byte> payload, int pointCount)
    {
        if (payload.Length != (pointCount + 1) / 2)
            throw new InvalidDataException("MELSEC MC 3E 二进制位数据长度无效。");

        var result = new bool[pointCount];
        for (var index = 0; index < pointCount; index++)
        {
            var packed = payload[index / 2];
            var value = index % 2 == 0 ? packed >> 4 : packed & 0x0F;
            result[index] = value switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException(
                    $"MELSEC MC 3E 返回了无效的二进制位值 0x{value:X1}。")
            };
        }
        return result;
    }

    private static object Decode(
        ReadOnlySpan<byte> bytes,
        AcquisitionSelectors.MelsecMc3EPoint point)
    {
        if (bytes.Length != point.WordCount * 2)
            throw new InvalidDataException("MELSEC MC 3E 点位读取长度与数据类型不匹配。");
        if (point.DataType == AcquisitionSelectors.BooleanDataType)
        {
            var word = BinaryPrimitives.ReadUInt16LittleEndian(bytes);
            return (word & (1 << (point.BitIndex ?? 0))) != 0;
        }

        // Box each scalar before the switch can promote mixed numeric arms to double.
        return point.DataType switch
        {
            "int16" => (object)BinaryPrimitives.ReadInt16LittleEndian(bytes),
            "uint16" => (object)BinaryPrimitives.ReadUInt16LittleEndian(bytes),
            "int32" => (object)BinaryPrimitives.ReadInt32LittleEndian(bytes),
            "uint32" => (object)BinaryPrimitives.ReadUInt32LittleEndian(bytes),
            "float32" => (object)BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes)),
            "int64" => (object)BinaryPrimitives.ReadInt64LittleEndian(bytes),
            "uint64" => (object)BinaryPrimitives.ReadUInt64LittleEndian(bytes),
            "float64" => (object)BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(bytes)),
            _ => throw new InvalidOperationException($"MELSEC MC 3E 暂不支持数据类型 {point.DataType}。")
        };
    }

    private static int Span(AcquisitionSelectors.MelsecMc3EPoint point, bool bitRead)
        => bitRead ? 1 : Math.Max(1, point.WordCount);

    internal sealed record Mc3ERead(
        ushort DeviceCode,
        bool BitRead,
        uint Start,
        int Count,
        IReadOnlyList<KeyValuePair<string, AcquisitionSelectors.MelsecMc3EPoint>> Points);
}
