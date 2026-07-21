using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Contracts.Events;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class ChatDataAccessTests
{
    [Fact]
    public async Task Reader_QueriesOnlyConfiguredEdges()
    {
        var store = new RecordingEventStore();
        var reader = new ChatEventReader(store, Options.Create(new ChatDataAccessOptions
        {
            Users = new Dictionary<string, ChatUserDataScope>(StringComparer.OrdinalIgnoreCase)
            {
                ["operator"] = new() { EdgeIds = ["EDGE-001", "EDGE-002"] }
            }
        }));

        await reader.QueryAsync("OPERATOR", new PlatformEventQuery { CorrelationId = "CYCLE-1", Limit = 20 });

        Assert.Equal(["EDGE-001", "EDGE-002"], store.Queries.Select(static query => query.EdgeId).Order());
    }

    [Fact]
    public async Task Reader_DeniesUserWithoutScope()
    {
        var reader = new ChatEventReader(
            new RecordingEventStore(),
            Options.Create(new ChatDataAccessOptions()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            reader.QueryAsync("unknown", new PlatformEventQuery()));
    }

    [Fact]
    public async Task Reader_QueryAll_PagesPastFiveHundredRows()
    {
        var store = new RecordingEventStore(1_201);
        var reader = new ChatEventReader(store, Options.Create(new ChatDataAccessOptions
        {
            Users = new Dictionary<string, ChatUserDataScope>(StringComparer.OrdinalIgnoreCase)
            {
                ["operator"] = new() { AllowAll = true }
            }
        }));

        var rows = await reader.QueryAllAsync("operator", new PlatformEventQuery());

        Assert.Equal(1_201, rows.Count);
        Assert.Equal([0L, 500L, 1_000L], store.Queries.Select(static query => query.AfterIngestId));
        Assert.All(store.Queries, static query => Assert.Equal(500, query.Limit));
    }

    private sealed class RecordingEventStore
        : IPlatformEventStore
    {
        private readonly IReadOnlyList<PlatformProductionEvent> _rows;

        public RecordingEventStore(int rowCount = 0)
            => _rows = Enumerable.Range(1, rowCount)
                .Select(static ingestId => new PlatformProductionEvent
                {
                    IngestId = ingestId,
                    EdgeId = "EDGE-001",
                    IngestedAt = DateTimeOffset.UnixEpoch,
                    Event = null!
                })
                .ToArray();

        public List<PlatformEventQuery> Queries { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<EventBatchResponse> IngestAsync(EventBatchRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            Queries.Add(query);
            var rows = _rows
                .Where(row => !query.AfterIngestId.HasValue || row.IngestId > query.AfterIngestId.Value)
                .OrderBy(static row => row.IngestId)
                .Take(query.Limit)
                .ToArray();
            return Task.FromResult<IReadOnlyList<PlatformProductionEvent>>(rows);
        }

        public Task<PlatformEventScopeStats> GetScopeStatsAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult(new PlatformEventScopeStats());
        }

        public Task<bool> CanConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
