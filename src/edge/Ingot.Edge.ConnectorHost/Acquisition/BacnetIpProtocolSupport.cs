// Reads bounded BACnet/IP object directories and Present_Value through a pinned unicast-only UDP transport.
using System.Buffers.Binary;
using System.IO.BACnet;
using System.IO.BACnet.Serialize;
using System.Net;
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class BacnetIpProtocolSupport
{
    public const int MaximumObjectListEntries = 512;
    public const int MaximumConfiguredPoints = 128;
    public const int MaximumConcurrentReads = 8;

    public static IReadOnlyList<BacnetSelectedObject> BuildSelectors(AcquisitionDeployment deployment)
    {
        var task = deployment.Task;
        var selectors = new Dictionary<string, BacnetSelectedObject>(StringComparer.Ordinal);

        void Add(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                return;
            if (!AcquisitionSelectors.TryParseBacnetObject(sourcePath, out var point, out var error))
                throw new InvalidOperationException(error);

            var objectType = ToBacnetObjectType(point!.ObjectType);
            var normalizedPath = AcquisitionSelectors.FormatBacnetObject(point);
            selectors.TryAdd(normalizedPath, new BacnetSelectedObject(
                normalizedPath,
                new BacnetObjectId(objectType, point.Instance),
                point.ObjectType));
        }

        foreach (var mapping in task.ValueMappings)
            Add(mapping.SourcePath);
        foreach (var mapping in task.ContextMappings)
            Add(mapping.SourcePath);
        if (task.ProcessSpecification is { } processSpecification)
        {
            Add(processSpecification.IdPath);
            Add(processSpecification.VersionPath);
            Add(processSpecification.NamePath);
            foreach (var mapping in processSpecification.ParameterMappings)
                Add(mapping.SourcePath);
        }

        if (selectors.Count == 0)
            throw new InvalidOperationException("BACnet 运行任务至少需要一个已配置的对象点位。");
        if (selectors.Count > MaximumConfiguredPoints)
        {
            throw new InvalidOperationException(
                $"BACnet 单任务最多读取 {MaximumConfiguredPoints} 个不同对象点位，当前配置 {selectors.Count} 个。");
        }
        return selectors.Values.ToArray();
    }

    public static async Task<BacnetIpClientSession> OpenAsync(
        BacnetIpConnection connection,
        AcquisitionHttpEgressPolicy egressPolicy,
        int timeoutMs,
        ILogger logger,
        CancellationToken ct)
    {
        if (!IPAddress.TryParse(connection.Host?.Trim(), out var configuredAddress) ||
            configuredAddress.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new InvalidOperationException("BACnet/IP 首期要求填写 IPv4 字面地址，不能使用主机名或 IPv6 地址。");
        }
        if (connection.Port is < 1 or > 65535)
            throw new InvalidOperationException("BACnet/IP UDP 端口必须在 1-65535 之间。");

        var pinnedHost = await egressPolicy.ResolvePinnedHostAsync(
            configuredAddress.ToString(),
            "BACnet/IP",
            ct).ConfigureAwait(false);
        if (!IPAddress.TryParse(pinnedHost, out var pinnedAddress) ||
            pinnedAddress.AddressFamily != AddressFamily.InterNetwork ||
            !pinnedAddress.Equals(configuredAddress))
        {
            throw new InvalidOperationException("BACnet/IP 出站策略未返回与配置相同的固定 IPv4 地址，已拒绝连接。");
        }
        EnsureUnicast(pinnedAddress);

        var remote = new IPEndPoint(pinnedAddress, connection.Port);
        var localAddress = ResolveLocalAddress(remote);
        var timeout = Math.Clamp(timeoutMs, 500, 30_000);
        var transport = new PinnedBacnetIpUdpProtocolTransport(localAddress, remote, logger);
        var client = new BacnetClient(transport, timeout, retries: 0);
        try
        {
            client.Start();
            return new BacnetIpClientSession(
                client,
                transport,
                remote,
                connection.DeviceInstance,
                TimeSpan.FromMilliseconds(timeout));
        }
        catch
        {
            client.Dispose();
            transport.Dispose();
            throw;
        }
    }

    public static IReadOnlyList<AcquisitionProbePoint> BuildDirectoryPoints(
        IReadOnlyList<BacnetObjectId> objectList)
        => objectList
            .Where(static item => TryGetObjectTypeName(item.Type, out _))
            .Select(item =>
            {
                _ = TryGetObjectTypeName(item.Type, out var objectType);
                return new AcquisitionProbePoint
                {
                    Path = AcquisitionSelectors.FormatBacnetObject(
                        new AcquisitionSelectors.BacnetObjectSelector(objectType, item.Instance)),
                    Name = $"{objectType} · {item.Instance}",
                    Kind = $"bacnet-{objectType}",
                    DataType = objectType.StartsWith("analog-", StringComparison.Ordinal)
                        ? "real"
                        : objectType.StartsWith("binary-", StringComparison.Ordinal)
                            ? "enumerated"
                            : "unsigned"
                };
            })
            .ToArray();

    private static IPAddress ResolveLocalAddress(IPEndPoint remote)
    {
        // UDP Connect resolves only the local route; it sends no datagram and the remote endpoint is already pinned.
        using var routeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        routeSocket.Connect(remote);
        if (routeSocket.LocalEndPoint is not IPEndPoint { Address: var localAddress } ||
            localAddress.Equals(IPAddress.Any))
        {
            throw new InvalidOperationException("无法为 BACnet/IP 固定的 IPv4 目标选择本地网卡地址。");
        }
        return localAddress;
    }

    private static void EnsureUnicast(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast) ||
            bytes[0] is >= 224 and <= 239)
        {
            throw new InvalidOperationException("BACnet/IP 目标必须是可单播的 IPv4 设备地址。");
        }
    }

    private static BacnetObjectTypes ToBacnetObjectType(string objectType) => objectType switch
    {
        "analog-input" => BacnetObjectTypes.OBJECT_ANALOG_INPUT,
        "analog-output" => BacnetObjectTypes.OBJECT_ANALOG_OUTPUT,
        "analog-value" => BacnetObjectTypes.OBJECT_ANALOG_VALUE,
        "binary-input" => BacnetObjectTypes.OBJECT_BINARY_INPUT,
        "binary-output" => BacnetObjectTypes.OBJECT_BINARY_OUTPUT,
        "binary-value" => BacnetObjectTypes.OBJECT_BINARY_VALUE,
        "multi-state-input" => BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT,
        "multi-state-output" => BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT,
        "multi-state-value" => BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE,
        _ => throw new InvalidOperationException($"BACnet 对象类型 {objectType} 不支持 Present_Value 采集。")
    };

    private static bool TryGetObjectTypeName(BacnetObjectTypes type, out string name)
    {
        name = type switch
        {
            BacnetObjectTypes.OBJECT_ANALOG_INPUT => "analog-input",
            BacnetObjectTypes.OBJECT_ANALOG_OUTPUT => "analog-output",
            BacnetObjectTypes.OBJECT_ANALOG_VALUE => "analog-value",
            BacnetObjectTypes.OBJECT_BINARY_INPUT => "binary-input",
            BacnetObjectTypes.OBJECT_BINARY_OUTPUT => "binary-output",
            BacnetObjectTypes.OBJECT_BINARY_VALUE => "binary-value",
            BacnetObjectTypes.OBJECT_MULTI_STATE_INPUT => "multi-state-input",
            BacnetObjectTypes.OBJECT_MULTI_STATE_OUTPUT => "multi-state-output",
            BacnetObjectTypes.OBJECT_MULTI_STATE_VALUE => "multi-state-value",
            _ => string.Empty
        };
        return name.Length > 0;
    }
}

