namespace Ingot.Edge.Application.Abstractions;

public interface IEventPersistenceHealth
{
    EventPersistenceHealthSnapshot Snapshot { get; }

    void ReportSuccess(DateTimeOffset at);

    void ReportFailure(DateTimeOffset at, Exception exception);
}

public sealed record EventPersistenceHealthSnapshot
{
    public bool IsDegraded { get; init; }

    public int ConsecutiveFailures { get; init; }

    public DateTimeOffset? LastSuccessAt { get; init; }

    public DateTimeOffset? LastFailureAt { get; init; }

    public string? LastError { get; init; }
}
