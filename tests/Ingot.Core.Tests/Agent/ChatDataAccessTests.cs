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

    private sealed class RecordingEventStore : IPlatformEventStore
    {
        public List<PlatformEventQuery> Queries { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<EventBatchResponse> IngestAsync(EventBatchRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<PlatformProductionEvent>>([]);
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
