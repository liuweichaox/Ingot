// Prevents independent serial drivers from opening the same Edge port at once.
namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class AcquisitionSerialSessionCoordinator
{
    private readonly object _sync = new();
    private readonly Dictionary<string, Reservation> _reservations = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public IDisposable Acquire(string portName, string protocolFamily, string fingerprint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portName);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocolFamily);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

        lock (_sync)
        {
            if (_reservations.TryGetValue(portName, out var existing))
            {
                var compatibleSession = string.Equals(existing.ProtocolFamily, protocolFamily, StringComparison.Ordinal) &&
                                        string.Equals(existing.Fingerprint, fingerprint, StringComparison.Ordinal);
                throw new InvalidOperationException(
                    compatibleSession
                        ? $"串口 {portName} 已有兼容的 {existing.ProtocolFamily} 会话；新任务必须通过共享会话池复用连接。"
                        : $"串口 {portName} 已由 {existing.ProtocolFamily} 会话占用且总线参数不兼容；请为当前采集使用独立物理串口。");
            }

            var reservation = new Reservation(protocolFamily, fingerprint);
            _reservations.Add(portName, reservation);
            return new Lease(this, portName, reservation);
        }
    }

    private void Release(string portName, Reservation reservation)
    {
        lock (_sync)
        {
            if (!_reservations.TryGetValue(portName, out var current) || !ReferenceEquals(current, reservation))
                return;
            _reservations.Remove(portName);
        }
    }

    private sealed class Reservation(string protocolFamily, string fingerprint)
    {
        public string ProtocolFamily { get; } = protocolFamily;
        public string Fingerprint { get; } = fingerprint;
    }

    private sealed class Lease(
        AcquisitionSerialSessionCoordinator owner,
        string portName,
        Reservation reservation) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                owner.Release(portName, reservation);
        }
    }
}
