using Ingot.Central.Infrastructure.AgentTools;
using Ingot.Central.Infrastructure.Events;
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
            Actors = new Dictionary<string, ChatActorDataScope>(StringComparer.OrdinalIgnoreCase)
            {
                ["operator"] = new() { EdgeIds = ["EDGE-001", "EDGE-002"] }
            }
        }));

        await reader.QueryAsync("OPERATOR", new CentralEventQuery { CorrelationId = "CYCLE-1", Limit = 20 });

        Assert.Equal(["EDGE-001", "EDGE-002"], store.Queries.Select(static query => query.EdgeId).Order());
    }

    [Fact]
    public async Task Reader_DeniesActorWithoutScope()
    {
        var reader = new ChatEventReader(
            new RecordingEventStore(),
            Options.Create(new ChatDataAccessOptions()));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            reader.QueryAsync("unknown", new CentralEventQuery()));
    }

    private sealed class RecordingEventStore : ICentralEventStore
    {
        public List<CentralEventQuery> Queries { get; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<EventBatchResponse> IngestAsync(EventBatchRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CentralProductionEvent>> QueryAsync(
            CentralEventQuery query,
            CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult<IReadOnlyList<CentralProductionEvent>>([]);
        }

        public Task<CentralEventScopeStats> GetScopeStatsAsync(
            CentralEventQuery query,
            CancellationToken ct = default)
        {
            Queries.Add(query);
            return Task.FromResult(new CentralEventScopeStats());
        }

        public Task<bool> CanConnectAsync(CancellationToken ct = default) => Task.FromResult(true);
    }
}
