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
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(connection.Host, connection.Port, ct).ConfigureAwait(false);
                var factory = new ModbusFactory();
                using var master = factory.CreateMaster(tcpClient);
                logger.LogInformation(
                    "Modbus TCP 采集任务已连接：Configuration={Configuration}, Device={Host}:{Port}, Unit={UnitId}",
                    configurationKey, connection.Host, connection.Port, connection.UnitId);
                string? currentRecipe = null;
                var lifecycle = new AcquisitionLifecycleTracker();

                while (!ct.IsCancellationRequested && tcpClient.Connected)
                {
                    var pollStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    status.RecordAttempt(configurationKey, DateTimeOffset.UtcNow);
                    var selectors = BuildSelectors(deployment);
                    var raw = await ReadSnapshotAsync(master, connection.UnitId, selectors)
                        .ConfigureAwait(false);
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
                    foreach (var productionEvent in lifecycle.Track(
                                 mapped,
                                 deployment.Profile.Lifecycle,
                                 deployment.DataModel.Acquisition.SamplePeriodMs))
                    {
                        await sink.EmitAsync(productionEvent, ct).ConfigureAwait(false);
                    }
                    currentRecipe = mapped.RecipeIdentity;
                    status.RecordSuccess(configurationKey, DateTimeOffset.UtcNow, currentRecipe);
                    var remaining = TimeSpan.FromMilliseconds(connection.PollIntervalMs) -
                                    System.Diagnostics.Stopwatch.GetElapsedTime(pollStarted);
                    if (remaining > TimeSpan.Zero)
                        await Task.Delay(remaining, ct).ConfigureAwait(false);
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

    private static IReadOnlyDictionary<string, AcquisitionValueMapping> BuildSelectors(
        AcquisitionDeployment deployment)
    {
        var result = new Dictionary<string, AcquisitionValueMapping>(StringComparer.Ordinal);
        foreach (var mapping in deployment.Profile.ValueMappings)
            result[mapping.SourcePath] = mapping;
        foreach (var mapping in deployment.Profile.ContextMappings)
            result[mapping.SourcePath] = ParseSelector(mapping.SourcePath);
        if (deployment.Profile.TimestampMode == "source" &&
            !string.IsNullOrWhiteSpace(deployment.Profile.TimestampPath))
        {
            result[deployment.Profile.TimestampPath] = ParseSelector(deployment.Profile.TimestampPath);
        }
        if (deployment.Profile.Recipe is { } recipe)
        {
            result[recipe.IdPath] = ParseSelector(recipe.IdPath);
            result[recipe.VersionPath] = ParseSelector(recipe.VersionPath);
            if (!string.IsNullOrWhiteSpace(recipe.NamePath))
                result[recipe.NamePath] = ParseSelector(recipe.NamePath);
            foreach (var mapping in recipe.ParameterMappings)
                result[mapping.SourcePath] = mapping;
        }
        return result;
    }

    private static AcquisitionValueMapping ParseSelector(string selector)
    {
        var parts = selector.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length < 3 ||
            !ushort.TryParse(parts[1], out var address))
        {
            throw new InvalidOperationException(
                $"Modbus 标量选择器无效：{selector}。应使用 area:address:type 格式。");
        }
        var dataType = parts[2];
        var quantity = dataType switch
        {
            "int16" or "uint16" => 1,
            "int32" or "uint32" or "float32" => 2,
            "int64" or "uint64" or "float64" => 4,
            "boolean" => 1,
            "string" when parts.Length > 3 && ushort.TryParse(parts[3], out var length)
                => (length + 1) / 2,
            _ => throw new InvalidOperationException($"Modbus 标量选择器类型无效：{dataType}。")
        };
        return new AcquisitionValueMapping
        {
            DataItemCode = selector,
            SourcePath = selector,
            ModbusArea = parts[0],
            ModbusAddress = address,
            ModbusQuantity = (ushort)quantity,
            SourceDataType = dataType == "boolean" ? "uint16" : dataType,
            ByteOrder = dataType == "string"
                ? "big-endian"
                : parts.Length > 3 ? parts[3] : "big-endian",
            WordOrder = dataType == "string"
                ? "high-low"
                : parts.Length > 4 ? parts[4] : "high-low"
        };
    }

    private static async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        IModbusMaster master,
        byte unitId,
        IReadOnlyDictionary<string, AcquisitionValueMapping> selectors)
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
                        ? await master.ReadCoilsAsync(unitId, start, quantity).ConfigureAwait(false)
                        : await master.ReadInputsAsync(unitId, start, quantity).ConfigureAwait(false);
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
                        "holding-register" => await master.ReadHoldingRegistersAsync(unitId, start, quantity)
                            .ConfigureAwait(false),
                        "input-register" => await master.ReadInputRegistersAsync(unitId, start, quantity)
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

    private static async Task<object> ReadAsync(
        IModbusMaster master,
        byte unitId,
        AcquisitionValueMapping mapping)
    {
        var address = mapping.ModbusAddress
            ?? throw new InvalidOperationException($"数据项 {mapping.DataItemCode} 缺少 Modbus 地址。");
        return mapping.ModbusArea switch
        {
            "coil" => (await master.ReadCoilsAsync(unitId, address, mapping.ModbusQuantity)
                .ConfigureAwait(false))[0],
            "discrete-input" => (await master.ReadInputsAsync(unitId, address, mapping.ModbusQuantity)
                .ConfigureAwait(false))[0],
            "input-register" => Decode(
                await master.ReadInputRegistersAsync(unitId, address, mapping.ModbusQuantity)
                    .ConfigureAwait(false),
                mapping),
            "holding-register" => Decode(
                await master.ReadHoldingRegistersAsync(unitId, address, mapping.ModbusQuantity)
                    .ConfigureAwait(false),
                mapping),
            _ => throw new InvalidOperationException(
                $"数据项 {mapping.DataItemCode} 的 Modbus 寄存器区无效：{mapping.ModbusArea}。")
        };
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