internal sealed record BacnetSelectedObject(string SourcePath, BacnetObjectId ObjectId, string ObjectType);

internal sealed class BacnetIpClientSession : IDisposable
{
    private readonly BacnetClient _client;
    private readonly PinnedBacnetIpUdpProtocolTransport _transport;
    private readonly BacnetAddress _address;
    private readonly BacnetObjectId _deviceObjectId;
    private readonly TimeSpan _operationTimeout;
    private bool _disposed;

    public BacnetIpClientSession(
        BacnetClient client,
        PinnedBacnetIpUdpProtocolTransport transport,
        IPEndPoint remote,
        uint deviceInstance,
        TimeSpan operationTimeout)
    {
        _client = client;
        _transport = transport;
        _address = new BacnetAddress(BacnetAddressTypes.IP, $"{remote.Address}:{remote.Port}");
        _deviceObjectId = new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceInstance);
        _operationTimeout = operationTimeout;
    }

    public async Task VerifyDeviceIdentifierAsync(CancellationToken ct)
    {
        var values = await RunWithTimeoutAsync(
            token => ReadPropertyAsync(
                _deviceObjectId,
                BacnetPropertyIds.PROP_OBJECT_IDENTIFIER,
                uint.MaxValue,
                token),
            "Device Object Identifier",
            ct).ConfigureAwait(false);
        if (values[0].Value is not BacnetObjectId identifier ||
            identifier.Type != BacnetObjectTypes.OBJECT_DEVICE ||
            identifier.Instance != _deviceObjectId.Instance)
        {
            throw new InvalidDataException(
                $"BACnet/IP Device Object Identifier 与配置的 Device Instance {_deviceObjectId.Instance} 不一致。");
        }
    }

    public async Task<IReadOnlyList<BacnetObjectId>> ReadObjectListAsync(CancellationToken ct)
    {
        return await RunWithTimeoutAsync(async token =>
        {
            var countValues = await ReadPropertyAsync(
                _deviceObjectId,
                BacnetPropertyIds.PROP_OBJECT_LIST,
                0,
                token).ConfigureAwait(false);
            if (!TryConvertObjectCount(countValues[0].Value, out var count))
                throw new InvalidDataException("BACnet Device Object_List[0] 没有返回有效的对象总数。");
            if (count > BacnetIpProtocolSupport.MaximumObjectListEntries)
            {
                throw new InvalidOperationException(
                    $"BACnet Device Object_List 包含 {count} 个对象，超过单设备上限 {BacnetIpProtocolSupport.MaximumObjectListEntries}；请按现场设备边界拆分采集。");
            }

            var objects = new BacnetObjectId[checked((int)count)];
            using var concurrency = new SemaphoreSlim(
                BacnetIpProtocolSupport.MaximumConcurrentReads,
                BacnetIpProtocolSupport.MaximumConcurrentReads);
            var reads = Enumerable.Range(0, objects.Length).Select(async offset =>
            {
                await concurrency.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var index = checked((uint)offset + 1);
                    var values = await ReadPropertyAsync(
                        _deviceObjectId,
                        BacnetPropertyIds.PROP_OBJECT_LIST,
                        index,
                        token).ConfigureAwait(false);
                    if (values[0].Value is not BacnetObjectId objectId)
                        throw new InvalidDataException($"BACnet Device Object_List[{index}] 没有返回有效的对象标识符。");
                    objects[offset] = objectId;
                }
                finally
                {
                    concurrency.Release();
                }
            }).ToArray();
            await Task.WhenAll(reads).ConfigureAwait(false);

            if (!objects.Any(objectId =>
                    objectId.Type == BacnetObjectTypes.OBJECT_DEVICE && objectId.Instance == _deviceObjectId.Instance))
            {
                throw new InvalidDataException("BACnet Device Object_List 中缺少与配置实例相同的 Device 对象。");
            }
            return (IReadOnlyList<BacnetObjectId>)objects;
        }, "Object_List", ct).ConfigureAwait(false);
    }

    public async Task<Dictionary<string, object?>> ReadSnapshotAsync(
        IReadOnlyList<BacnetSelectedObject> selectors,
        CancellationToken ct)
    {
        return await RunWithTimeoutAsync(async token =>
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var values = new Dictionary<string, object?>(selectors.Count, StringComparer.Ordinal);
            using var concurrency = new SemaphoreSlim(
                BacnetIpProtocolSupport.MaximumConcurrentReads,
                BacnetIpProtocolSupport.MaximumConcurrentReads);
            var reads = selectors.Select(async selector =>
            {
                await concurrency.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    var response = await ReadPropertyAsync(
                        selector.ObjectId,
                        BacnetPropertyIds.PROP_PRESENT_VALUE,
                        uint.MaxValue,
                        token).ConfigureAwait(false);
                    return new KeyValuePair<string, object?>(
                        selector.SourcePath,
                        NormalizePresentValue(response[0], selector.ObjectType));
                }
                finally
                {
                    concurrency.Release();
                }
            }).ToArray();
            foreach (var result in await Task.WhenAll(reads).ConfigureAwait(false))
                values.Add(result.Key, result.Value);
            return values;
        }, "Present_Value 快照", ct).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _client.Dispose();
        _transport.Dispose();
    }

    private async Task<IList<BacnetValue>> ReadPropertyAsync(
        BacnetObjectId objectId,
        BacnetPropertyIds propertyId,
        uint arrayIndex,
        CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var values = await _client.ReadPropertyAsync(
            _address,
            objectId,
            propertyId,
            arrayIndex: arrayIndex,
            cancellationToken: ct).ConfigureAwait(false);
        if (values.Count != 1)
        {
            throw new InvalidDataException(
                $"BACnet ReadProperty 返回了 {values.Count} 个值，期望标量属性只返回一个值。");
        }
        return values;
    }

    private async Task<T> RunWithTimeoutAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutSource.CancelAfter(_operationTimeout);
        try
        {
            return await operation(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"BACnet/IP {operationName} 超过 {_operationTimeout.TotalMilliseconds:0}ms 未完成。");
        }
    }

    private static bool TryConvertObjectCount(object? value, out uint count)
    {
        try
        {
            count = Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            count = 0;
            return false;
        }
    }

    internal static object NormalizePresentValue(BacnetValue value, string objectType)
    {
        var raw = value.Value ?? throw new InvalidDataException("BACnet Present_Value 返回空值。");
        if (objectType.StartsWith("binary-", StringComparison.Ordinal))
        {
            if (raw is bool boolean)
                return boolean;
            var binaryValue = Convert.ToInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
            return binaryValue switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException($"BACnet binary Present_Value 无效：{binaryValue}。")
            };
        }

        if (objectType.StartsWith("multi-state-", StringComparison.Ordinal))
        {
            var state = Convert.ToUInt32(raw, System.Globalization.CultureInfo.InvariantCulture);
            if (state == 0)
                throw new InvalidDataException("BACnet multi-state Present_Value 必须从 1 开始。");
            return state;
        }

        if (raw is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal))
            throw new InvalidDataException($"BACnet analog Present_Value 类型不受支持：{raw.GetType().Name}。");
        return raw;
    }
}

