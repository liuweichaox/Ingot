// Shares one bounded, serialized BACnet MS/TP master per explicitly allowed physical port.
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.BACnet;
using Ingot.Contracts.Acquisition;
using Microsoft.Extensions.Logging;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class BacnetMstpSessionPool(
    AcquisitionSerialPortPolicy portPolicy,
    AcquisitionSerialSessionCoordinator serialCoordinator,
    ILogger<BacnetMstpSessionPool>? logger = null) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SharedSession> _sessions = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool _disposed;

    public async ValueTask<Lease> AcquireAsync(BacnetMstpConnection connection, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var portName = portPolicy.RequireAllowedPort(connection.PortName);
        var fingerprint = CreateBusFingerprint(connection);
        while (true)
        {
            Task? closingTask = null;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_sessions.TryGetValue(portName, out var existing))
                {
                    if (existing.IsClosing)
                    {
                        closingTask = existing.Closed.Task;
                    }
                    else
                    {
                        EnsureCompatible(existing, fingerprint, portName);
                        existing.AddReference();
                        return new Lease(this, existing);
                    }
                }
                else
                {
                    var reservation = serialCoordinator.Acquire(portName, "bacnet-mstp", fingerprint);
                    try
                    {
                        var session = new SharedSession(portName, connection, fingerprint, reservation, logger);
                        session.AddReference();
                        _sessions.Add(portName, session);
                        return new Lease(this, session);
                    }
                    catch
                    {
                        reservation.Dispose();
                        throw;
                    }
                }
            }

            await closingTask!.WaitAsync(ct).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        SharedSession[] sessions;
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            sessions = _sessions.Values.ToArray();
            foreach (var session in sessions)
                session.MarkClosing();
        }

        foreach (var session in sessions)
            await CloseAndRemoveAsync(session).ConfigureAwait(false);
    }

    private async ValueTask ReleaseAsync(SharedSession session)
    {
        var close = false;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(session.PortName, out var current) || !ReferenceEquals(current, session))
                return;
            if (session.ReferenceCount <= 0) return;
            session.RemoveReference();
            if (session.ReferenceCount == 0)
            {
                session.MarkClosing();
                close = true;
            }
        }

        if (close)
            await CloseAndRemoveAsync(session).ConfigureAwait(false);
    }

    private async Task CloseAndRemoveAsync(SharedSession session)
    {
        await session.CloseAsync().ConfigureAwait(false);
        lock (_sync)
        {
            if (_sessions.TryGetValue(session.PortName, out var current) && ReferenceEquals(current, session))
                _sessions.Remove(session.PortName);
            session.Closed.TrySetResult(true);
        }
    }

    private static string CreateBusFingerprint(BacnetMstpConnection connection)
        => string.Join("\u001f", connection.BaudRate, connection.LocalStationAddress,
            connection.MaxMaster, connection.MaxInfoFrames, "8", "none", "one");

    private static void EnsureCompatible(SharedSession existing, string fingerprint, string portName)
    {
        if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"BACnet MS/TP 串口 {portName} 已有活动总线会话；波特率、本地站地址、Max Master 和 Max Info Frames 必须一致。不同 Device Instance 可以共享同一总线。");
        }
    }

    public sealed class Lease : IAsyncDisposable
    {
        private readonly BacnetMstpSessionPool _owner;
        private readonly SharedSession _session;
        private int _disposed;

        internal Lease(BacnetMstpSessionPool owner, SharedSession session)
        {
            _owner = owner;
            _session = session;
        }

        internal Task<IReadOnlyList<BacnetObjectId>> ReadObjectListAsync(
            uint deviceInstance,
            int timeoutMs,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return _session.ReadObjectListAsync(deviceInstance, timeoutMs, ThrowIfDisposed, ct);
        }

        internal Task<Dictionary<string, object?>> ReadSnapshotAsync(
            uint deviceInstance,
            IReadOnlyList<BacnetSelectedObject> selectors,
            int timeoutMs,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            return _session.ReadSnapshotAsync(deviceInstance, selectors, timeoutMs, ThrowIfDisposed, ct);
        }

        public ValueTask DisposeAsync()
            => Interlocked.Exchange(ref _disposed, 1) == 0
                ? _owner.ReleaseAsync(_session)
                : ValueTask.CompletedTask;

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(Lease));
        }
    }

    internal sealed class SharedSession
    {
        private readonly SemaphoreSlim _ioGate = new(1, 1);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly BacnetMstpConnection _connection;
        private readonly IDisposable _serialReservation;
        private readonly ILogger? _logger;
        private readonly Dictionary<uint, BacnetAddress> _addresses = [];
        private readonly HashSet<uint> _verifiedDevices = [];
        private BacnetClient? _client;
        private BacnetMstpProtocolTransport? _transport;
        private Task? _closeTask;
        private int _closing;
        private bool _closed;

        public SharedSession(
            string portName,
            BacnetMstpConnection connection,
            string fingerprint,
            IDisposable serialReservation,
            ILogger? logger)
        {
            PortName = portName;
            _connection = connection;
            Fingerprint = fingerprint;
            _serialReservation = serialReservation;
            _logger = logger;
        }

        public string PortName { get; }
        public string Fingerprint { get; }
        public int ReferenceCount { get; private set; }
        public bool IsClosing => Volatile.Read(ref _closing) != 0;
        public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void AddReference() => ReferenceCount++;
        public void RemoveReference() => ReferenceCount--;
        public void MarkClosing() => Interlocked.Exchange(ref _closing, 1);

        public Task<IReadOnlyList<BacnetObjectId>> ReadObjectListAsync(
            uint deviceInstance,
            int timeoutMs,
            Action validateLease,
            CancellationToken ct)
            => ExecuteAsync(300_000, async token =>
            {
                var requestTimeoutMs = Math.Clamp(timeoutMs, 500, 30_000);
                var address = await ResolveAddressAsync(deviceInstance, timeoutMs, token).ConfigureAwait(false);
                await VerifyDeviceIdentifierAsync(address, deviceInstance, requestTimeoutMs, token).ConfigureAwait(false);
                var countValues = await ReadPropertyAsync(address,
                    new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceInstance),
                    BacnetPropertyIds.PROP_OBJECT_LIST, 0, requestTimeoutMs, token).ConfigureAwait(false);
                if (!TryConvertObjectCount(countValues[0].Value, out var count))
                    throw new InvalidDataException("BACnet Device Object_List[0] 没有返回有效的对象总数。");
                if (count > BacnetIpProtocolSupport.MaximumObjectListEntries)
                    throw new InvalidOperationException(
                        $"BACnet MS/TP Device Object_List 包含 {count} 个对象，超过单设备上限 {BacnetIpProtocolSupport.MaximumObjectListEntries}；请按现场设备边界拆分采集。");

                var objects = new BacnetObjectId[checked((int)count)];
                for (var offset = 0; offset < objects.Length; offset++)
                {
                    token.ThrowIfCancellationRequested();
                    var index = checked((uint)offset + 1);
                    var values = await ReadPropertyAsync(address,
                        new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceInstance),
                        BacnetPropertyIds.PROP_OBJECT_LIST, index, requestTimeoutMs, token).ConfigureAwait(false);
                    if (values[0].Value is not BacnetObjectId objectId)
                        throw new InvalidDataException($"BACnet Device Object_List[{index}] 没有返回有效的对象标识符。");
                    objects[offset] = objectId;
                }

                if (!objects.Any(item => item.Type == BacnetObjectTypes.OBJECT_DEVICE && item.Instance == deviceInstance))
                    throw new InvalidDataException("BACnet Device Object_List 中缺少与配置实例相同的 Device 对象。");
                return (IReadOnlyList<BacnetObjectId>)objects;
            }, validateLease, ct);

        public Task<Dictionary<string, object?>> ReadSnapshotAsync(
            uint deviceInstance,
            IReadOnlyList<BacnetSelectedObject> selectors,
            int timeoutMs,
            Action validateLease,
            CancellationToken ct)
            => ExecuteAsync(timeoutMs, async token =>
            {
                var address = await ResolveAddressAsync(deviceInstance, timeoutMs, token).ConfigureAwait(false);
                await VerifyDeviceIdentifierAsync(address, deviceInstance, timeoutMs, token).ConfigureAwait(false);
                var values = new Dictionary<string, object?>(selectors.Count, StringComparer.Ordinal);
                foreach (var selector in selectors)
                {
                    token.ThrowIfCancellationRequested();
                    var response = await ReadPropertyAsync(address, selector.ObjectId,
                        BacnetPropertyIds.PROP_PRESENT_VALUE, uint.MaxValue, timeoutMs, token).ConfigureAwait(false);
                    values.Add(selector.SourcePath,
                        BacnetIpClientSession.NormalizePresentValue(response[0], selector.ObjectType));
                }
                return values;
            }, validateLease, ct);

        public Task CloseAsync()
        {
            lock (this)
                return _closeTask ??= CloseCoreAsync();
        }

        private async Task<T> ExecuteAsync<T>(
            int timeoutMs,
            Func<CancellationToken, Task<T>> operation,
            Action validateLease,
            CancellationToken ct)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            await _ioGate.WaitAsync(wait.Token).ConfigureAwait(false);
            try
            {
                if (_closed || IsClosing)
                    throw new ObjectDisposedException(nameof(BacnetMstpSessionPool));
                validateLease();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
                timeout.CancelAfter(Math.Clamp(timeoutMs, 500, 300_000));
                try
                {
                    EnsureConnected();
                    return await operation(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_shutdown.IsCancellationRequested)
                {
                    throw new TimeoutException($"BACnet MS/TP 串口 {PortName} 读取超过 {Math.Clamp(timeoutMs, 500, 300_000)}ms。");
                }
            }
            finally
            {
                _ioGate.Release();
            }
        }

        private void EnsureConnected()
        {
            if (_client is not null) return;
            var serialTransport = SerialTransport.Mstp(
                PortName,
                _connection.BaudRate,
                checked((short)_connection.LocalStationAddress!.Value),
                _connection.MaxMaster,
                _connection.MaxInfoFrames);
            // The shared client uses a stable upper bound; each task applies its own timeout token per operation.
            var client = new BacnetClient(serialTransport, 30_000, retries: 0);
            try
            {
                client.Start();
                _transport = serialTransport;
                _client = client;
            }
            catch
            {
                client.Dispose();
                serialTransport.Dispose();
                throw;
            }
        }

        private async Task<BacnetAddress> ResolveAddressAsync(
            uint deviceInstance,
            int timeoutMs,
            CancellationToken ct)
        {
            if (_addresses.TryGetValue(deviceInstance, out var cached))
                return cached;

            var client = _client ?? throw new InvalidOperationException("BACnet MS/TP client 尚未启动。");
            var candidates = new ConcurrentDictionary<string, BacnetAddress>(StringComparer.Ordinal);
            BacnetClient.IamHandler handler = (_, address, instance, _, _, _) =>
            {
                if (instance != deviceInstance) return;
                candidates.TryAdd(address.ToString(), address);
            };

            client.OnIam += handler;
            try
            {
                client.WhoIs(checked((int)deviceInstance), checked((int)deviceInstance), receiver: null, source: null);
                // Collect the entire bounded I-Am window so token-delayed duplicate instances are not missed.
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 2_000, 10_000)), ct)
                    .ConfigureAwait(false);
            }
            finally
            {
                client.OnIam -= handler;
            }

            if (candidates.Count == 0)
                throw new TimeoutException(
                    $"BACnet MS/TP 在限定响应窗口内未发现配置的 Device Instance {deviceInstance}。请检查波特率、MS/TP 站地址和 RS-485 布线。");
            if (candidates.Count > 1)
                throw new InvalidDataException(
                    $"BACnet MS/TP 总线上有多个设备声明 Device Instance {deviceInstance}；请先修复 BACnet 设备实例冲突。");

            var address = candidates.Values.Single();
            _addresses[deviceInstance] = address;
            return address;
        }

        private async Task VerifyDeviceIdentifierAsync(
            BacnetAddress address,
            uint deviceInstance,
            int timeoutMs,
            CancellationToken ct)
        {
            if (_verifiedDevices.Contains(deviceInstance)) return;
            var values = await ReadPropertyAsync(address,
                new BacnetObjectId(BacnetObjectTypes.OBJECT_DEVICE, deviceInstance),
                BacnetPropertyIds.PROP_OBJECT_IDENTIFIER, uint.MaxValue, timeoutMs, ct).ConfigureAwait(false);
            if (values[0].Value is not BacnetObjectId identifier ||
                identifier.Type != BacnetObjectTypes.OBJECT_DEVICE || identifier.Instance != deviceInstance)
            {
                throw new InvalidDataException(
                    $"BACnet MS/TP Device Object Identifier 与配置的 Device Instance {deviceInstance} 不一致。");
            }
            _verifiedDevices.Add(deviceInstance);
        }

        private async Task<IList<BacnetValue>> ReadPropertyAsync(
            BacnetAddress address,
            BacnetObjectId objectId,
            BacnetPropertyIds propertyId,
            uint arrayIndex,
            int timeoutMs,
            CancellationToken ct)
        {
            var client = _client ?? throw new InvalidOperationException("BACnet MS/TP client 尚未启动。");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Math.Clamp(timeoutMs, 500, 30_000));
            IList<BacnetValue> values;
            try
            {
                values = await client.ReadPropertyAsync(address, objectId, propertyId,
                    arrayIndex: arrayIndex, cancellationToken: timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"BACnet MS/TP 读取 {objectId}/{propertyId} 超过 {Math.Clamp(timeoutMs, 500, 30_000)}ms。");
            }
            if (values.Count != 1)
                throw new InvalidDataException(
                    $"BACnet MS/TP ReadProperty 返回了 {values.Count} 个值，期望标量属性只返回一个值。");
            return values;
        }

        private async Task CloseCoreAsync()
        {
            _shutdown.Cancel();
            await _ioGate.WaitAsync().ConfigureAwait(false);
            try
            {
                _closed = true;
                ResetConnection();
            }
            finally
            {
                _ioGate.Release();
                _serialReservation.Dispose();
            }
        }

        private void ResetConnection()
        {
            try { _client?.Dispose(); }
            catch (Exception exception) { _logger?.LogDebug(exception, "释放 BACnet MS/TP client 失败：{Port}", PortName); }
            try { _transport?.Dispose(); }
            catch (Exception exception) { _logger?.LogDebug(exception, "释放 BACnet MS/TP transport 失败：{Port}", PortName); }
            _client = null;
            _transport = null;
            _addresses.Clear();
            _verifiedDevices.Clear();
        }

        private static bool TryConvertObjectCount(object? value, out uint count)
        {
            try
            {
                count = Convert.ToUInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
                count = 0;
                return false;
            }
        }
    }
}
