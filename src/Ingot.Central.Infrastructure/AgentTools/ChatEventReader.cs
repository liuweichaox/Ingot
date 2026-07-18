using Ingot.Central.Infrastructure.Events;
using Ingot.Contracts.Events;
using Microsoft.Extensions.Options;

namespace Ingot.Central.Infrastructure.AgentTools;

public sealed class ChatDataAccessOptions
{
    public Dictionary<string, ChatActorDataScope> Actors { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ChatActorDataScope
{
    public bool AllowAll { get; set; }

    public IReadOnlyList<string> EdgeIds { get; set; } = [];
}

public interface IChatEventReader
{
    Task<IReadOnlyList<CentralProductionEvent>> QueryAsync(
        string actorId,
        CentralEventQuery query,
        CancellationToken ct = default);
}

public sealed class ChatEventReader(
    ICentralEventStore events,
    IOptions<ChatDataAccessOptions> options) : IChatEventReader
{
    private readonly ChatDataAccessOptions _options = options.Value;

    public async Task<IReadOnlyList<CentralProductionEvent>> QueryAsync(
        string actorId,
        CentralEventQuery query,
        CancellationToken ct = default)
    {
        if (!_options.Actors.TryGetValue(actorId, out var scope))
            throw new UnauthorizedAccessException("当前 Actor 没有配置生产事实访问范围。");
        if (scope.AllowAll)
            return await events.QueryAsync(query, ct).ConfigureAwait(false);

        var edgeIds = scope.EdgeIds
            .Where(static edgeId => !string.IsNullOrWhiteSpace(edgeId))
            .Select(static edgeId => edgeId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (edgeIds.Length == 0)
            throw new UnauthorizedAccessException("当前 Actor 的生产事实访问范围为空。");

        if (!string.IsNullOrWhiteSpace(query.EdgeId))
        {
            if (!edgeIds.Contains(query.EdgeId, StringComparer.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("当前 Actor 不能访问请求的 Edge 数据。");
            return await events.QueryAsync(query, ct).ConfigureAwait(false);
        }

        var batches = await Task.WhenAll(edgeIds.Select(edgeId =>
            events.QueryAsync(query with { EdgeId = edgeId }, ct))).ConfigureAwait(false);
        var ordered = query.AfterIngestId.HasValue
            ? batches.SelectMany(static batch => batch).OrderBy(static row => row.IngestId)
            : batches.SelectMany(static batch => batch).OrderByDescending(static row => row.IngestId);
        return ordered.Take(Math.Clamp(query.Limit, 1, 500)).ToArray();
    }
}