internal sealed class PinnedBacnetIpUdpProtocolTransport : BacnetIpUdpProtocolTransport
{
    private readonly IPAddress _localAddress;
    private readonly IPEndPoint _remoteEndpoint;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _socketGate = new();
    private Socket? _socket;
    private BVLC? _bvlc;
    private Task? _receiveTask;
    private bool _disposed;

    public PinnedBacnetIpUdpProtocolTransport(
        IPAddress localAddress,
        IPEndPoint remoteEndpoint,
        ILogger logger)
        : base(0, useExclusivePort: true, localEndpointIp: localAddress.ToString(), maxApdu: BacnetMaxAdpu.MAX_APDU1024)
    {
        _localAddress = localAddress;
        _remoteEndpoint = remoteEndpoint;
        _logger = logger;
    }

    public override void Start()
    {
        lock (_socketGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_socket is not null)
                return;

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                ExclusiveAddressUse = true
            };
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Broadcast, false);
            socket.Bind(new IPEndPoint(_localAddress, 0));
            _socket = socket;
            _bvlc = new BVLC(this);
            _receiveTask = ReceiveLoopAsync(_stopping.Token);
        }
    }

    public override BacnetAddress GetBroadcastAddress()
        => throw new InvalidOperationException("BACnet/IP broadcast discovery is disabled; use the configured unicast device address.");

    public override int Send(
        byte[] buffer,
        int offset,
        int dataLength,
        BacnetAddress address,
        bool waitForTransmission,
        int timeout)
    {
        if (address.type != BacnetAddressTypes.IP || address.net != 0 || address.adr is not { Length: 6 })
            throw new InvalidOperationException("BACnet/IP transport permits only local-network unicast addresses.");

        var targetAddress = new IPAddress(address.adr.AsSpan(0, 4));
        var targetPort = BinaryPrimitives.ReadUInt16BigEndian(address.adr.AsSpan(4, 2));
        var target = new IPEndPoint(targetAddress, targetPort);
        if (!target.Equals(_remoteEndpoint) || !IsUnicast(targetAddress))
            throw new InvalidOperationException("BACnet/IP transport refused a destination other than the pinned unicast device.");
        if (offset < HeaderLength || dataLength < 0 || buffer.Length < offset + dataLength)
            throw new ArgumentOutOfRangeException(nameof(offset), "BACnet/IP request buffer range is invalid.");

        var payloadLength = dataLength + HeaderLength;
        if (payloadLength > MaxBufferLength)
            throw new InvalidOperationException($"BACnet/IP request size {payloadLength} exceeds UDP payload limit {MaxBufferLength}.");

        var bvlc = _bvlc ?? throw new InvalidOperationException("BACnet/IP transport has not been started.");
        bvlc.Encode(
            buffer,
            offset - HeaderLength,
            BacnetBvlcFunctions.BVLC_ORIGINAL_UNICAST_NPDU,
            payloadLength);
        var socket = _socket ?? throw new ObjectDisposedException(nameof(PinnedBacnetIpUdpProtocolTransport));
        return socket.SendTo(
            buffer,
            offset - HeaderLength,
            payloadLength,
            SocketFlags.None,
            target);
    }

    public override void Dispose()
    {
        lock (_socketGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _stopping.Cancel();
            _socket?.Dispose();
            _socket = null;
            _stopping.Dispose();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var socket = _socket;
        var bvlc = _bvlc;
        if (socket is null || bvlc is null)
            return;

        try
        {
            var buffer = new byte[MaxBufferLength];
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var packet = await socket.ReceiveFromAsync(
                        buffer.AsMemory(),
                        SocketFlags.None,
                        new IPEndPoint(IPAddress.Any, 0),
                        ct).ConfigureAwait(false);
                    if (packet.RemoteEndPoint is not IPEndPoint remoteEndpoint ||
                        !remoteEndpoint.Equals(_remoteEndpoint) || packet.ReceivedBytes < HeaderLength)
                        continue;

                    var headerLength = bvlc.Decode(
                        buffer,
                        0,
                        out var function,
                        out _,
                        remoteEndpoint);
                    if (headerLength <= 0 || function != BacnetBvlcFunctions.BVLC_ORIGINAL_UNICAST_NPDU ||
                        packet.ReceivedBytes <= headerLength)
                    {
                        continue;
                    }

                    var remoteAddress = new BacnetAddress(
                        BacnetAddressTypes.IP,
                        $"{remoteEndpoint.Address}:{remoteEndpoint.Port}");
                    InvokeMessageRecieved(buffer, headerLength, packet.ReceivedBytes - headerLength, remoteAddress);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException) when (ct.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _logger.LogDebug(exception, "Ignored an invalid BACnet/IP unicast response from the configured device.");
                }
            }
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "BACnet/IP pinned unicast receive loop stopped.");
        }
    }

    private static bool IsUnicast(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast))
            return false;
        var firstOctet = address.GetAddressBytes()[0];
        return firstOctet is < 224 or > 239;
    }
}
