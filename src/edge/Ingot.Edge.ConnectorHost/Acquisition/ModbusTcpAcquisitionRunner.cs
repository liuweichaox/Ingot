using System.Buffers.Binary;
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.Application.Abstractions;
using NModbus;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class ModbusTcpAcquisitionRunner(
    IEventSink sink,
    AcquisitionStatus status,
    ILogger<ModbusTcpAcquisitionRunner> logger) : IAcquisitionProtocolRunner
{
    public string Protocol => AcquisitionProtocols.ModbusTcp;

    public async Task RunAsync(
        string configurationKey,
        AcquisitionDeployment deployment,
        string normalizedSource,
        CancellationToken ct)
    {
        var connection = deployment.Profile.ModbusTcp
            ?? throw new InvalidOperationException("Modbus TCP 连接配置不能为空。");
        string? currentRecipe = null;
        var lifecycle = new AcquisitionLifecycleTracker();
        var sourceDeduplicator = new AcquisitionSourceDeduplicator();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcpClient = new TcpClient();
                await ConnectAsync(tcpClient, connection, deployment.Profile.Execution, ct).ConfigureAwait(false);
                var factory = new ModbusFactory();
                using var master = factory.CreateMaster(tcpClient);
                logger.LogInformation(
                    "Modbus TCP 采集任务已连接：Configuration={Configuration}, Device={Host}:{Port}, Unit={UnitId}",
                    configurationKey, connection.Host, connection.Port, connection.UnitId);
                while (!ct.IsCancellationRequested && tcpClient.Connected)
                {
                    var readStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    readTimeout.CancelAfter(Math.Max(1000, deployment.Profile.Execution.TimeoutMs));
                    var selectors = BuildSelectors(deployment, connection.AddressBase);
                    Dictionary<string, object?> raw;
                    try
                    {
                        raw = await ReadSnapshotAsync(master, connection.UnitId, selectors, readTimeout.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new TimeoutException(
                            $"读取 Modbus 设备 {connection.Host}:{connection.Port} 超过 {deployment.Profile.Execution.TimeoutMs}ms 未完成。");
                    }
                    var occurredAt = DateTimeOffset.UtcNow;
                    if (deployment.Profile.TimestampMode == "source" &&
                        !string.IsNullOrWhiteSpace(deployment.Profile.TimestampPath))
                    {
                        var sourceTimestamp = raw[deployment.Profile.TimestampPath];
                        occurredAt = DateTimeOffset.FromUnixTimeMilliseconds(
                            Convert.ToInt64(sourceTimestamp, System.Globalization.CultureInfo.InvariantCulture));
                    }
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
                    var events = lifecycle.Track(
                        mapped,
                        deployment.Profile.Lifecycle,
                        connection.PollIntervalMs);
                    await sink.EmitBatchAsync(events, ct).ConfigureAwait(false);
                    status.RecordCycleState(configurationKey, lifecycle.IsRunActive);
                    currentRecipe = mapped.RecipeIdentity;
                    status.RecordSuccess(
                        configurationKey,
                        DateTimeOffset.UtcNow,
                        currentRecipe,
                        readDurationMs: System.Diagnostics.Stopwatch.GetElapsedTime(readStarted).TotalMilliseconds);
                    await Task.Delay(connection.PollIntervalMs, ct).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                status.RecordFailure(configurationKey, exception.Message);
                logger.LogWarning(exception, "Modbus TCP 采集任务 {Configuration} 读取失败，等待重连", configurationKey);
                await Task.Delay(deployment.Profile.Execution.ReconnectDelayMs, ct).ConfigureAwait(false);
            }
        }
    }

    internal static IReadOnlyDictionary<string, AcquisitionValueMapping> BuildSelectors(
        AcquisitionDeployment deployment,
        string addressBase = "zero-based")
    {
        var result = new Dictionary<string, AcquisitionValueMapping>(StringComparer.Ordinal);
        foreach (var mapping in deployment.Profile.ValueMappings)
            result[mapping.SourcePath] = NormalizeAddress(mapping, addressBase);
        foreach (var mapping in deployment.Profile.ContextMappings)
            result[mapping.SourcePath] = NormalizeAddress(ParseSelector(mapping.SourcePath), addressBase);
        if (deployment.Profile.TimestampMode == "source" &&
            !string.IsNullOrWhiteSpace(deployment.Profile.TimestampPath))
        {
            result[deployment.Profile.TimestampPath] = NormalizeAddress(
                ParseSelector(deployment.Profile.TimestampPath), addressBase);
        }
        if (deployment.Profile.Recipe is { } recipe)
        {
            result[recipe.IdPath] = NormalizeAddress(ParseSelector(recipe.IdPath), addressBase);
            result[recipe.VersionPath] = NormalizeAddress(ParseSelector(recipe.VersionPath), addressBase);
            if (!string.IsNullOrWhiteSpace(recipe.NamePath))
                result[recipe.NamePath] = NormalizeAddress(ParseSelector(recipe.NamePath), addressBase);
            foreach (var mapping in recipe.ParameterMappings)
                result[mapping.SourcePath] = NormalizeAddress(mapping, addressBase);
        }
        return result;
    }

    private static AcquisitionValueMapping NormalizeAddress(
        AcquisitionValueMapping mapping,
        string addressBase)
    {
        if (addressBase == "zero-based") return mapping;
        if (addressBase != "one-based")
            throw new InvalidOperationException($"Modbus 地址起点无效：{addressBase}。");
        var address = mapping.ModbusAddress
            ?? throw new InvalidOperationException($"Modbus 选择器缺少地址：{mapping.SourcePath}。");
        if (address == 0)
            throw new InvalidOperationException("使用 1 基地址时，寄存器地址必须大于 0。");
        return mapping with { ModbusAddress = checked((ushort)(address - 1)) };
    }

    /// <summary>
    ///     标量选择器（上下文、配方、时间戳来源）的解析交给公共契约中的
    ///     <see cref="AcquisitionSelectors"/>，与平台保存配置时使用的是同一份规则。
    /// </summary>
    private static AcquisitionValueMapping ParseSelector(string selector)
    {
        if (!AcquisitionSelectors.TryParseModbus(selector, out var point, out var error))
            throw new InvalidOperationException(error);
        return new AcquisitionValueMapping
        {
            DataItemCode = selector,
            SourcePath = selector,
            ModbusArea = point.Area,
            ModbusAddress = point.Address,
            ModbusQuantity = point.Quantity,
            SourceDataType = point.DataType,
            ByteOrder = point.ByteOrder,
            WordOrder = point.WordOrder,
            BitIndex = point.BitIndex
        };
    }

    /// <summary>建立连接时应用配置的超时；以前依赖操作系统默认值，半开连接会长时间挂起。</summary>
    private static async Task ConnectAsync(
        TcpClient client,
        ModbusTcpConnection connection,
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
            throw new TimeoutException($"连接 Modbus 设备 {connection.Host}:{connection.Port} 超过 {timeout}ms 未完成。");
        }
    }

    internal static async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        IModbusMaster master,
        byte unitId,
        IReadOnlyDictionary<string, AcquisitionValueMapping> selectors,
        CancellationToken ct = default)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var area in selectors.GroupBy(item => item.Value.ModbusArea, StringComparer.Ordinal))
        {
            var pending = area.OrderBy(item => item.Value.ModbusAddress).ToList();
            while (pending.Count > 0)
            {
                var start = pending[0].Value.ModbusAddress
                    ?? throw new InvalidOperationException($"Modbus 选择器缺少地址：{pending[0].Key}。");
                var maxQuantity = area.Key is "coil" or "discrete-input" ? 2000 : 125;
                var included = pending
                    .TakeWhile(item =>
                    {
                        var address = item.Value.ModbusAddress!.Value;
                        return address + item.Value.ModbusQuantity <= start + maxQuantity;
                    })
                    .ToArray();
                var end = included.Max(item =>
                    item.Value.ModbusAddress!.Value + item.Value.ModbusQuantity);
                var quantity = checked((ushort)(end - start));
                if (area.Key is "coil" or "discrete-input")
                {
                    var block = area.Key == "coil"
                        ? await master.ReadCoilsAsync(unitId, start, quantity).WaitAsync(ct).ConfigureAwait(false)
                        : await master.ReadInputsAsync(unitId, start, quantity).WaitAsync(ct).ConfigureAwait(false);
                    foreach (var item in included)
                    {
                        var offset = item.Value.ModbusAddress!.Value - start;
                        result[item.Key] = block[offset];
                    }
                }
                else
                {
                    var block = area.Key switch
                    {
                        "holding-register" => await master.ReadHoldingRegistersAsync(unitId, start, quantity).WaitAsync(ct)
                            .ConfigureAwait(false),
                        "input-register" => await master.ReadInputRegistersAsync(unitId, start, quantity).WaitAsync(ct)
                            .ConfigureAwait(false),
                        _ => throw new InvalidOperationException($"Modbus 寄存器区无效：{area.Key}。")
                    };
                    foreach (var item in included)
                    {
                        var offset = item.Value.ModbusAddress!.Value - start;
                        var registers = block
                            .Skip(offset)
                            .Take(item.Value.ModbusQuantity)
                            .ToArray();
                        result[item.Key] = Decode(registers, item.Value);
                    }
                }
                pending.RemoveRange(0, included.Length);
            }
        }
        return result;
    }

    internal static object Decode(ushort[] registers, AcquisitionValueMapping mapping)
    {
        if (registers.Length == 0)
            throw new InvalidDataException($"数据项 {mapping.DataItemCode} 没有返回寄存器值。");
        var ordered = registers.ToArray();
        if (mapping.WordOrder == "low-high" && ordered.Length > 1)
            Array.Reverse(ordered);
        var bytes = new byte[ordered.Length * 2];
        for (var index = 0; index < ordered.Length; index++)
        {
            if (mapping.ByteOrder == "little-endian")
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2, 2), ordered[index]);
            else
                BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(index * 2, 2), ordered[index]);
        }

        var type = mapping.SourceDataType == "auto"
            ? registers.Length == 1 ? "uint16" : "float32"
            : mapping.SourceDataType;
        if (type == AcquisitionSelectors.BooleanDataType)
        {
            // 保持/输入寄存器取单个位。线圈与离散输入区不会走到这里，
            // 它们在 ReadSnapshotAsync 中直接返回布尔值。
            var bit = mapping.BitIndex
                ?? throw new InvalidOperationException(
                    $"数据项 {mapping.DataItemCode} 从寄存器读取布尔值时必须指定位偏移。");
            return (ReadUInt16(bytes, mapping.ByteOrder) & (1 << bit)) != 0;
        }

        return type switch
        {
            "int16" => ReadInt16(bytes, mapping.ByteOrder),
            "uint16" => ReadUInt16(bytes, mapping.ByteOrder),
            "int32" => ReadInt32(bytes, mapping.ByteOrder),
            "uint32" => ReadUInt32(bytes, mapping.ByteOrder),
            "float32" => BitConverter.Int32BitsToSingle(ReadInt32(bytes, mapping.ByteOrder)),
            "int64" => ReadInt64(bytes, mapping.ByteOrder),
            "uint64" => ReadUInt64(bytes, mapping.ByteOrder),
            "float64" => BitConverter.Int64BitsToDouble(ReadInt64(bytes, mapping.ByteOrder)),
            "string" => System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0'),
            _ => throw new InvalidOperationException(
                $"数据项 {mapping.DataItemCode} 的 Modbus 源数据类型无效：{type}。")
        };
    }

    private static short ReadInt16(byte[] value, string order)
        => order == "little-endian"
            ? BinaryPrimitives.ReadInt16LittleEndian(value)
            : BinaryPrimitives.ReadInt16BigEndian(value);
    private static ushort ReadUInt16(byte[] value, string order)
        => order == "little-endian"
            ? BinaryPrimitives.ReadUInt16LittleEndian(value)
            : BinaryPrimitives.ReadUInt16BigEndian(value);
    private static int ReadInt32(byte[] value, string order)
        => order == "little-endian"
            ? BinaryPrimitives.ReadInt32LittleEndian(value)
            : BinaryPrimitives.ReadInt32BigEndian(value);
    private static uint ReadUInt32(byte[] value, string order)
        => order == "little-endian"
            ? BinaryPrimitives.ReadUInt32LittleEndian(value)
            : BinaryPrimitives.ReadUInt32BigEndian(value);
    private static long ReadInt64(byte[] value, string order)
        => order == "little-endian"
            ? BinaryPrimitives.ReadInt64LittleEndian(value)
            : BinaryPrimitives.ReadInt64BigEndian(value);
    private static ulong ReadUInt64(byte[] value, string order)
        => order == "little-endian"
            ? BinaryPrimitives.ReadUInt64LittleEndian(value)
            : BinaryPrimitives.ReadUInt64BigEndian(value);
}
