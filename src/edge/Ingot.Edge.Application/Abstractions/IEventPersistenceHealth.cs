namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     记录事件记录持久化路径的最近状态。它与数据库连通性检查互补：
///     一次真实 append 失败必须立即反映到健康端点。
/// </summary>
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
