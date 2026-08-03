using System.Buffers.Binary;
using System.Globalization;
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
///     三菱 MC 协议 A 兼容 1E 帧采集器 —— 用于 FX3U-ENET(-L/-ADP) 等设备。
///     不依赖 HslCommunication（商业授权）；MC/SLMP 是公开协议，本类直接构造 1E 帧字节。
///
///     帧字节布局按 FX3U-ENET-ADP User's Manual 的 A-compatible 1E frame：
///     软元件号在前，2 字节软元件代码按低字节到高字节发送（D 的线缆字节为 20H 44H）。
///
///     选择器语法与解析由 <see cref="AcquisitionSelectors"/> 提供，平台侧与本类共用同一份规则。
///
///     相对早期实现修正了三处语义：
///     <list type="number">
///       <item>位软元件（M/X/Y/B/S/L）读取布尔值时使用位单位批量读命令 0x00，
///             而不是一律用字单位批量读 0x01 —— 后者返回的是从该编号起 16 个点打包成的字，
///             把它当成单点状态是错的；</item>
///       <item>相邻点位按 <see cref="McA1EConnection.MaxMergeGap"/> 合并成一次读取，
///             不再每个点位一次 TCP 往返；</item>
///       <item>支持 <c>TimestampMode = source</c>，与 Modbus 采集器一致；
///             以前会接受该配置然后静默使用采集节点时间。</item>
///     </list>
/// </summary>
public sealed class MelsecA1EAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    ILogger<MelsecA1EAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.MelsecA1E;

    /// <summary>1E 帧的软元件代码（ASCII 两字节，按低字节到高字节发送）。</summary>
    private static readonly IReadOnlyDictionary<string, byte[]> DeviceCodes =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["D"] = " D"u8.ToArray(), ["W"] = " W"u8.ToArray(), ["R"] = " R"u8.ToArray(),
            ["M"] = " M"u8.ToArray(), ["X"] = " X"u8.ToArray(), ["Y"] = " Y"u8.ToArray(),
            ["B"] = " B"u8.ToArray(), ["T"] = " T"u8.ToArray(), ["C"] = " C"u8.ToArray(),
            ["L"] = " L"u8.ToArray(), ["S"] = " S"u8.ToArray(),
        };

    /// <summary>1E 帧字批量读取一次最多 256 个字；位批量读取一次最多 256 个点。</summary>
    private const int MaxWordsPerRead = 256;
    private const int MaxBitsPerRead = 256;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var connection = deployment.Profile.MelsecA1E
            ?? throw new InvalidOperationException("MELSEC 1E 连接配置不能为空。");
        var selectors = BuildSelectors(deployment);
        var plan = BuildReadPlan(selectors, connection.MaxMergeGap);
        string? currentRecipe = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcpClient = new TcpClient();
                await ConnectAsync(tcpClient, connection, deployment.Profile.Execution, ct).ConfigureAwait(false);
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
                    readTimeout.CancelAfter(Math.Max(1000, deployment.Profile.Execution.TimeoutMs));
                    Dictionary<string, object?> raw;
                    try
                    {
                        raw = await ReadSnapshotAsync(stream, connection, plan, readTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"读取 MELSEC PLC {connection.Host}:{connection.Port} 超过 {deployment.Profile.Execution.TimeoutMs}ms 未完成。");
                    }
                    var occurredAt = ResolveTimestamp(deployment.Profile, raw);
                    var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                        deployment, raw, normalizedSource, currentRecipe, occurredAt);
                    if (!sourceDeduplicator.ShouldEmit(mapped.Sample))
                    {
                        currentRecipe = mapped.RecipeIdentity;
                        status.RecordSuccess(
                            configurationKey,
                            DateTimeOffset.UtcNow,
                            currentRecipe,
                            incrementSample: false,
                            readDurationMs: System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);
                        await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }
                    await sink.EmitBatchAsync(
                        lifecycle.Track(mapped, deployment.Profile.Lifecycle, connection.PollIntervalMs),
                        ct).ConfigureAwait(false);

                    currentRecipe = mapped.RecipeIdentity;
                    status.RecordSuccess(configurationKey, DateTimeOffset.UtcNow, currentRecipe,
                        readDurationMs: System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);
                    await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "MELSEC 1E 采集任务 {Configuration} 读取失败，等待重连", configurationKey);
                await Task.Delay(deployment.Profile.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>建立连接时应用配置的超时；以前完全依赖操作系统默认值，半开连接会长时间挂起。</summary>
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
        AcquisitionProfile profile,
        IReadOnlyDictionary<string, object?> raw)
    {
        if (profile.TimestampMode != "source" || string.IsNullOrWhiteSpace(profile.TimestampPath))
            return DateTimeOffset.UtcNow;
        if (!raw.TryGetValue(profile.TimestampPath, out var value) || value is null)
            throw new InvalidOperationException($"配置的时间来源没有读到值：{profile.TimestampPath}。");
        return DateTimeOffset.FromUnixTimeMilliseconds(
            Convert.ToInt64(value, CultureInfo.InvariantCulture));
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

        foreach (var mapping in deployment.Profile.ValueMappings) Add(mapping.SourcePath);
        foreach (var mapping in deployment.Profile.ContextMappings) Add(mapping.SourcePath);
        if (deployment.Profile.TimestampMode == "source") Add(deployment.Profile.TimestampPath);
        if (deployment.Profile.Recipe is { } recipe)
        {
            Add(recipe.IdPath);
            Add(recipe.VersionPath);
            Add(recipe.NamePath);
            foreach (var mapping in recipe.ParameterMappings) Add(mapping.SourcePath);
        }

        return result;
    }

    /// <summary>一次 1E 读取请求覆盖的点位集合。</summary>
    internal sealed record McRead(
        string Device,
        byte[] DeviceCode,
        bool BitRead,
        uint Start,
        int Count,
        IReadOnlyList<KeyValuePair<string, AcquisitionSelectors.MelsecPoint>> Points);

    /// <summary>
    ///     把点位合并成尽量少的读取请求。同一软元件、同一读取方式（位/字）且编号间隔
    ///     不超过 <paramref name="maxMergeGap"/> 的点位合并成一次请求。
    ///     maxMergeGap 为 0 时退化为逐点读取。
    /// </summary>
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
                // maxMergeGap <= 0 表示显式关闭合并：每个点位单独一次读取。
                // 现场排查某个软元件是否可读时需要这个逃生口，因此不能因为
                // "间隔恰好为 0" 就把连续点位并进来。
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

    /// <summary>1E 帧字批量读取请求（命令 0x01）。</summary>
    internal static byte[] BuildWordReadFrame(
        byte[] deviceCode, uint address, int wordCount, ushort timer, string layout,
        byte pcNumber = 0xFF, string dataCode = "binary")
        => BuildReadFrame(0x01, deviceCode, address, wordCount, timer, layout, pcNumber, dataCode);

    /// <summary>
    ///     1E 帧位批量读取请求（命令 0x00）。
    ///     位软元件读取布尔值必须用这个命令；用字读取拿到的是 16 个点打包成的字。
    /// </summary>
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
        if (dataCode == "ascii")
        {
            // ASCII 码的各数值按 H→L 发送；设备代码 D 的逻辑值为 4420H。
            // 例如 D100/1 word: 01 FF 0010 00000064 4420 0001。
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
            var ascii = await ReadExactAsync(stream, 4 + wordCount * 4, ct).ConfigureAwait(false);
            var text = System.Text.Encoding.ASCII.GetString(ascii);
            EnsureAsciiSuccess(text);
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

        // 成功响应：[0]=0x81 [1]=结束码0x00 + wordCount*2 字节数据；错误：[1]!=0
        var buffer = await ReadExactAsync(stream, 2 + wordCount * 2, ct).ConfigureAwait(false);
        if (buffer[1] != 0x00)
            throw new InvalidOperationException($"MELSEC PLC 返回错误：结束码=0x{buffer[1]:X2}");
        return buffer;
    }

    /// <summary>
    ///     位批量读响应。二进制模式每个字节承载 2 个点（高半字节在前），
    ///     ASCII 模式每个点一个 '0' / '1' 字符。
    /// </summary>
    internal static async Task<bool[]> ReadBitResponseAsync(
        NetworkStream stream, int pointCount, string dataCode, CancellationToken ct)
    {
        var result = new bool[pointCount];
        if (dataCode == "ascii")
        {
            var ascii = await ReadExactAsync(stream, 4 + pointCount, ct).ConfigureAwait(false);
            var text = System.Text.Encoding.ASCII.GetString(ascii);
            EnsureAsciiSuccess(text);
            for (var index = 0; index < pointCount; index++)
                result[index] = text[4 + index] != '0';
            return result;
        }

        var buffer = await ReadExactAsync(stream, 2 + (pointCount + 1) / 2, ct).ConfigureAwait(false);
        if (buffer[1] != 0x00)
            throw new InvalidOperationException($"MELSEC PLC 返回错误：结束码=0x{buffer[1]:X2}");
        for (var index = 0; index < pointCount; index++)
        {
            var value = buffer[2 + index / 2];
            result[index] = (index % 2 == 0 ? value >> 4 : value & 0x0F) != 0;
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

    /// <summary>
    ///     按数据类型名解码的兼容重载。位软元件的布尔值由位批量读直接返回，
    ///     因此这个重载不接受 boolean。
    /// </summary>
    internal static object? Decode(byte[] response, string type, int wordCount)
    {
        if (type == AcquisitionSelectors.BooleanDataType)
            throw new InvalidOperationException("布尔值请使用位批量读或提供带位偏移的点位。");
        if (!AcquisitionSelectors.TryGetMelsecDevice("D", out var device))
            throw new InvalidOperationException("MELSEC 软元件表缺少 D。");
        return Decode(response, new AcquisitionSelectors.MelsecPoint(device, 0, "0", type, wordCount, null));
    }

    /// <param name="wordOffset">该点位在本次合并读取的数据块中的字偏移。</param>
    internal static object? Decode(byte[] response, AcquisitionSelectors.MelsecPoint point, int wordOffset = 0)
    {
        var required = 2 + (wordOffset + Math.Max(1, point.WordCount)) * 2;
        if (response.Length < required)
            throw new InvalidDataException(
                $"MELSEC PLC 响应长度不足：期望至少 {required} 字节，实际 {response.Length} 字节。");
        var data = response.AsSpan(2 + wordOffset * 2, Math.Max(1, point.WordCount) * 2);
        if (point.DataType == AcquisitionSelectors.BooleanDataType)
        {
            // 走到这里只可能是"字软元件 + 位偏移"；位软元件的布尔值由位批量读直接返回。
            var word = BinaryPrimitives.ReadUInt16LittleEndian(data);
            var bit = point.BitIndex ?? 0;
            return (word & (1 << bit)) != 0;
        }

        // 三菱字为小端；跨字的 32/64 位按低字在前拼接。
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
            "string" => System.Text.Encoding.ASCII.GetString(data).TrimEnd('\0'),
            _ => null
        };
    }

}
