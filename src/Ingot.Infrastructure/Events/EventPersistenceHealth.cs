using Ingot.Application.Abstractions;

namespace Ingot.Infrastructure.Events;

/// <summary>
///     进程内事件持久化健康状态。成功 append 是唯一恢复信号，
///     避免单纯 SELECT 成功掩盖实际写路径故障。
/// </summary>
public sealed class EventPersistenceHealth : IEventPersistenceHealth
{
    private readonly object _sync = new();
    private EventPersistenceHealthSnapshot _snapshot = new();

    public EventPersistenceHealthSnapshot Snapshot
    {
        get
        {
            lock (_sync)
                return _snapshot;
        }
    }

    public void ReportSuccess(DateTimeOffset at)
    {
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                IsDegraded = false,
                ConsecutiveFailures = 0,
                LastSuccessAt = at,
                LastError = null
            };
        }
    }

    public void ReportFailure(DateTimeOffset at, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (_sync)
        {
            _snapshot = _snapshot with
            {
                IsDegraded = true,
                ConsecutiveFailures = _snapshot.ConsecutiveFailures + 1,
                LastFailureAt = at,
                LastError = exception.GetBaseException().Message
            };
        }
    }
}
