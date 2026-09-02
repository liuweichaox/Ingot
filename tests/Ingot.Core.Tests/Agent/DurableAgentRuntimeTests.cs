using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class DurableAgentRuntimeTests
{
    [Fact]
    public async Task ProcessNextAsync_RevokedAuthorization_FailsBeforeAToolCanReadData()
    {
        var store = new DurableRunStore();
        await store.CreateAsync(QueuedRun("revoked"));
        var tool = new RecordingTool();
        var runtime = CreateRuntime(store, tool, new DenyAuthorization());

        Assert.True(await runtime.ProcessNextAsync("worker-a"));

        var saved = await store.GetAsync("revoked");
        Assert.Equal(AgentRunStatuses.Failed, saved!.Status);
        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task ProcessNextAsync_InitializationFailure_MarksClaimedRunFailed()
    {
        var store = new DurableRunStore { ThrowOnHistoryRead = true };
        await store.CreateAsync(QueuedRun("initialization-failure"));
        var runtime = CreateRuntime(store, new RecordingTool(), new AllowAuthorization());

        Assert.True(await runtime.ProcessNextAsync("worker-a"));

        var saved = await store.GetAsync("initialization-failure");
        Assert.Equal(AgentRunStatuses.Failed, saved!.Status);
        Assert.Contains("历史", saved.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessNextAsync_LostLease_CannotOverwriteNewOwnersSnapshotOrAppendEvents()
    {
        var store = new DurableRunStore { LoseLeaseOnFirstUpdate = true };
        await store.CreateAsync(QueuedRun("lease-lost"));
        var runtime = CreateRuntime(store, new RecordingTool(), new AllowAuthorization());

        Assert.True(await runtime.ProcessNextAsync("worker-a"));

        var saved = await store.GetAsync("lease-lost");
        Assert.Equal(AgentRunStatuses.Running, saved!.Status);
        Assert.Equal("worker-b", store.CurrentLeaseOwner);
        Assert.Empty(await store.ReadEventsAsync("lease-lost", 0, 100));
    }

    private static AgentRuntime CreateRuntime(
        DurableRunStore store,
        IAnalysisTool tool,
        IAgentRunAuthorization authorization)
    {
        var options = Options.Create(new ChatOptions
        {
            Enabled = true,
            Provider = "Deterministic",
            FastModel = "deterministic-v1",
            ReasoningModel = "deterministic-v1",
            MaxRunSeconds = 10
        });
        return new AgentRuntime(
            store,
            new DefaultModelRouter([new DeterministicModelClient()]),
            [tool],
            new DefaultPlanValidator(options),
            new DefaultAnalysisResultValidator(),
            new BoundedCombinedAnalysisWorkflow(options),
            new NullAgentRunLifecycleSink(),
            options,
            NullLogger<AgentRuntime>.Instance,
            new FixedModelSettings(),
            authorization);
    }

    private static AgentRunSnapshot QueuedRun(string runId) => new()
    {
        RunId = runId,
        ConversationId = Guid.NewGuid().ToString(),
        UserId = "operator",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Question = "检查数据质量",
        Mode = "quick",
        Status = AgentRunStatuses.Queued,
        ModelProvider = "Deterministic",
        Model = "deterministic-v1",
        PromptVersion = "test",
        ToolsetVersion = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        Usage = new AgentUsageSummary(),
        AccessScope = new AgentRunAccessScopeSnapshot { SiteIds = ["SITE-001"] }
    };

    private sealed class FixedModelSettings : IModelServiceConfigurationProvider
    {
        public ModelServiceConnectionSettings Current { get; } = new()
        {
            Enabled = true,
            Provider = "Deterministic",
            Protocol = "deterministic",
            FastModel = "deterministic-v1",
            ReasoningModel = "deterministic-v1"
        };
    }

    private sealed class AllowAuthorization : IAgentRunAuthorization
    {
        public Task<AgentAccessScope?> ResolveCurrentScopeAsync(
            string userId,
            AgentRunAccessScopeSnapshot capturedScope,
            CancellationToken ct = default)
            => Task.FromResult<AgentAccessScope?>(new AgentAccessScope
            {
                SiteIds = new HashSet<string>(capturedScope.SiteIds, StringComparer.OrdinalIgnoreCase)
            });
    }

    private sealed class DenyAuthorization : IAgentRunAuthorization
    {
        public Task<AgentAccessScope?> ResolveCurrentScopeAsync(
            string userId,
            AgentRunAccessScopeSnapshot capturedScope,
            CancellationToken ct = default)
            => Task.FromResult<AgentAccessScope?>(null);
    }

    private sealed class RecordingTool : IAnalysisTool
    {
        public bool Executed { get; private set; }

        public AnalysisToolDefinition Definition { get; } = new()
        {
            Name = "check_data_quality",
            Version = "v1",
            Description = "test",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Access = AgentToolAccess.Read,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new { siteId = new { type = "string" } },
                additionalProperties = false
            })
        };

        public Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default)
        {
            Executed = true;
            return Task.FromResult(new AnalysisToolResult
            {
                Tool = call.Tool,
                Summary = "已检查。",
                Data = JsonSerializer.SerializeToElement(new { count = 1 }),
                RelatedRecords = [new RelatedRecordRef { Kind = "dataset", Id = "1", Label = "test" }]
            });
        }
    }

    private sealed class DurableRunStore : IDurableAgentRunStore
    {
        private AgentRunSnapshot? run;
        private readonly List<AgentStreamEvent> events = [];
        private string? leaseOwner;
        private long generation;
        private long sequence;

        public bool ThrowOnHistoryRead { get; init; }
        public bool LoseLeaseOnFirstUpdate { get; init; }
        public string? CurrentLeaseOwner => leaseOwner;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CreateAsync(AgentRunSnapshot value, CancellationToken ct = default)
        {
            run = value;
            return Task.CompletedTask;
        }

        public Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default)
            => Task.FromResult(run?.RunId == runId ? run : null);

        public Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(
            string entryPoint, string userId, DateTimeOffset? before, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentRunSnapshot>>([]);

        public Task<IReadOnlyList<AgentRunSnapshot>> ListConversationAsync(
            string entryPoint, string userId, string conversationId, int limit, CancellationToken ct = default)
        {
            if (ThrowOnHistoryRead)
                throw new InvalidOperationException("读取对话历史失败。");
            return Task.FromResult<IReadOnlyList<AgentRunSnapshot>>([]);
        }

        public Task UpdateAsync(AgentRunSnapshot value, CancellationToken ct = default)
        {
            run = value;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> DeleteConversationAsync(
            string entryPoint, string userId, string conversationId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<AgentStreamEvent> AppendEventAsync(
            string runId, string type, object? data, CancellationToken ct = default)
            => Task.FromResult(Append(runId, type, data));

        public Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(
            string runId, long afterSequence, int limit, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentStreamEvent>>(events.Where(item => item.Sequence > afterSequence).ToArray());

        public Task<ClaimedAgentRun?> ClaimNextAsync(
            string owner, TimeSpan leaseDuration, CancellationToken ct = default)
        {
            if (run is null || run.Status != AgentRunStatuses.Queued)
                return Task.FromResult<ClaimedAgentRun?>(null);
            leaseOwner = owner;
            generation++;
            run = run with { Status = AgentRunStatuses.Running };
            return Task.FromResult<ClaimedAgentRun?>(new ClaimedAgentRun(
                run, new AgentRunLease(run.RunId, owner, generation)));
        }

        public Task<bool> RenewLeaseAsync(AgentRunLease lease, TimeSpan leaseDuration, CancellationToken ct = default)
            => Task.FromResult(Owns(lease));

        public Task ReleaseLeaseAsync(AgentRunLease lease, CancellationToken ct = default)
        {
            if (Owns(lease) && run is not null && AgentRunStatuses.IsTerminal(run.Status))
                leaseOwner = null;
            return Task.CompletedTask;
        }

        public Task<bool> UpdateLeasedAsync(AgentRunSnapshot value, AgentRunLease lease, CancellationToken ct = default)
        {
            if (LoseLeaseOnFirstUpdate && generation == 1)
            {
                leaseOwner = "worker-b";
                generation++;
                return Task.FromResult(false);
            }
            if (!Owns(lease))
                return Task.FromResult(false);
            run = value;
            return Task.FromResult(true);
        }

        public Task<AgentStreamEvent?> AppendLeasedEventAsync(
            string runId, AgentRunLease lease, string type, object? data, CancellationToken ct = default)
            => Task.FromResult<AgentStreamEvent?>(Owns(lease) ? Append(runId, type, data) : null);

        public Task<AgentRunSnapshot?> RequestCancellationAsync(
            string runId, string userId, string reason, CancellationToken ct = default)
        {
            if (run is null || run.RunId != runId || run.UserId != userId || AgentRunStatuses.IsTerminal(run.Status))
                return Task.FromResult<AgentRunSnapshot?>(null);
            run = run with
            {
                Status = run.Status == AgentRunStatuses.Queued ? AgentRunStatuses.Cancelled : AgentRunStatuses.Cancelling,
                CancellationReason = reason
            };
            return Task.FromResult<AgentRunSnapshot?>(run);
        }

        private bool Owns(AgentRunLease lease)
            => run?.RunId == lease.RunId && leaseOwner == lease.Owner && generation == lease.Generation;

        private AgentStreamEvent Append(string runId, string type, object? data)
        {
            var item = new AgentStreamEvent
            {
                Sequence = ++sequence,
                Type = type,
                OccurredAt = DateTimeOffset.UtcNow,
                Data = data is null ? null : JsonSerializer.SerializeToElement(data)
            };
            events.Add(item);
            return item;
        }
    }
}
