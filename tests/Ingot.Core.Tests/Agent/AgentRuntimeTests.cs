using System.Collections.Concurrent;
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class AgentRuntimeTests
{
    [Fact]
    public async Task ChatRun_CompletesWithWhitelistedReadOnlyToolAndRelatedRecords()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, [new QualityTool()]);

        var created = await runtime.StartAsync(ProductEntryPoints.Chat, "analyst", new CreateChatRunRequest
        {
            Question = "检查最近数据是否完整"
        });
        var completed = await WaitForTerminalAsync(runtime, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.Equal(RunPurposes.ReadOnlyAnalysis, completed.Purpose);
        Assert.NotNull(completed.Answer);
        Assert.Equal("check_data_quality", Assert.Single(completed.ToolInvocations).Tool);
        Assert.Single(completed.Answer!.RelatedRecords);
        var events = await store.ReadEventsAsync(created.RunId, 0, 100);
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.PlanCreated);
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.RunCompleted);
    }

    [Fact]
    public async Task ChatRun_RefusesConclusionWhenToolReportsInsufficientData()
    {
        var runtime = CreateRuntime(new MemoryRunStore(), [new InsufficientQualityTool()]);
        var created = await runtime.StartAsync(ProductEntryPoints.Chat, "analyst", new CreateChatRunRequest
        {
            Question = "检查数据完整性"
        });
        var completed = await WaitForTerminalAsync(runtime, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.NotNull(completed.Answer);
        Assert.Empty(completed.Answer!.Findings);
        Assert.NotEmpty(completed.Answer.Limitations);
        Assert.Equal(1, completed.Usage.ModelCalls);
    }

    [Fact]
    public async Task ChatRun_HistoryIsUserScopedAndUnsupportedEntryPointIsRejected()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, [new QualityTool()]);
        await store.CreateAsync(Snapshot("other", "other-user"));

        var page = await runtime.ListAsync(ProductEntryPoints.Chat, "analyst", null, 20);

        Assert.Empty(page.Items);
        Assert.Throws<ArgumentOutOfRangeException>(() => runtime.GetCapabilities("unsupported"));
        Assert.Contains(ProductEntryPoints.Chat, ProductEntryPoints.All);
        Assert.Contains(ProductEntryPoints.Mcp, ProductEntryPoints.All);
        Assert.Contains(ProductEntryPoints.Monitor, ProductEntryPoints.All);
    }

    [Fact]
    public async Task ChatRun_CanBeCancelledBeforeExecutionStarts()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, [new BlockingQualityTool()]);
        var created = await runtime.StartAsync(ProductEntryPoints.Chat, "operator", new CreateChatRunRequest
        {
            Question = "检查数据"
        });

        Assert.True(await runtime.CancelAsync(ProductEntryPoints.Chat, created.RunId, "operator", "取消"));
        var completed = await WaitForTerminalAsync(runtime, created.RunId);
        Assert.Equal(AgentRunStatuses.Cancelled, completed.Status);
    }

    [Fact]
    public async Task ChatRun_CompletedHistoryCanBeDeletedOnlyByItsOwner()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, [new QualityTool()]);
        await store.CreateAsync(Snapshot("owned", "operator"));

        Assert.False(await runtime.DeleteAsync(ProductEntryPoints.Chat, "owned", "other-user"));
        Assert.True(await runtime.DeleteAsync(ProductEntryPoints.Chat, "owned", "operator"));
        Assert.Null(await runtime.GetAsync(ProductEntryPoints.Chat, "owned"));
    }

    private static AgentRuntime CreateRuntime(MemoryRunStore store, IReadOnlyList<IAnalysisTool> tools)
    {
        var options = Options.Create(new ChatOptions
        {
            Enabled = true,
            MaxRunSeconds = 10,
            EnableCombinedAnalysis = true,
            MaxDiscussionRounds = 1,
            MaxDiscussionTurns = 3
        });
        var model = new DeterministicModelClient();
        return new AgentRuntime(
            store,
            new DefaultModelRouter([model]),
            tools,
            new DefaultPlanValidator(options),
            new DefaultAnalysisResultValidator(),
            new BoundedCombinedAnalysisWorkflow(options),
            options,
            NullLogger<AgentRuntime>.Instance);
    }

    private static async Task<AgentRunSnapshot> WaitForTerminalAsync(IAgentRuntime runtime, string runId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var snapshot = await runtime.GetAsync(ProductEntryPoints.Chat, runId);
            if (snapshot is not null && AgentRunStatuses.IsTerminal(snapshot.Status))
                return snapshot;
            await Task.Delay(10);
        }
        throw new TimeoutException("Chat 运行没有在预期时间内结束。");
    }

    private static AgentRunSnapshot Snapshot(string runId, string userId) => new()
    {
        RunId = runId,
        UserId = userId,
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Question = "history",
        Mode = "quick",
        Status = AgentRunStatuses.Completed,
        ModelProvider = "test",
        Model = "test",
        PromptVersion = "test",
        ToolsetVersion = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        Usage = new AgentUsageSummary()
    };

    private sealed class QualityTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition { get; } = DefinitionFor("check_data_quality");

        public Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default)
            => Task.FromResult(new AnalysisToolResult
            {
                Tool = call.Tool,
                Summary = "已检查 10 条事件，数据完整。",
                Data = JsonSerializer.SerializeToElement(new { eventCount = 10 }),
                RelatedRecords = [new RelatedRecordRef { Kind = "dataset", Id = "quality-1", Label = "数据质量" }]
            });
    }

    private sealed class InsufficientQualityTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition { get; } = DefinitionFor("check_data_quality");

        public Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default)
            => Task.FromResult(new AnalysisToolResult
            {
                Tool = call.Tool,
                Summary = "数据缺失，无法判断。",
                Data = JsonSerializer.SerializeToElement(new { missing = true }),
                Outcome = AnalysisToolOutcomes.InsufficientData,
                Limitations = ["遥测窗口不完整。"],
                RelatedRecords = [new RelatedRecordRef { Kind = "dataset", Id = "quality-2", Label = "数据质量" }]
            });
    }

    private sealed class BlockingQualityTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition { get; } = DefinitionFor("check_data_quality");

        public async Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("不可达。");
        }
    }

    private static AnalysisToolDefinition DefinitionFor(string name) => new()
    {
        Name = name,
        Version = "v1",
        Description = "test",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Access = AgentToolAccess.Read,
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
            additionalProperties = false
        })
    };

    private sealed class MemoryRunStore : IAgentRunStore
    {
        private readonly ConcurrentDictionary<string, AgentRunSnapshot> _runs = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, List<AgentStreamEvent>> _events = new(StringComparer.Ordinal);
        private long _sequence;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CreateAsync(AgentRunSnapshot run, CancellationToken ct = default)
        {
            _runs[run.RunId] = run;
            _events.TryAdd(run.RunId, []);
            return Task.CompletedTask;
        }

        public Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default)
            => Task.FromResult(_runs.TryGetValue(runId, out var run) ? run : null);

        public Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(
            string entryPoint,
            string userId,
            DateTimeOffset? before,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentRunSnapshot>>(_runs.Values
                .Where(run => run.EntryPoint == entryPoint && run.UserId == userId && (!before.HasValue || run.CreatedAt < before.Value))
                .OrderByDescending(static run => run.CreatedAt)
                .Take(limit)
                .ToArray());

        public Task UpdateAsync(AgentRunSnapshot run, CancellationToken ct = default)
        {
            _runs[run.RunId] = run;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteAsync(string runId, CancellationToken ct = default)
        {
            _events.TryRemove(runId, out _);
            return Task.FromResult(_runs.TryRemove(runId, out _));
        }

        public Task<AgentStreamEvent> AppendEventAsync(
            string runId,
            string type,
            object? data,
            CancellationToken ct = default)
        {
            var item = new AgentStreamEvent
            {
                Sequence = Interlocked.Increment(ref _sequence),
                Type = type,
                OccurredAt = DateTimeOffset.UtcNow,
                Data = data is null ? null : JsonSerializer.SerializeToElement(data)
            };
            _events.GetOrAdd(runId, []).Add(item);
            return Task.FromResult(item);
        }

        public Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(
            string runId,
            long afterSequence,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentStreamEvent>>(_events.TryGetValue(runId, out var events)
                ? events.Where(item => item.Sequence > afterSequence).Take(limit).ToArray()
                : []);
    }
}
