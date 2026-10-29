// 共享寄存器协议的物理 TCP 会话，同时隔离任务快照与版本不兼容配置。
using System.Net.Sockets;
using Ingot.Contracts.Acquisition;
using NModbus;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class RegisterAcquisitionSessionPool : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<EndpointKey, SharedSession> _sessions = [];
    private readonly Dictionary<SourceVersionKey, SourceVersionBinding> _sourceVersions = [];
    private bool _disposed;

    public static string CreateSourceVersionFingerprint(
        ModbusTcpConnection connection,
        IngestionTask task)
        => JoinFingerprint(
            connection.Host,
            connection.Port,
            connection.AddressBase,
            connection.PollIntervalMs,
            connection.MaxMergeGap,
            task.EdgeId,
            task.Source);

    public static string CreateSourceVersionFingerprint(
        McA1EConnection connection,
        IngestionTask task)
        => JoinFingerprint(
            connection.Host,
            connection.Port,
            connection.DataCode,
            connection.PcNumber,
            connection.MonitoringTimer,
            connection.WordOrderLayout,
            connection.PollIntervalMs,
            connection.MaxMergeGap,
            task.EdgeId,
            task.Source);

    public static string CreateSourceVersionFingerprint(
        Mc3EConnection connection,
        IngestionTask task)
        => JoinFingerprint(
            connection.Host,
            connection.Port,
            connection.Family,
            connection.NetworkNumber,
            connection.PcNumber,
            connection.ModuleIoNumber,
            connection.StationNumber,
            connection.MonitoringTimer,
            connection.PollIntervalMs,
            connection.MaxMergeGap,
            task.EdgeId,
            task.Source);

    public static string CreateSourceVersionFingerprint(
        OmronFinsTcpConnection connection,
        IngestionTask task)
        => JoinFingerprint(
            connection.Host,
            connection.Port,
            connection.PollIntervalMs,
            task.EdgeId,
            task.Source);

    public async ValueTask<Lease> AcquireAsync(
        string protocol,
        string host,
        int port,
        string codec,
        string? dataSourceId,
        int? dataSourceVersion,
        string sourceVersionFingerprint,
        Func<CancellationToken, Task<TcpClient>> connectAsync,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connectAsync);
        ct.ThrowIfCancellationRequested();
        var endpoint = new EndpointKey(NormalizeHost(host), port);
        var normalizedProtocol = protocol?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedCodec = codec?.Trim().ToLowerInvariant() ?? string.Empty;
        ValidateRequest(endpoint, normalizedProtocol, normalizedCodec);
        var sourceKey = CreateSourceVersionKey(dataSourceId, dataSourceVersion);
        var sourceFingerprint = sourceKey is null
            ? null
            : JoinFingerprint(
                endpoint.Host,
                endpoint.Port,
                normalizedProtocol,
                normalizedCodec,
                sourceVersionFingerprint);

        while (true)
        {
            Task? closingTask = null;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_sessions.TryGetValue(endpoint, out var existing))
                {
                    if (existing.IsClosing)
                    {
                        closingTask = existing.Closed.Task;
                    }
                    else
                    {
                        EnsureCompatible(existing, normalizedProtocol, normalizedCodec, endpoint);
                        RegisterSourceVersion(sourceKey, sourceFingerprint);
                        existing.AddReference(sourceKey);
                        return new Lease(this, existing, sourceKey, connectAsync);
                    }
                }
                else
                {
                    RegisterSourceVersion(sourceKey, sourceFingerprint);
                    var session = new SharedSession(
                        endpoint,
                        normalizedProtocol,
                        normalizedCodec);
                    session.AddReference(sourceKey);
                    _sessions.Add(endpoint, session);
                    return new Lease(this, session, sourceKey, connectAsync);
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
        lock (_sync)
            _sourceVersions.Clear();
    }

    private async ValueTask ReleaseAsync(SharedSession session, SourceVersionKey? sourceKey)
    {
        var close = false;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(session.Endpoint, out var current) || !ReferenceEquals(current, session))
                return;
            if (session.ReferenceCount <= 0) return;

            if (session.ReferenceCount == 1)
            {
                session.RemoveReference(null);
                session.MarkClosing();
                close = true;
            }
            else
            {
                session.RemoveReference(sourceKey);
                ReleaseSourceVersion(sourceKey);
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
            if (_sessions.TryGetValue(session.Endpoint, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(session.Endpoint);
                foreach (var sourceKey in session.SourceKeys)
                    ReleaseSourceVersion(sourceKey);
            }
            session.Closed.TrySetResult(true);
        }
    }

    private void RegisterSourceVersion(SourceVersionKey? sourceKey, string? fingerprint)
    {
        if (sourceKey is null || fingerprint is null) return;
        if (_sourceVersions.TryGetValue(sourceKey.Value, out var existing))
        {
            if (!string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"数据源 {sourceKey.Value.DataSourceId} v{sourceKey.Value.Version} 的接入参数与仍在运行的版本不一致。" +
                    "请为配置变更创建新版本。");
            }

            existing.ReferenceCount++;
            return;
        }

        _sourceVersions.Add(sourceKey.Value, new SourceVersionBinding(fingerprint));
    }

    private void ReleaseSourceVersion(SourceVersionKey? sourceKey)
    {
        if (sourceKey is null || !_sourceVersions.TryGetValue(sourceKey.Value, out var existing)) return;
        existing.ReferenceCount--;
        if (existing.ReferenceCount <= 0)
            _sourceVersions.Remove(sourceKey.Value);
    }

    private static void EnsureCompatible(
        SharedSession existing,
        string protocol,
        string codec,
        EndpointKey endpoint)
    {
        if (!string.Equals(existing.Protocol, protocol, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"采集端点 {endpoint.Host}:{endpoint.Port} 已由 {existing.Protocol} 会话占用，" +
                $"不能同时使用 {protocol}。请统一该端点的协议配置。");
        }
        if (!string.Equals(existing.Codec, codec, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"采集端点 {endpoint.Host}:{endpoint.Port} 的通信数据码不兼容：" +
                $"当前为 {existing.Codec}，请求为 {codec}。请统一 PLC 端口通信数据码并创建一致的数据源版本。");
        }
    }

    private static void ValidateRequest(EndpointKey endpoint, string protocol, string codec)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Host))
            throw new ArgumentException("寄存器采集端点缺少主机名。", nameof(endpoint));
        if (endpoint.Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(endpoint), "寄存器采集端口必须在 1-65535 之间。");
        if (protocol is not (AcquisitionProtocols.ModbusTcp or AcquisitionProtocols.MelsecA1E or
            AcquisitionProtocols.MelsecMc3E or AcquisitionProtocols.OmronFinsTcp))
            throw new ArgumentException($"寄存器会话池不支持协议 {protocol}。", nameof(protocol));
        if (protocol == AcquisitionProtocols.MelsecA1E && codec is not ("ascii" or "binary"))
            throw new ArgumentException("MELSEC 1E 通信数据码必须是 ascii 或 binary。", nameof(codec));
        if (protocol == AcquisitionProtocols.MelsecMc3E && codec != "binary")
            throw new ArgumentException("MELSEC MC 3E 当前仅支持 binary 通信数据码。", nameof(codec));
        if ((protocol is AcquisitionProtocols.ModbusTcp or AcquisitionProtocols.OmronFinsTcp) && codec.Length > 0)
            throw new ArgumentException("当前 TCP 协议会话不接受通信数据码。", nameof(codec));
    }

    private static SourceVersionKey? CreateSourceVersionKey(string? dataSourceId, int? dataSourceVersion)
        => !string.IsNullOrWhiteSpace(dataSourceId) && dataSourceVersion is > 0
            ? new SourceVersionKey(dataSourceId.Trim(), dataSourceVersion.Value)
            : null;

    private static string NormalizeHost(string? host)
    {
        var value = host?.Trim().TrimEnd('.') ?? string.Empty;
        return System.Net.IPAddress.TryParse(value, out var address)
            ? address.ToString()
            : value.ToLowerInvariant();
    }

    private static string JoinFingerprint(params object?[] values)
        => string.Join("\u001f", values.Select(static value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));

    internal readonly record struct EndpointKey(string Host, int Port);
    internal readonly record struct SourceVersionKey(string DataSourceId, int Version);

    private sealed class SourceVersionBinding(string fingerprint)
    {
        public string Fingerprint { get; } = fingerprint;
        public int ReferenceCount { get; set; } = 1;
    }

    public sealed class Lease : IAsyncDisposable
    {
        private readonly RegisterAcquisitionSessionPool _owner;
        private readonly SharedSession _session;
        private readonly SourceVersionKey? _sourceKey;
        private readonly Func<CancellationToken, Task<TcpClient>> _connectAsync;
        private int _disposed;

        internal Lease(
            RegisterAcquisitionSessionPool owner,
            SharedSession session,
            SourceVersionKey? sourceKey,
            Func<CancellationToken, Task<TcpClient>> connectAsync)
        {
            _owner = owner;
            _session = session;
            _sourceKey = sourceKey;
            _connectAsync = connectAsync;
        }

        public Task<T> ExecuteMelsecAsync<T>(
            int timeoutMs,
            Func<NetworkStream, CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_session.Protocol != AcquisitionProtocols.MelsecA1E)
                throw new InvalidOperationException("当前共享会话不是 MELSEC 1E 协议。");
            ArgumentNullException.ThrowIfNull(operation);
            return _session.ExecuteAsync(
                timeoutMs,
                _connectAsync,
                (session, token) => operation(session.Stream!, token),
                ThrowIfDisposed,
                ct);
        }

        public Task<T> ExecuteMelsecMc3EAsync<T>(
            int timeoutMs,
            Func<NetworkStream, CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_session.Protocol != AcquisitionProtocols.MelsecMc3E)
                throw new InvalidOperationException("当前共享会话不是 MELSEC MC 3E 协议。");
            ArgumentNullException.ThrowIfNull(operation);
            return _session.ExecuteAsync(
                timeoutMs,
                _connectAsync,
                (session, token) => operation(session.Stream!, token),
                ThrowIfDisposed,
                ct);
        }

        internal Task<T> ExecuteOmronFinsTcpAsync<T>(
            int timeoutMs,
            Func<OmronFinsTcpSession, CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_session.Protocol != AcquisitionProtocols.OmronFinsTcp)
                throw new InvalidOperationException("当前共享会话不是 Omron FINS/TCP 协议。");
            ArgumentNullException.ThrowIfNull(operation);
            return _session.ExecuteAsync(
                timeoutMs,
                _connectAsync,
                async (session, token) =>
                {
                    session.OmronFinsTcpSession ??= await OmronFinsTcpSession.CreateAsync(
                        session.Stream!, token).ConfigureAwait(false);
                    return await operation(session.OmronFinsTcpSession, token).ConfigureAwait(false);
                },
                ThrowIfDisposed,
                ct);
        }

        public Task<T> ExecuteModbusAsync<T>(
            int timeoutMs,
            Func<IModbusMaster, CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            if (_session.Protocol != AcquisitionProtocols.ModbusTcp)
                throw new InvalidOperationException("当前共享会话不是 Modbus TCP 协议。");
            ArgumentNullException.ThrowIfNull(operation);
            return _session.ExecuteAsync(
                timeoutMs,
                _connectAsync,
                (session, token) => operation(session.Master!, token),
                ThrowIfDisposed,
                ct);
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
                throw new ObjectDisposedException(nameof(Lease));
        }

        public ValueTask DisposeAsync()
            => Interlocked.Exchange(ref _disposed, 1) == 0
                ? _owner.ReleaseAsync(_session, _sourceKey)
                : ValueTask.CompletedTask;
    }

    internal sealed class SharedSession
    {
        private readonly SemaphoreSlim _ioGate = new(1, 1);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Dictionary<SourceVersionKey, int> _sourceReferences = [];
        private Task? _closeTask;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private IModbusMaster? _master;
        private bool _closed;

        public SharedSession(
            EndpointKey endpoint,
            string protocol,
            string codec)
        {
            Endpoint = endpoint;
            Protocol = protocol;
            Codec = codec;
        }

        public EndpointKey Endpoint { get; }
        public string Protocol { get; }
        public string Codec { get; }
        public int ReferenceCount { get; private set; }
        private int _closing;
        public bool IsClosing => Volatile.Read(ref _closing) != 0;
        public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IEnumerable<SourceVersionKey> SourceKeys => _sourceReferences.Keys.ToArray();
        public NetworkStream? Stream => _stream;
        public IModbusMaster? Master => _master;
        public OmronFinsTcpSession? OmronFinsTcpSession { get; set; }

        public void AddReference(SourceVersionKey? sourceKey)
        {
            ReferenceCount++;
            if (sourceKey is not null)
            {
                _sourceReferences.TryGetValue(sourceKey.Value, out var count);
                _sourceReferences[sourceKey.Value] = count + 1;
            }
        }

        public void RemoveReference(SourceVersionKey? sourceKey)
        {
            ReferenceCount--;
            if (sourceKey is not null && _sourceReferences.TryGetValue(sourceKey.Value, out var count))
            {
                if (count <= 1)
                    _sourceReferences.Remove(sourceKey.Value);
                else
                    _sourceReferences[sourceKey.Value] = count - 1;
            }
        }

        public void MarkClosing() => Interlocked.Exchange(ref _closing, 1);

        public async Task<T> ExecuteAsync<T>(
            int timeoutMs,
            Func<CancellationToken, Task<TcpClient>> connectAsync,
            Func<SharedSession, CancellationToken, Task<T>> operation,
            Action validateLease,
            CancellationToken ct)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            await _ioGate.WaitAsync(wait.Token).ConfigureAwait(false);
            try
            {
                if (_closed || IsClosing)
                    throw new ObjectDisposedException(nameof(RegisterAcquisitionSessionPool));
                validateLease();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
                timeout.CancelAfter(Math.Max(1000, timeoutMs));
                try
                {
                    await EnsureConnectedAsync(connectAsync, timeout.Token).ConfigureAwait(false);
                    return await operation(this, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_shutdown.IsCancellationRequested)
                {
                    ResetConnection();
                    throw new TimeoutException($"寄存器采集端点 {Endpoint.Host}:{Endpoint.Port} 的完整读取超过 {Math.Max(1000, timeoutMs)}ms。");
                }
                catch
                {
                    ResetConnection();
                    throw;
                }
            }
            finally
            {
                _ioGate.Release();
            }
        }

        public Task CloseAsync()
        {
            lock (this)
                return _closeTask ??= CloseCoreAsync();
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
            }
        }

        private async Task EnsureConnectedAsync(
            Func<CancellationToken, Task<TcpClient>> connectAsync,
            CancellationToken ct)
        {
            if (_client is not null) return;
            var client = await connectAsync(ct).ConfigureAwait(false);
            try
            {
                var stream = client.GetStream();
                var master = Protocol == AcquisitionProtocols.ModbusTcp
                    ? new ModbusFactory().CreateMaster(client)
                    : null;
                _client = client;
                _stream = stream;
                _master = master;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }

        private void ResetConnection()
        {
            var master = _master;
            var stream = _stream;
            var client = _client;
            _master = null;
            _stream = null;
            _client = null;
            OmronFinsTcpSession = null;
            try { master?.Dispose(); }
            catch { /* 清理失败不能保留已损坏连接。 */ }
            try { stream?.Dispose(); }
            catch { /* 清理失败不能保留已损坏连接。 */ }
            try { client?.Dispose(); }
            catch { /* 清理失败不能保留已损坏连接。 */ }
        }
    }
}
