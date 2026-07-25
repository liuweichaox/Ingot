using System.Buffers.Binary;
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
///     三菱 MC 协议 1E 帧（二进制）采集器 —— 用于 FX3U-ENET-ADP 等 A 兼容 1E 帧设备。
///     不依赖 HslCommunication（商业授权）；MC/SLMP 是公开协议，本类直接构造 1E 帧字节。
///
///     帧字节布局按 FX3U-ENET-ADP User's Manual 的 A-compatible 1E binary frame：
///     软元件号在前，2 字节软元件代码按低字节到高字节发送（D 的线缆字节为 20H 44H）。
///
///     选择器格式：软元件:地址:类型，例如 D:100:int16 / D:200:float32（float32/int32 读 2 个字）。
///     与 Modbus 采集器共用后续管道（ProtocolAcquisitionSnapshotMapper / 生命周期 / outbox / 事件契约），
///     本类只负责"传输 + 1E 帧编解码"这一薄层——这正是"加一个协议 ≈ 10% 工作量"的体现。
/// </summary>
public sealed class MelsecA1EAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    ILogger<MelsecA1EAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.MelsecA1E;

    private static readonly IReadOnlyDictionary<string, byte[]> DeviceCodes =
        new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["D"] = " D"u8.ToArray(), ["W"] = " W"u8.ToArray(), ["R"] = " R"u8.ToArray(),
            ["M"] = " M"u8.ToArray(), ["X"] = " X"u8.ToArray(), ["Y"] = " Y"u8.ToArray(),
            ["B"] = " B"u8.ToArray(), ["T"] = " T"u8.ToArray(), ["C"] = " C"u8.ToArray(),
            ["L"] = " L"u8.ToArray(), ["S"] = " S"u8.ToArray(),
        };

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var connection = deployment.Profile.MelsecA1E
            ?? throw new InvalidOperationException("MELSEC 1E 连接配置不能为空。");
        string? currentRecipe = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(connection.Host, connection.Port, ct).ConfigureAwait(false);
                using var stream = tcpClient.GetStream();
                logger.LogInformation(
                    "MELSEC 1E 采集任务已连接：Configuration={Configuration}, Device={Host}:{Port}",
                    configurationKey, connection.Host, connection.Port);
                while (!ct.IsCancellationRequested && tcpClient.Connected)
                {
                    var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                    var selectors = BuildSelectors(deployment);
                    var raw = await ReadSnapshotAsync(stream, connection, selectors, ct).ConfigureAwait(false);
                    var occurredAt = DateTimeOffset.UtcNow;
                    var mapped = ProtocolAcquisitionSnapshotMapper.Map(
                        deployment, raw, normalizedSource, currentRecipe, occurredAt);
                    foreach (var productionEvent in lifecycle.Track(
                                 mapped, deployment.Profile.Lifecycle, connection.PollIntervalMs))
                    {
                        await sink.EmitAsync(productionEvent, ct).ConfigureAwait(false);
                    }
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

    private static IReadOnlyDictionary<string, McSelector> BuildSelectors(AcquisitionDeployment deployment)
    {
        var result = new Dictionary<string, McSelector>(StringComparer.Ordinal);
        void Add(string path) => result[path] = ParseSelector(path);
        foreach (var mapping in deployment.Profile.ValueMappings) Add(mapping.SourcePath);
        foreach (var mapping in deployment.Profile.ContextMappings) Add(mapping.SourcePath);
        if (deployment.Profile.Recipe is { } recipe)
        {
            Add(recipe.IdPath);
            Add(recipe.VersionPath);
            if (!string.IsNullOrWhiteSpace(recipe.NamePath)) Add(recipe.NamePath);
            foreach (var mapping in recipe.ParameterMappings) Add(mapping.SourcePath);
        }
        return result;
    }

    private static McSelector ParseSelector(string selector)
    {
        // 软元件:地址:类型，例如 D:100:int16
        var parts = selector.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !DeviceCodes.TryGetValue(parts[0], out var code) ||
            !uint.TryParse(parts[1], out var address))
        {
            throw new InvalidOperationException(
                $"MELSEC 选择器无效：{selector}。应使用 软元件:地址:类型（如 D:100:int16）。");
        }
        var type = parts[2];
        var words = type switch
        {
            "int16" or "uint16" => 1,
            "int32" or "uint32" or "float32" => 2,
            "int64" or "uint64" or "float64" => 4,
            _ => throw new InvalidOperationException($"MELSEC 暂不支持的数据类型：{type}。")
        };
        return new McSelector(selector, parts[0].ToUpperInvariant(), code, address, type, words);
    }

    private static async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        NetworkStream stream, McA1EConnection connection,
        IReadOnlyDictionary<string, McSelector> selectors, CancellationToken ct)
    {
        var snapshot = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (path, sel) in selectors)
        {
            var request = BuildWordReadFrame(sel.DeviceCode, sel.Address, sel.WordCount,
                connection.MonitoringTimer, connection.WordOrderLayout);
            await stream.WriteAsync(request, ct).ConfigureAwait(false);
            var response = await ReadResponseAsync(stream, sel.WordCount, ct).ConfigureAwait(false);
            snapshot[path] = Decode(response, sel.Type, sel.WordCount);
        }
        return snapshot;
    }

    /// <summary>1E 帧字批量读取请求（命令 0x01）。</summary>
    internal static byte[] BuildWordReadFrame(byte[] deviceCode, uint address, int wordCount, ushort timer, string layout)
    {
        var head = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(head, address);
        var timerBytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(timerBytes, timer);
        if (!string.Equals(layout, "A", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("FX3U-ENET-ADP 的 A-compatible 1E 帧只支持软元件号在前的布局 A。");
        var body = head.Concat(deviceCode);
        return new byte[] { 0x01, 0xFF }
            .Concat(timerBytes)
            .Concat(body)
            .Concat(new byte[] { (byte)(wordCount == 256 ? 0 : wordCount), 0x00 })
            .ToArray();
    }

    private static async Task<byte[]> ReadResponseAsync(NetworkStream stream, int wordCount, CancellationToken ct)
    {
        // 成功响应：[0]=0x81 [1]=结束码0x00 + wordCount*2 字节数据；错误：[1]!=0 (+异常码)
        var expected = 2 + wordCount * 2;
        var buffer = new byte[Math.Max(expected, 4)];
        var read = 0;
        while (read < 2)
            read += await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
        if (buffer[1] != 0x00)
            throw new InvalidOperationException($"MELSEC PLC 返回错误：结束码=0x{buffer[1]:X2}");
        while (read < expected)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read, expected - read), ct).ConfigureAwait(false);
            if (n <= 0) break;
            read += n;
        }
        return buffer;
    }

    internal static object? Decode(byte[] response, string type, int wordCount)
    {
        if (response.Length < 2 + wordCount * 2)
            throw new InvalidDataException(
                $"MELSEC PLC 响应长度不足：期望至少 {2 + wordCount * 2} 字节，实际 {response.Length} 字节。");
        var data = response.AsSpan(2, wordCount * 2);
        // 三菱字为小端；跨字的 32/64 位按低字在前拼接。
        return type switch
        {
            "int16" => BinaryPrimitives.ReadInt16LittleEndian(data),
            "uint16" => BinaryPrimitives.ReadUInt16LittleEndian(data),
            "int32" => BinaryPrimitives.ReadInt32LittleEndian(data),
            "uint32" => BinaryPrimitives.ReadUInt32LittleEndian(data),
            "float32" => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data)),
            "int64" => BinaryPrimitives.ReadInt64LittleEndian(data),
            "uint64" => BinaryPrimitives.ReadUInt64LittleEndian(data),
            "float64" => BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data)),
            _ => null
        };
    }

    private sealed record McSelector(
        string SourcePath, string Device, byte[] DeviceCode, uint Address, string Type, int WordCount);
}
