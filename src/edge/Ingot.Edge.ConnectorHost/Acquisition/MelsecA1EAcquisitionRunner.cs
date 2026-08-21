using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class MelsecA1EAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    ILogger<MelsecA1EAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.MelsecA1E;

    private static readonly IReadOnlyDictionary<string, byte[]> DeviceCodes =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["D"] = " D"u8.ToArray(),
            ["W"] = " W"u8.ToArray(),
            ["R"] = " R"u8.ToArray(),
            ["M"] = " M"u8.ToArray(),
            ["X"] = " X"u8.ToArray(),
            ["Y"] = " Y"u8.ToArray(),
            ["B"] = " B"u8.ToArray(),
            ["T"] = " T"u8.ToArray(),
            ["C"] = " C"u8.ToArray(),
            ["L"] = " L"u8.ToArray(),
            ["S"] = " S"u8.ToArray(),
        };

    private const int MaxWordsPerRead = 256;
    private const int MaxBitsPerRead = 256;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var connection = deployment.Task.MelsecA1E
            ?? throw new InvalidOperationException("MELSEC 1E 连接配置不能为空。");
        var selectors = BuildSelectors(deployment);
        var plan = BuildReadPlan(selectors, connection.MaxMergeGap);
        var fallbackPlan = plan.Any(static read => read.Points.Count > 1)
            ? BuildReadPlan(selectors, 0)
            : plan;
        string? currentProcessSpecification = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcpClient = new TcpClient();
                await ConnectAsync(tcpClient, connection, deployment.Task.Execution, ct).ConfigureAwait(false);
                using var stream = tcpClient.GetStream();
                logger.LogInformation(
                    "MELSEC 1E 采集任务已连接：Configuration={Configuration}, Device={Host}:{Port}, " +
                    "Points={PointCount}, Reads={ReadCount}",
                    configurationKey, connection.Host, connection.Port, selectors.Count, plan.Count);
                while (!ct.IsCancellationRequested && tcpClient.Connected)
                {
                    var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readTimeout.CancelAfter(Math.Max(1000, deployment.Task.Execution.TimeoutMs));
                    Dictionary<string, object?> raw;
                    try
                    {
                        try
                        {
                            raw = await ReadSnapshotAsync(stream, connection, plan, readTimeout.Token)
                                .ConfigureAwait(false);
                        }
                        catch (InvalidOperationException) when (!ReferenceEquals(plan, fallbackPlan))
                        {
                            raw = await ReadSnapshotAsync(stream, connection, fallbackPlan, readTimeout.Token)
                                .ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"读取 MELSEC PLC {connection.Host}:{connection.Port} 超过 {deployment.Task.Execution.TimeoutMs}ms 未完成。");
                    }
                    var observedAt = DateTimeOffset.UtcNow;
                    var readDurationMs = System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds;
                    status.RecordReadSuccess(configurationKey, observedAt, readDurationMs);
                    var occurredAt = ResolveTimestamp(deployment.Task, raw, observedAt);
                    var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                        deployment, raw, normalizedSource, currentProcessSpecification, occurredAt);
                    var deduplication = sourceDeduplicator.Evaluate(
                        mapped.Sample,
                        observedAt,
                        TimeSpan.FromMilliseconds(deployment.Task.Execution.SourceIdentityStaleAfterMs));
                    if (deduplication is AcquisitionDeduplicationResult.Duplicate or AcquisitionDeduplicationResult.Stalled)
                    {
                        currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                        status.RecordDuplicateSnapshot(
                            configurationKey,
                            deduplication == AcquisitionDeduplicationResult.Stalled,
                            $"设备源身份超过 {deployment.Task.Execution.SourceIdentityStaleAfterMs}ms 未变化。");
                        await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }
                    var events = lifecycle.Track(
                        mapped,
                        deployment.Task.Lifecycle,
                        connection.PollIntervalMs);
                    await sink.EmitBatchAsync(events, ct).ConfigureAwait(false);
                    status.RecordProcessExecutionState(configurationKey, lifecycle.IsRunActive);
                    status.RecordEmissionOutcome(
                        configurationKey,
                        events.Count,
                        deployment.Task.Lifecycle is not null && events.Count == 0);

                    currentProcessSpecification = mapped.ProcessSpecificationIdentity;
                    status.RecordValidSnapshot(
                        configurationKey,
                        observedAt,
                        currentProcessSpecification,
                        deduplication == AcquisitionDeduplicationResult.Changed ? observedAt : null);
                    await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "MELSEC 1E 采集任务 {Configuration} 读取失败，等待重连", configurationKey);
                await Task.Delay(deployment.Task.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ConnectAsync(
        TcpClient client,
        McA1EConnection connection,
        AcquisitionExecutionOptions execution,
        CancellationToken ct)
    {
        var timeout = Math.Max(1000, execution.TimeoutMs);
        client.ReceiveTimeout = timeout;
        client.SendTimeout = timeout;
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
        attempt.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(connection.Host, connection.Port, attempt.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"连接 MELSEC PLC {connection.Host}:{connection.Port} 超过 {timeout}ms 未完成。");
        }
    }

    private static DateTimeOffset ResolveTimestamp(
        IngestionTask task,
        IReadOnlyDictionary<string, object?> raw,
        DateTimeOffset observedAt)
    {
        if (task.TimestampMode != "source" || string.IsNullOrWhiteSpace(task.TimestampPath))
            return DateTimeOffset.UtcNow;
        raw.TryGetValue(task.TimestampPath, out var value);
        return AcquisitionTimestampParser.Parse(
            value,
            task.TimestampEncoding,
            task.TimestampPath,
            observedAt,
            task.Execution.MaximumFutureTimestampSkewMs);
    }

    internal static IReadOnlyDictionary<string, AcquisitionSelectors.MelsecPoint> BuildSelectors(
        AcquisitionDeployment deployment)
    {
        var result = new Dictionary<string, AcquisitionSelectors.MelsecPoint>(StringComparer.Ordinal);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || result.ContainsKey(path)) return;
            if (!AcquisitionSelectors.TryParseMelsec(path, out var point, out var error))
                throw new InvalidOperationException(error);
            result[path] = point;
        }

        foreach (var mapping in deployment.Task.ValueMappings)
        {
            Add(mapping.SourcePath);
            Add(mapping.QualityPath);
        }
        foreach (var mapping in deployment.Task.ContextMappings) Add(mapping.SourcePath);
        if (deployment.Task.TimestampMode == "source") Add(deployment.Task.TimestampPath);
        if (deployment.Task.ProcessSpecification is { } processSpecification)
        {
            Add(processSpecification.IdPath);
            Add(processSpecification.VersionPath);
            Add(processSpecification.NamePath);
            foreach (var mapping in processSpecification.ParameterMappings)
            {
                Add(mapping.SourcePath);
                Add(mapping.QualityPath);
            }
        }

        return result;
    }

    internal sealed record McRead(
        string Device,
        byte[] DeviceCode,
        bool BitRead,
        uint Start,
        int Count,
        IReadOnlyList<KeyValuePair<string, AcquisitionSelectors.MelsecPoint>> Points);

    internal static IReadOnlyList<McRead> BuildReadPlan(
        IReadOnlyDictionary<string, AcquisitionSelectors.MelsecPoint> selectors,
        int maxMergeGap)
    {
        var reads = new List<McRead>();
        var groups = selectors
            .GroupBy(item => (item.Value.Device.Code, item.Value.UsesBitRead))
            .OrderBy(group => group.Key.Code, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var bitRead = group.Key.UsesBitRead;
            var limit = bitRead ? MaxBitsPerRead : MaxWordsPerRead;
            var code = DeviceCodes.TryGetValue(group.Key.Code, out var deviceCode)
                ? deviceCode
                : throw new InvalidOperationException($"MELSEC 软元件无效：{group.Key.Code}。");
            var ordered = group.OrderBy(item => item.Value.WireAddress).ToList();
            var index = 0;
            while (index < ordered.Count)
            {
                var start = ordered[index].Value.WireAddress;
                var end = start + (uint)Span(ordered[index].Value, bitRead);
                var batch = new List<KeyValuePair<string, AcquisitionSelectors.MelsecPoint>> { ordered[index] };
                var next = index + 1;

                while (maxMergeGap > 0 && next < ordered.Count)
                {
                    var candidate = ordered[next].Value;
                    var gap = candidate.WireAddress > end ? candidate.WireAddress - end : 0;
                    var candidateEnd = candidate.WireAddress + (uint)Span(candidate, bitRead);
                    if (gap > (uint)maxMergeGap || candidateEnd - start > (uint)limit) break;
                    batch.Add(ordered[next]);
                    if (candidateEnd > end) end = candidateEnd;
                    next += 1;
                }

                reads.Add(new McRead(group.Key.Code, code, bitRead, start, (int)(end - start), batch));
                index = next;
            }
        }

        return reads;
    }

    private static int Span(AcquisitionSelectors.MelsecPoint point, bool bitRead)
        => bitRead ? 1 : Math.Max(1, point.WordCount);

    internal static async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        NetworkStream stream,
        McA1EConnection connection,
        IReadOnlyList<McRead> plan,
        CancellationToken ct)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var read in plan)
        {
            var request = read.BitRead
                ? BuildBitReadFrame(read.DeviceCode, read.Start, read.Count,
                    connection.MonitoringTimer, connection.WordOrderLayout, connection.PcNumber, connection.DataCode)
                : BuildWordReadFrame(read.DeviceCode, read.Start, read.Count,
                    connection.MonitoringTimer, connection.WordOrderLayout, connection.PcNumber, connection.DataCode);
            await stream.WriteAsync(request, ct).ConfigureAwait(false);
            if (read.BitRead)
            {
                var bits = await ReadBitResponseAsync(stream, read.Count, connection.DataCode, ct).ConfigureAwait(false);
                foreach (var (path, point) in read.Points)
                    snapshot[path] = bits[(int)(point.WireAddress - read.Start)];
                continue;
            }

            var response = await ReadResponseAsync(stream, read.Count, connection.DataCode, ct).ConfigureAwait(false);
            foreach (var (path, point) in read.Points)
            {
                var offset = (int)(point.WireAddress - read.Start);
                snapshot[path] = Decode(response, point, offset);
            }
        }

        return snapshot;
    }

    internal static byte[] BuildWordReadFrame(
        byte[] deviceCode, uint address, int wordCount, ushort timer, string layout,
        byte pcNumber = 0xFF, string dataCode = "binary")
        => BuildReadFrame(0x01, deviceCode, address, wordCount, timer, layout, pcNumber, dataCode);

    internal static byte[] BuildBitReadFrame(
        byte[] deviceCode, uint address, int pointCount, ushort timer, string layout,
        byte pcNumber = 0xFF, string dataCode = "binary")
        => BuildReadFrame(0x00, deviceCode, address, pointCount, timer, layout, pcNumber, dataCode);

    private static byte[] BuildReadFrame(
        byte command, byte[] deviceCode, uint address, int count, ushort timer, string layout,
        byte pcNumber, string dataCode)
    {
        if (!string.Equals(layout, "A", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FX3U-ENET-ADP 的 A-compatible 1E 帧只支持软元件号在前的布局 A。");
        if (count is < 1 or > 256)
            throw new InvalidOperationException($"MELSEC 1E 单次读取点数必须在 1-256 之间，实际为 {count}。");
        if (address > uint.MaxValue - (uint)(count - 1))
            throw new InvalidOperationException("MELSEC 1E 读取范围超出软元件地址边界。");
        if (dataCode == "ascii")
        {

            var ascii = $"{command:X2}{pcNumber:X2}{timer:X4}{address:X8}" +
                        $"{deviceCode[1]:X2}{deviceCode[0]:X2}{count:X4}";
            return System.Text.Encoding.ASCII.GetBytes(ascii);
        }

        if (dataCode != "binary")
            throw new InvalidOperationException($"MELSEC 1E 通信数据码无效：{dataCode}。");

        var head = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(head, address);
        var timerBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(timerBytes, timer);
        var body = head.Concat(deviceCode);
        return new byte[] { command, pcNumber }
            .Concat(timerBytes)
            .Concat(body)
            .Concat(new byte[] { (byte)(count == 256 ? 0 : count), 0x00 })
            .ToArray();
    }

    private static async Task<byte[]> ReadResponseAsync(
        NetworkStream stream, int wordCount, string dataCode, CancellationToken ct)
    {
        if (dataCode == "ascii")
        {
            var asciiHeader = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
            var headerText = System.Text.Encoding.ASCII.GetString(asciiHeader);
            EnsureAsciiSuccess(headerText);
            var asciiData = await ReadExactAsync(stream, wordCount * 4, ct).ConfigureAwait(false);
            var text = headerText + System.Text.Encoding.ASCII.GetString(asciiData);
            var binary = new byte[2 + wordCount * 2];
            binary[0] = 0x81;
            for (var index = 0; index < wordCount; index++)
            {
                if (!ushort.TryParse(text.AsSpan(4 + index * 4, 4), NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture, out var word))
                    throw new InvalidDataException("MELSEC PLC 返回了无效的 ASCII 字数据。");
                BinaryPrimitives.WriteUInt16LittleEndian(binary.AsSpan(2 + index * 2, 2), word);
            }

            return binary;
        }

        var header = await ReadExactAsync(stream, 2, ct).ConfigureAwait(false);
        EnsureBinarySuccess(header);
        var data = await ReadExactAsync(stream, wordCount * 2, ct).ConfigureAwait(false);
        return header.Concat(data).ToArray();
    }

    internal static async Task<bool[]> ReadBitResponseAsync(
        NetworkStream stream, int pointCount, string dataCode, CancellationToken ct)
    {
        var result = new bool[pointCount];
        if (dataCode == "ascii")
        {
            var header = await ReadExactAsync(stream, 4, ct).ConfigureAwait(false);
            var headerText = System.Text.Encoding.ASCII.GetString(header);
            EnsureAsciiSuccess(headerText);
            var data = await ReadExactAsync(stream, pointCount, ct).ConfigureAwait(false);
            return DecodeBitPayload(data, pointCount, dataCode);
        }

        var responseHeader = await ReadExactAsync(stream, 2, ct).ConfigureAwait(false);
        EnsureBinarySuccess(responseHeader);
        var responseData = await ReadExactAsync(stream, (pointCount + 1) / 2, ct).ConfigureAwait(false);
        return DecodeBitPayload(responseData, pointCount, dataCode);
    }

    internal static bool[] DecodeBitPayload(ReadOnlySpan<byte> payload, int pointCount, string dataCode)
    {
        var result = new bool[pointCount];
        if (dataCode == "ascii")
        {
            if (payload.Length < pointCount)
                throw new InvalidDataException("MELSEC PLC 返回的 ASCII 位数据长度不足。");
            for (var index = 0; index < pointCount; index++)
            {
                result[index] = payload[index] switch
                {
                    (byte)'0' => false,
                    (byte)'1' => true,
                    _ => throw new InvalidDataException(
                        $"MELSEC PLC 返回了无效的 ASCII 位值 0x{payload[index]:X2}。")
                };
            }
            return result;
        }

        if (dataCode != "binary")
            throw new InvalidDataException($"MELSEC 1E 通信数据码无效：{dataCode}。");
        if (payload.Length < (pointCount + 1) / 2)
            throw new InvalidDataException("MELSEC PLC 返回的二进制位数据长度不足。");
        for (var index = 0; index < pointCount; index++)
        {
            var packed = payload[index / 2];
            var value = index % 2 == 0 ? packed >> 4 : packed & 0x0F;
            result[index] = value switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException(
                    $"MELSEC PLC 返回了无效的二进制位值 0x{value:X1}。")
            };
        }
        return result;
    }

    private static void EnsureAsciiSuccess(string text)
    {
        if (!byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var completeCode))
            throw new InvalidDataException("MELSEC PLC 返回了无效的 ASCII 完成码。");
        if (completeCode != 0)
            throw new InvalidOperationException($"MELSEC PLC 返回错误：结束码=0x{completeCode:X2}");
    }

    internal static void EnsureBinarySuccess(ReadOnlySpan<byte> header)
    {
        if (header.Length < 2)
            throw new InvalidDataException("MELSEC PLC 返回的二进制响应头长度不足。");
        if (header[1] != 0x00)
            throw new InvalidOperationException($"MELSEC PLC 返回错误：结束码=0x{header[1]:X2}");
    }

    private static async Task<byte[]> ReadExactAsync(NetworkStream stream, int expected, CancellationToken ct)
    {
        var buffer = new byte[expected];
        var read = 0;
        while (read < expected)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, expected - read), ct).ConfigureAwait(false);
            if (n <= 0)
                throw new EndOfStreamException($"MELSEC PLC 响应提前结束：期望 {expected} 字节，实际 {read} 字节。");
            read += n;
        }

        return buffer;
    }

    internal static object? Decode(byte[] response, AcquisitionSelectors.MelsecPoint point, int wordOffset = 0)
    {
        var required = 2 + (wordOffset + Math.Max(1, point.WordCount)) * 2;
        if (response.Length < required)
            throw new InvalidDataException(
                $"MELSEC PLC 响应长度不足：期望至少 {required} 字节，实际 {response.Length} 字节。");
        var data = response.AsSpan(2 + wordOffset * 2, Math.Max(1, point.WordCount) * 2);
        if (point.DataType == AcquisitionSelectors.BooleanDataType)
        {

            var word = BinaryPrimitives.ReadUInt16LittleEndian(data);
            var bit = point.BitIndex ?? 0;
            return (word & (1 << bit)) != 0;
        }

        return point.DataType switch
        {
            "int16" => BinaryPrimitives.ReadInt16LittleEndian(data),
            "uint16" => BinaryPrimitives.ReadUInt16LittleEndian(data),
            "int32" => BinaryPrimitives.ReadInt32LittleEndian(data),
            "uint32" => BinaryPrimitives.ReadUInt32LittleEndian(data),
            "float32" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data)),
            "int64" => BinaryPrimitives.ReadInt64LittleEndian(data),
            "uint64" => BinaryPrimitives.ReadUInt64LittleEndian(data),
            "float64" => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data)),
            "string" => DecodeAsciiString(
                data[..Math.Min(data.Length, point.ByteLength ?? data.Length)]),
            _ => null
        };
    }

    private static string DecodeAsciiString(ReadOnlySpan<byte> data)
    {
        if (data.ContainsAnyExceptInRange((byte)0x00, (byte)0x7F))
            throw new InvalidDataException("MELSEC PLC 字符串包含非 ASCII 字节；请修正点位类型或设备编码。");
        return System.Text.Encoding.ASCII.GetString(data).TrimEnd('\0');
    }

}
