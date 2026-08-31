// 验证 Agent 的 AgentRuntime 能力、只读边界和拒绝路径。

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
    private static readonly AgentAccessScope SiteScope = new()
    {
        SiteIds = new HashSet<string>(["SITE-001"], StringComparer.OrdinalIgnoreCase)
    };

    [Fact]
    public async Task ChatRun_CompletesWithWhitelistedReadOnlyToolAndRelatedRecords()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, [new QualityTool()]);

        var created = await runtime.StartAsync(ProductEntryPoints.Chat, "analyst", new CreateChatRunRequest
        {
            Question = "检查最近数据是否完整"
        }, SiteScope);
        var completed = await WaitForTerminalAsync(runtime, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.Equal(RunPurposes.ReadOnlyAnalysis, completed.Purpose);
        Assert.NotNull(completed.Answer);
        Assert.Equal("check_data_quality", Assert.Single(completed.ToolInvocations).Tool);
        var toolResult = Assert.Single(completed.ToolResults);
        Assert.Equal("check_data_quality", toolResult.Tool);
        Assert.Equal(64, toolResult.ContentHash.Length);
        Assert.NotEqual(default, toolResult.VerifiedAt);
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
        }, SiteScope);
        var completed = await WaitForTerminalAsync(runtime, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.NotNull(completed.Answer);
        Assert.Empty(completed.Answer!.Findings);
        Assert.NotEmpty(completed.Answer.Limitations);
        Assert.Equal(1, completed.Usage.ModelCalls);
    }

    [Fact]
    public async Task ChatRun_NoQueryPlanCompletesWithGuidanceInsteadOfFailure()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, [new QualityTool()], model: new NoQueryModelClient());

        var created = await runtime.StartAsync(
            ProductEntryPoints.Chat,
            "analyst",
            new CreateChatRunRequest { Question = "hi" },
            SiteScope);
        var completed = await WaitForTerminalAsync(runtime, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.Empty(completed.ToolInvocations);
        Assert.Contains("继续这段对话", completed.Answer!.Summary, StringComparison.Ordinal);
        Assert.Equal(2, completed.Usage.ModelCalls);
        var events = await store.ReadEventsAsync(created.RunId, 0, 100);
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.RunCompleted);
    }

    [Fact]
    public async Task ChatRun_ContinuationKeepsConversationAndSuppliesCompletedHistory()
    {
        var store = new MemoryRunStore();
        var model = new NoQueryModelClient();
        var runtime = CreateRuntime(store, [new QualityTool()], model: model);
        var first = await runtime.StartAsync(
            ProductEntryPoints.Chat,
            "analyst",
            new CreateChatRunRequest { Question = "hi" },
            SiteScope);
        var firstCompleted = await WaitForTerminalAsync(runtime, first.RunId);

        var second = await runtime.StartAsync(
            ProductEntryPoints.Chat,
            "analyst",
            new CreateChatRunRequest
            {
                Question = "继续",
                ConversationId = firstCompleted.ConversationId
            },
            SiteScope);
        await WaitForTerminalAsync(runtime, second.RunId);

        Assert.Equal(first.RunId, first.ConversationId);
        Assert.Equal(first.ConversationId, second.ConversationId);
        var previous = Assert.Single(model.LastRequest!.ConversationHistory);
        Assert.Equal("hi", previous.Question);
        Assert.Contains("继续这段对话", previous.Summary, StringComparison.Ordinal);
        Assert.Equal(2, (await runtime.GetConversationAsync(
            ProductEntryPoints.Chat,
            "analyst",
            first.ConversationId!)).Count);
        Assert.True(await runtime.DeleteConversationAsync(
            ProductEntryPoints.Chat,
            first.ConversationId!,
            "analyst"));
        Assert.Empty(await runtime.GetConversationAsync(
            ProductEntryPoints.Chat,
            "analyst",
            first.ConversationId!));
    }

    [Fact]
    public async Task ChatRun_DeterministicProviderDoesNotAdvertiseProfessionalCombinedAnalysis()
    {
        var runtime = CreateRuntime(new MemoryRunStore(), [new QualityTool()]);
        var capabilities = runtime.GetCapabilities(ProductEntryPoints.Chat);

        Assert.Equal(["quick"], capabilities.Modes);
        Assert.False(capabilities.CombinedAnalysisEnabled);
        Assert.True(capabilities.IsDeterministic);
        Assert.Empty(capabilities.Roles);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync(
            ProductEntryPoints.Chat,
            "analyst",
            new CreateChatRunRequest
            {
                Question = "综合核对最近数据是否完整",
                Mode = "combined"
            },
            SiteScope));
        Assert.Equal("多视角研判尚未启用。", error.Message);
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
        }, SiteScope);

        Assert.True(await runtime.CancelAsync(ProductEntryPoints.Chat, created.RunId, "operator", "取消"));
        var completed = await WaitForTerminalAsync(runtime, created.RunId);
        Assert.Equal(AgentRunStatuses.Cancelled, completed.Status);
    }

    [Fact]
    public async Task StartAsync_RejectsRunsBeyondConfiguredConcurrentLimit()
    {
        var runtime = CreateRuntime(
            new MemoryRunStore(),
            [new BlockingQualityTool()],
            options =>
            {
                options.MaxConcurrentRuns = 1;
                options.MaxConcurrentRunsPerUser = 1;
            });
        var first = await runtime.StartAsync(
            ProductEntryPoints.Chat,
            "operator",
            new CreateChatRunRequest { Question = "检查数据" },
            SiteScope);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync(
            ProductEntryPoints.Chat,
            "another-operator",
            new CreateChatRunRequest { Question = "检查其他数据" },
            SiteScope));

        Assert.Contains("系统并发上限", error.Message, StringComparison.Ordinal);
        Assert.True(await runtime.CancelAsync(ProductEntryPoints.Chat, first.RunId, "operator", "测试清理"));
        await WaitForTerminalAsync(runtime, first.RunId);
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

    [Fact]
    public async Task Runtime_ExecutesDeterministicKnowledgeSearchWithIntegerSchemaArgument()
    {
        var store = new MemoryRunStore();
        var tool = new KnowledgeSearchTool();
        var runtime = CreateRuntime(store, [tool]);

        var created = await runtime.StartAsync(
            ProductEntryPoints.Chat,
            "operator",
            new CreateChatRunRequest { Question = "作业指导书规定的温度上限是多少？" },
            SiteScope);
        var completed = await WaitForTerminalAsync(runtime, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.Equal("search_process_knowledge", Assert.Single(completed.ToolInvocations).Tool);
        Assert.True(tool.Executed);
    }

    [Fact]
    public async Task Runtime_ExcludesNonReadToolFromCapabilitiesAndExecution()
    {
        var store = new MemoryRunStore();
        var nonRead = new NonReadTool();
        var runtime = CreateRuntime(store, [new QualityTool(), nonRead]);

        var capabilities = runtime.GetCapabilities(ProductEntryPoints.Chat);
        Assert.DoesNotContain(capabilities.Tools, tool => tool.Name == nonRead.Definition.Name);
        var created = await runtime.StartAsync(
            ProductEntryPoints.Chat,
            "operator",
            new CreateChatRunRequest { Question = "检查数据质量" },
            SiteScope);
        var completed = await WaitForTerminalAsync(runtime, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.False(nonRead.Executed);
    }

    private static AgentRuntime CreateRuntime(
        MemoryRunStore store,
        IReadOnlyList<IAnalysisTool> tools,
        Action<ChatOptions>? configure = null,
        IModelClient? model = null)
    {
        var chatOptions = new ChatOptions
        {
            Enabled = true,
            MaxRunSeconds = 10,
            EnableCombinedAnalysis = true,
            MaxDiscussionRounds = 1,
            MaxDiscussionTurns = 3
        };
        configure?.Invoke(chatOptions);
        var options = Options.Create(chatOptions);
        var resolvedModel = model ?? new DeterministicModelClient();
        chatOptions.Provider = resolvedModel.Provider;
        chatOptions.FastModel = resolvedModel.ModelFor(ModelRole.Fast);
        chatOptions.ReasoningModel = resolvedModel.ModelFor(ModelRole.Reasoning);
        return new AgentRuntime(
            store,
            new DefaultModelRouter([resolvedModel]),
            tools,
            new DefaultPlanValidator(options),
            new DefaultAnalysisResultValidator(),
            new BoundedCombinedAnalysisWorkflow(options),
            new NullAgentRunLifecycleSink(),
            options,
            NullLogger<AgentRuntime>.Instance,
            new FixedModelServiceConfigurationProvider(new ModelServiceConnectionSettings
            {
                Enabled = true,
                Provider = chatOptions.Provider,
                Protocol = chatOptions.Protocol,
                FastModel = chatOptions.FastModel,
                ReasoningModel = chatOptions.ReasoningModel
            }));
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

    private sealed class FixedModelServiceConfigurationProvider(ModelServiceConnectionSettings settings)
        : IModelServiceConfigurationProvider
    {
        public ModelServiceConnectionSettings Current { get; } = settings;
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

    private sealed class KnowledgeSearchTool : IAnalysisTool
    {
        public bool Executed { get; private set; }
        public AnalysisToolDefinition Definition { get; } = new()
        {
            Name = "search_process_knowledge",
            Version = "v1",
            Description = "test",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                required = new[] { "query" },
                properties = new
                {
                    query = new { type = "string", minLength = 1, maxLength = 500 },
                    limit = new { type = "integer", minimum = 1, maximum = 20 }
                },
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
                Summary = "找到一条作业指导书记录。",
                Data = JsonSerializer.SerializeToElement(new { records = 1 }),
                RelatedRecords = [new RelatedRecordRef { Kind = "knowledge", Id = "1", Label = "作业指导书" }]
            });
        }
    }

    private sealed class NonReadTool : IAnalysisTool
    {
        public bool Executed { get; private set; }
        public AnalysisToolDefinition Definition { get; } = DefinitionFor("mutating_tool") with
        {
            Access = "write"
        };

        public Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default)
        {
            Executed = true;
            throw new InvalidOperationException("非只读工具不应执行。");
        }
    }

    private sealed class NoQueryModelClient : IModelClient
    {
        public CreateChatRunRequest? LastRequest { get; private set; }

        public string EntryPoint => ProductEntryPoints.Chat;

        public string Provider => "NoQuery";

        public string Model => "no-query-v1";

        public string ModelFor(ModelRole role) => Model;

        public Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
            CreateChatRunRequest request,
            IReadOnlyCollection<AnalysisToolDefinition> tools,
            CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new ModelCallResult<AnalysisPlan>
            {
                Value = new AnalysisPlan
                {
                    Intent = "conversation",
                    Summary = "用户未提出需要查询生产数据的问题。",
                    ToolCalls = []
                },
                Usage = Usage("intent.resolve")
            });
        }

        public Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
            CreateChatRunRequest request,
            AnalysisPlan plan,
            IReadOnlyList<AnalysisToolResult> results,
            CancellationToken ct = default)
            => throw new InvalidOperationException("无查询计划不应调用答案模型。");

        public Task<ModelCallResult<AnalysisAnswer>> ComposeConversationAsync(
            CreateChatRunRequest request,
            AnalysisPlan plan,
            CancellationToken ct = default)
            => Task.FromResult(new ModelCallResult<AnalysisAnswer>
            {
                Value = new AnalysisAnswer
                {
                    Summary = "你好，我可以继续这段对话。"
                },
                Usage = Usage("conversation.compose")
            });

        public Task<ModelCallResult<PerspectiveAnalysis>> ParticipateAsync(
            CombinedAnalysisTurn turn,
            CancellationToken ct = default)
            => throw new InvalidOperationException("无查询计划不应进入多视角分析。");

        private ModelCallUsage Usage(string operation) => new()
        {
            Provider = Provider,
            Model = Model,
            Operation = operation
        };
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

        public Task<IReadOnlyList<AgentRunSnapshot>> ListConversationAsync(
            string entryPoint,
            string userId,
            string conversationId,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentRunSnapshot>>(_runs.Values
                .Where(run => run.EntryPoint == entryPoint && run.UserId == userId &&
                              (run.ConversationId ?? run.RunId) == conversationId)
                .OrderBy(static run => run.CreatedAt)
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

        public Task<bool> DeleteConversationAsync(
            string entryPoint,
            string userId,
            string conversationId,
            CancellationToken ct = default)
        {
            var runIds = _runs.Values
                .Where(run => run.EntryPoint == entryPoint && run.UserId == userId &&
                              (run.ConversationId ?? run.RunId) == conversationId)
                .Select(static run => run.RunId)
                .ToArray();
            foreach (var runId in runIds)
            {
                _runs.TryRemove(runId, out _);
                _events.TryRemove(runId, out _);
            }
            return Task.FromResult(runIds.Length > 0);
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
