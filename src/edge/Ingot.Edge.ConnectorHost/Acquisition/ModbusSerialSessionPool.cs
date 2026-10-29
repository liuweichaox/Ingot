// 为同一物理串口提供受配置约束、串行访问的共享 Modbus RTU/ASCII 会话。
using System.IO.Ports;
using Ingot.Contracts.Acquisition;
using Microsoft.Extensions.Logging;
using NModbus;
using NModbus.IO;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class ModbusSerialSessionPool(
    AcquisitionSerialPortPolicy portPolicy,
    AcquisitionSerialSessionCoordinator serialCoordinator,
    ILogger<ModbusSerialSessionPool>? logger = null) : IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, SharedSession> _sessions = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<SourceVersionKey, SourceVersionBinding> _sourceVersions = [];
    private bool _disposed;

    public static string CreateSourceVersionFingerprint(ModbusRtuConnection connection, IngestionTask task)
        => string.Join("\u001f", new object?[]
        {
            "rtu",
            connection.BaudRate,
            connection.DataBits,
            connection.Parity,
            connection.StopBits,
            connection.UnitId,
            connection.AddressBase,
            connection.PollIntervalMs,
            connection.MaxMergeGap,
            task.EdgeId,
            task.Source
        }.Select(static value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));

    public static string CreateSourceVersionFingerprint(ModbusAsciiConnection connection, IngestionTask task)
        => string.Join("\u001f", new object?[]
        {
            "ascii",
            connection.BaudRate,
            connection.DataBits,
            connection.Parity,
            connection.StopBits,
            connection.UnitId,
            connection.AddressBase,
            connection.PollIntervalMs,
            connection.MaxMergeGap,
            task.EdgeId,
            task.Source
        }.Select(static value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty));

    public ValueTask<Lease> AcquireAsync(
        ModbusRtuConnection connection,
        string? dataSourceId,
        int? dataSourceVersion,
        string sourceVersionFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return AcquireAsync(
            connection.PortName,
            SerialSettings.From(connection),
            dataSourceId,
            dataSourceVersion,
            sourceVersionFingerprint,
            ct);
    }

    public ValueTask<Lease> AcquireAsync(
        ModbusAsciiConnection connection,
        string? dataSourceId,
        int? dataSourceVersion,
        string sourceVersionFingerprint,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return AcquireAsync(
            connection.PortName,
            SerialSettings.From(connection),
            dataSourceId,
            dataSourceVersion,
            sourceVersionFingerprint,
            ct);
    }

    private async ValueTask<Lease> AcquireAsync(
        string requestedPortName,
        SerialSettings serial,
        string? dataSourceId,
        int? dataSourceVersion,
        string sourceVersionFingerprint,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var portName = portPolicy.RequireAllowedPort(requestedPortName);
        var sourceKey = CreateSourceVersionKey(dataSourceId, dataSourceVersion);
        var sourceFingerprint = sourceKey is null
            ? null
            : string.Join("\u001f", portName, serial.Fingerprint, sourceVersionFingerprint);

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
                        EnsureCompatible(existing, serial, portName);
                        RegisterSourceVersion(sourceKey, sourceFingerprint);
                        existing.AddReference(sourceKey);
                        return new Lease(this, existing, sourceKey);
                    }
                }
                else
                {
                    RegisterSourceVersion(sourceKey, sourceFingerprint);
                    IDisposable? serialReservation = null;
                    try
                    {
                        serialReservation = serialCoordinator.Acquire(portName, "modbus", serial.Fingerprint);
                        var session = new SharedSession(portName, serial, logger, serialReservation);
                        session.AddReference(sourceKey);
                        _sessions.Add(portName, session);
                        return new Lease(this, session, sourceKey);
                    }
                    catch
                    {
                        serialReservation?.Dispose();
                        ReleaseSourceVersion(sourceKey);
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
        lock (_sync)
            _sourceVersions.Clear();
    }

    private async ValueTask ReleaseAsync(SharedSession session, SourceVersionKey? sourceKey)
    {
        var close = false;
        lock (_sync)
        {
            if (!_sessions.TryGetValue(session.PortName, out var current) || !ReferenceEquals(current, session))
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
            if (_sessions.TryGetValue(session.PortName, out var current) && ReferenceEquals(current, session))
            {
                _sessions.Remove(session.PortName);
                foreach (var (sourceKey, referenceCount) in session.SourceReferences)
                {
                    for (var index = 0; index < referenceCount; index++)
                        ReleaseSourceVersion(sourceKey);
                }
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
                    $"数据源 {sourceKey.Value.DataSourceId} v{sourceKey.Value.Version} 的串口参数与仍在运行的版本不一致。请为配置变更创建新版本。");
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

    private static void EnsureCompatible(SharedSession existing, SerialSettings requested, string portName)
    {
        if (!string.Equals(existing.Settings.Fingerprint, requested.Fingerprint, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"串口 {portName} 已被不同串口参数或 Modbus 帧格式占用；同一 RS-485 总线必须使用相同 RTU/ASCII 格式、波特率、数据位、校验位和停止位。Unit ID 可以不同。");
        }
    }

    private static SourceVersionKey? CreateSourceVersionKey(string? id, int? version)
        => !string.IsNullOrWhiteSpace(id) && version is > 0
            ? new SourceVersionKey(id.Trim(), version.Value)
            : null;

    internal readonly record struct SourceVersionKey(string DataSourceId, int Version);

    private sealed class SourceVersionBinding(string fingerprint)
    {
        public string Fingerprint { get; } = fingerprint;
        public int ReferenceCount { get; set; } = 1;
    }

    internal sealed record SerialSettings(string FrameMode, int BaudRate, int DataBits, Parity Parity, StopBits StopBits)
    {
        public string Fingerprint => $"{FrameMode}\u001f{BaudRate}\u001f{DataBits}\u001f{Parity}\u001f{StopBits}";

        public static SerialSettings From(ModbusRtuConnection connection)
        {
            var parity = ParseParity(connection.Parity, "Modbus RTU");
            var stopBits = ParseStopBits(connection.StopBits, "Modbus RTU");
            if (connection.BaudRate is < 300 or > 4_000_000 || connection.DataBits != 8)
                throw new InvalidOperationException("Modbus RTU 波特率或数据位配置无效。");
            return new SerialSettings("rtu", connection.BaudRate, connection.DataBits, parity, stopBits);
        }

        public static SerialSettings From(ModbusAsciiConnection connection)
        {
            var parity = ParseParity(connection.Parity, "Modbus ASCII");
            var stopBits = ParseStopBits(connection.StopBits, "Modbus ASCII");
            if (connection.BaudRate is < 300 or > 4_000_000 || connection.DataBits != 7 ||
                (parity == Parity.None && stopBits != StopBits.Two) ||
                (parity != Parity.None && stopBits != StopBits.One))
                throw new InvalidOperationException("Modbus ASCII 波特率、数据位、校验位或停止位组合无效。");
            return new SerialSettings("ascii", connection.BaudRate, connection.DataBits, parity, stopBits);
        }

        private static Parity ParseParity(string? value, string protocol)
            => value switch
            {
                "none" => Parity.None,
                "even" => Parity.Even,
                "odd" => Parity.Odd,
                _ => throw new InvalidOperationException($"{protocol} 校验位配置无效。")
            };

        private static StopBits ParseStopBits(string? value, string protocol)
            => value switch
            {
                "one" => StopBits.One,
                "two" => StopBits.Two,
                _ => throw new InvalidOperationException($"{protocol} 停止位配置无效。")
            };
    }

    public sealed class Lease : IAsyncDisposable
    {
        private readonly ModbusSerialSessionPool _owner;
        private readonly SharedSession _session;
        private readonly SourceVersionKey? _sourceKey;
        private int _disposed;

        internal Lease(ModbusSerialSessionPool owner, SharedSession session, SourceVersionKey? sourceKey)
        {
            _owner = owner;
            _session = session;
            _sourceKey = sourceKey;
        }

        public Task<T> ExecuteModbusAsync<T>(
            int timeoutMs,
            Func<IModbusMaster, CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(operation);
            return _session.ExecuteAsync(timeoutMs, operation, ThrowIfDisposed, ct);
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
        private readonly ILogger<ModbusSerialSessionPool>? _logger;
        private readonly IDisposable _serialReservation;
        private Task? _closeTask;
        private SerialPort? _port;
        private SerialPortResource? _resource;
        private IModbusMaster? _master;
        private bool _closed;
        private bool _poisoned;
        private int _closing;

        public SharedSession(
            string portName,
            SerialSettings settings,
            ILogger<ModbusSerialSessionPool>? logger,
            IDisposable serialReservation)
        {
            PortName = portName;
            Settings = settings;
            _logger = logger;
            _serialReservation = serialReservation;
        }

        public string PortName { get; }
        public SerialSettings Settings { get; }
        public int ReferenceCount { get; private set; }
        public bool IsClosing => Volatile.Read(ref _closing) != 0;
        public TaskCompletionSource<bool> Closed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IEnumerable<KeyValuePair<SourceVersionKey, int>> SourceReferences => _sourceReferences.ToArray();

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
                if (count <= 1) _sourceReferences.Remove(sourceKey.Value);
                else _sourceReferences[sourceKey.Value] = count - 1;
            }
        }

        public void MarkClosing() => Interlocked.Exchange(ref _closing, 1);

        public async Task<T> ExecuteAsync<T>(
            int timeoutMs,
            Func<IModbusMaster, CancellationToken, Task<T>> operation,
            Action validateLease,
            CancellationToken ct)
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
            await _ioGate.WaitAsync(wait.Token).ConfigureAwait(false);
            try
            {
                if (_closed || IsClosing)
                    throw new ObjectDisposedException(nameof(ModbusSerialSessionPool));
                if (_poisoned)
                    throw new IOException($"串口 {PortName} 会话已失效，需释放全部采集任务后重新连接。");
                validateLease();

                var timeout = Math.Max(1000, timeoutMs);
                using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdown.Token);
                operationTimeout.CancelAfter(timeout);
                try
                {
                    EnsureConnected(timeout);
                    _resource!.ReadTimeout = timeout;
                    _resource.WriteTimeout = timeout;
                    var master = _master!;
                    master.Transport.ReadTimeout = timeout;
                    master.Transport.WriteTimeout = timeout;
                    var work = Task.Run(() => operation(master, operationTimeout.Token));
                    try
                    {
                        return await work.WaitAsync(operationTimeout.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_shutdown.IsCancellationRequested)
                    {
                        ResetConnection();
                        try { await work.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false); }
                        catch { _poisoned = true; }
                        throw new TimeoutException($"Modbus {Settings.FrameMode.ToUpperInvariant()} 串口 {PortName} 读取超过 {timeout}ms。");
                    }
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && !_shutdown.IsCancellationRequested)
                {
                    ResetConnection();
                    throw new TimeoutException($"Modbus {Settings.FrameMode.ToUpperInvariant()} 串口 {PortName} 读取超过 {timeout}ms。");
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
                _serialReservation.Dispose();
            }
        }

        private void EnsureConnected(int timeoutMs)
        {
            if (_port is not null) return;
            var port = new SerialPort(PortName, Settings.BaudRate, Settings.Parity, Settings.DataBits, Settings.StopBits)
            {
                ReadTimeout = timeoutMs,
                WriteTimeout = timeoutMs
            };
            try
            {
                port.Open();
                var resource = new SerialPortResource(port);
                var factory = new ModbusFactory();
                var master = Settings.FrameMode == "ascii"
                    ? factory.CreateAsciiMaster(resource)
                    : factory.CreateRtuMaster(resource);
                _port = port;
                _resource = resource;
                _master = master;
            }
            catch
            {
                port.Dispose();
                throw;
            }
        }

        private void ResetConnection()
        {
            var master = _master;
            var resource = _resource;
            var port = _port;
            _master = null;
            _resource = null;
            _port = null;
            try { master?.Dispose(); }
            catch (Exception exception) { _logger?.LogDebug(exception, "释放 Modbus RTU master 失败：{Port}", PortName); }
            try { resource?.Dispose(); }
            catch (Exception exception) { _logger?.LogDebug(exception, "释放 Modbus RTU stream resource 失败：{Port}", PortName); }
            try { port?.Dispose(); }
            catch (Exception exception) { _logger?.LogDebug(exception, "释放 Modbus RTU 串口失败：{Port}", PortName); }
        }
    }

    private sealed class SerialPortResource(SerialPort port) : IStreamResource, IDisposable
    {
        public int InfiniteTimeout => Timeout.Infinite;
        public int ReadTimeout { get => port.ReadTimeout; set => port.ReadTimeout = value; }
        public int WriteTimeout { get => port.WriteTimeout; set => port.WriteTimeout = value; }
        public void DiscardInBuffer() => port.DiscardInBuffer();
        public int Read(byte[] buffer, int offset, int count) => port.Read(buffer, offset, count);
        public void Write(byte[] buffer, int offset, int count) => port.Write(buffer, offset, count);
        public void Dispose() => port.Dispose();
    }
}
