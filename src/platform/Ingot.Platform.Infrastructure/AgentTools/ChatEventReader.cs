using Ingot.Platform.Infrastructure.Events;
using Ingot.Contracts.Events;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class ChatDataAccessOptions
{
    public Dictionary<string, ChatUserDataScope> Users { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ChatUserDataScope
{
    public bool AllowAll { get; set; }

    public IReadOnlyList<string> EdgeIds { get; set; } = [];
}

public interface IChatEventReader
{
    Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
        string userId,
        PlatformEventQuery query,
        CancellationToken ct = default);

    Task<PlatformEventScopeStats> GetScopeStatsAsync(
        string userId,
        PlatformEventQuery query,
        CancellationToken ct = default);
}

public sealed class ChatEventReader(
    IPlatformEventStore events,
    IOptions<ChatDataAccessOptions> options) : IChatEventReader
{
    private readonly ChatDataAccessOptions _options = options.Value;

    public async Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
        string userId,
        PlatformEventQuery query,
        CancellationToken ct = default)
    {
        var edgeIds = ResolveEdgeScope(userId);
        if (edgeIds is null)
            return await events.QueryAsync(query, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.EdgeId))
        {
            EnsureEdgeAllowed(edgeIds, query.EdgeId);
            return await events.QueryAsync(query, ct).ConfigureAwait(false);
        }

        var batches = await Task.WhenAll(edgeIds.Select(edgeId =>
            events.QueryAsync(query with { EdgeId = edgeId }, ct))).ConfigureAwait(false);
        var ordered = query.AfterIngestId.HasValue
            ? batches.SelectMany(static batch => batch).OrderBy(static row => row.IngestId)
            : batches.SelectMany(static batch => batch).OrderByDescending(static row => row.IngestId);
        return ordered.Take(Math.Clamp(query.Limit, 1, 500)).ToArray();
    }

    public async Task<PlatformEventScopeStats> GetScopeStatsAsync(
        string userId,
        PlatformEventQuery query,
        CancellationToken ct = default)
    {
        var edgeIds = ResolveEdgeScope(userId);
        if (edgeIds is null)
            return await events.GetScopeStatsAsync(query, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.EdgeId))
        {
            EnsureEdgeAllowed(edgeIds, query.EdgeId);
            return await events.GetScopeStatsAsync(query, ct).ConfigureAwait(false);
        }

        var parts = await Task.WhenAll(edgeIds.Select(edgeId =>
            events.GetScopeStatsAsync(query with { EdgeId = edgeId }, ct))).ConfigureAwait(false);
        return Combine(parts);
    }

    // null 表示 AllowAll（不按 Edge 收窄）；否则返回该用户 被授权的 Edge 集合。
    private string[]? ResolveEdgeScope(string userId)
    {
        if (!_options.Users.TryGetValue(userId, out var scope))
            throw new UnauthorizedAccessException("当前用户 没有配置生产数据访问范围。");
        if (scope.AllowAll)
            return null;

        var edgeIds = scope.EdgeIds
            .Where(static edgeId => !string.IsNullOrWhiteSpace(edgeId))
            .Select(static edgeId => edgeId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (edgeIds.Length == 0)
            throw new UnauthorizedAccessException("当前用户 的生产数据访问范围为空。");
        return edgeIds;
    }

    private static void EnsureEdgeAllowed(string[] edgeIds, string requestedEdgeId)
    {
        if (!edgeIds.Contains(requestedEdgeId, StringComparer.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("当前用户 不能访问请求的 Edge 数据。");
    }

    private static PlatformEventScopeStats Combine(IReadOnlyList<PlatformEventScopeStats> parts)
    {
        var latests = parts.Where(static part => part.LatestOccurredAt.HasValue)
            .Select(static part => part.LatestOccurredAt!.Value).ToArray();
        var earliests = parts.Where(static part => part.EarliestOccurredAt.HasValue)
            .Select(static part => part.EarliestOccurredAt!.Value).ToArray();
        return new PlatformEventScopeStats
        {
            Count = parts.Sum(static part => part.Count),
            LatestOccurredAt = latests.Length == 0 ? null : latests.Max(),
            EarliestOccurredAt = earliests.Length == 0 ? null : earliests.Min()
        };
    }
}
