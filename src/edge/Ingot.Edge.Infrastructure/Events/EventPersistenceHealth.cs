using Ingot.Edge.Application.Abstractions;

namespace Ingot.Edge.Infrastructure.Events;

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
