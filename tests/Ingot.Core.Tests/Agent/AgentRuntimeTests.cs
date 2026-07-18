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
    public async Task Run_Completes_WithWhitelistedToolAndEvidence()
    {
        var store = new MemoryRunStore();
        var model = new DeterministicModelClient();
        var runtime = CreateRuntime(store, model, [new QualityTool()]);

        var created = await runtime.StartAsync(ProductSurfaces.Chat, "ANALYST-001", new CreateAgentRunRequest
        {
            Question = "检查最近数据是否完整"
        });

        AgentRunSnapshot? completed = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            completed = await runtime.GetAsync(ProductSurfaces.Chat, created.RunId);
            if (completed is not null && AgentRunStatuses.IsTerminal(completed.Status))
                break;
            await Task.Delay(10);
        }

        Assert.NotNull(completed);
        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.NotNull(completed.Answer);
        Assert.Single(completed.Answer.Evidence);
        Assert.Equal("check_data_quality", Assert.Single(completed.ToolInvocations).Tool);
        Assert.Equal(2, completed.Usage.ModelCalls);
        Assert.Equal(1, completed.Usage.ToolCalls);
        Assert.Null(completed.Usage.EstimatedCost);
        var events = await store.ReadEventsAsync(created.RunId, 0, 100);
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.PlanCreated);
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.RunCompleted);
    }

    [Fact]
    public async Task ConnectorRun_RepairsBuildThenStopsAtApprovalGate()
    {
        var store = new MemoryRunStore();
        var model = new ConnectorLoopModelClient();
        var agentOptions = new AgentOptions
        {
            Enabled = true,
            MaxRunSeconds = 10,
            MaxIterations = 5,
            MaxToolCalls = 8
        };
        var runtime = CreateRuntime(
            store,
            model,
            [new BuildLoopTool(), new WriteLoopTool(), new TestLoopTool()],
            agentOptions: agentOptions);

        var created = await runtime.StartAsync(ProductSurfaces.Agent, "ENGINEER-001", new CreateAgentRunRequest
        {
            Question = "构建测试连接器并修复"
        });
        var completed = await WaitForTerminalAsync(runtime, ProductSurfaces.Agent, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.Equal(ConnectorWorkflowStages.AwaitingPackageApproval, completed.WorkflowStage);
        Assert.Equal(3, completed.Iteration);
        Assert.Equal(
            ["build_connector_workspace", "write_connector_workspace_file", "build_connector_workspace", "test_connector_workspace"],
            completed.ToolInvocations.Select(static item => item.Tool));
        var events = await store.ReadEventsAsync(created.RunId, 0, 100);
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.ApprovalRequired);
        Assert.DoesNotContain(completed.ToolInvocations, item => item.Tool == "package_connector_workspace");
    }

    [Fact]
    public async Task Investigation_IsolatesOneParticipantFailure()
    {
        var options = Options.Create(new ChatOptions { EnableMultiAgent = true, MaxDiscussionRounds = 1, MaxDiscussionTurns = 3 });
        var workflow = new BoundedInvestigationWorkflow(options);
        var events = new List<string>();
        var result = await workflow.RunAsync(
            new CreateAgentRunRequest { Question = "调查原因", Mode = "deep" },
            new AnalysisPlan { Intent = "investigate", Summary = "调查" },
            [new AnalysisToolResult
            {
                Tool = "check_data_quality",
                Summary = "数据完整。",
                Data = JsonSerializer.SerializeToElement(new { complete = true }),
                Evidence = [new EvidenceRef { Kind = "dataset", Id = "real", Label = "真实证据" }]
            }],
            new FailingParticipantModelClient(),
            (type, _, _) => { events.Add(type); return Task.CompletedTask; });

        Assert.Equal("candidate", result.Verdict.Status);
        Assert.Equal(2, result.Verdict.Transcript.Count);
        Assert.Equal(2, result.ModelCalls.Count);
        Assert.Contains(AgentStreamEventTypes.DiscussionParticipantFailed, events);
    }

    [Fact]
    public async Task CapabilitiesAndHistory_AreActorScoped()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, new DeterministicModelClient(), [new QualityTool()],
            chatOptions: new ChatOptions { Enabled = true, EnableMultiAgent = true });
        await store.CreateAsync(new AgentRunSnapshot
        {
            RunId = "other", ActorId = "OTHER", Surface = ProductSurfaces.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis, Question = "secret", Mode = "standard", Status = "completed",
            ModelProvider = "test", Model = "test", PromptVersion = "v", ToolsetVersion = "v", CreatedAt = DateTimeOffset.UtcNow,
            Usage = new AgentUsageSummary()
        });

        var page = await runtime.ListAsync(ProductSurfaces.Chat, "ANALYST-001", null, 20);
        Assert.Empty(page.Items);
        Assert.Contains("deep", runtime.GetCapabilities(ProductSurfaces.Chat).Modes);
        Assert.Equal(["check_data_quality"], runtime.GetCapabilities(ProductSurfaces.Chat).Tools.Select(static tool => tool.Name));
    }

    [Fact]
    public void Capabilities_StrictlySeparateChatAndDesktopAgentTools()
    {
        var runtime = CreateRuntime(
            new MemoryRunStore(),
            new DeterministicModelClient(),
            [new QualityTool(), new BuildLoopTool()],
            chatOptions: new ChatOptions { Enabled = true, EnableMultiAgent = true });

        var chat = runtime.GetCapabilities(ProductSurfaces.Chat);
        var agent = runtime.GetCapabilities(ProductSurfaces.Agent);

        Assert.Equal(RunPurposes.ReadOnlyAnalysis, chat.Purpose);
        Assert.Equal(["check_data_quality"], chat.Tools.Select(static tool => tool.Name));
        Assert.Contains("deep", chat.Modes);
        Assert.Equal(RunPurposes.ConnectorCodeGeneration, agent.Purpose);
        Assert.Equal(["build_connector_workspace"], agent.Tools.Select(static tool => tool.Name));
        Assert.Equal(["standard"], agent.Modes);
        Assert.Empty(agent.Roles);
    }

    [Fact]
    public void ModelRouter_SelectsTheClientForTheRequestedSurface()
    {
        var chat = new SurfaceModelClient(ProductSurfaces.Chat, "chat-model");
        var agent = new SurfaceModelClient(ProductSurfaces.Agent, "agent-model");
        var router = new DefaultModelRouter([agent, chat]);

        Assert.Same(chat, router.GetClient(ProductSurfaces.Chat, ModelRole.Fast));
        Assert.Same(agent, router.GetClient(ProductSurfaces.Agent, ModelRole.Reasoning));
    }

    [Fact]
    public async Task RunReadListAndCancel_AreSurfaceIsolated()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, new DeterministicModelClient(), [new QualityTool(), new BuildLoopTool()]);
        await store.CreateAsync(Snapshot("chat-run", ProductSurfaces.Chat, "operator"));
        await store.CreateAsync(Snapshot("agent-run", ProductSurfaces.Agent, "operator"));

        Assert.Null(await runtime.GetAsync(ProductSurfaces.Chat, "agent-run"));
        Assert.Null(await runtime.GetAsync(ProductSurfaces.Agent, "chat-run"));
        Assert.Equal(["chat-run"], (await runtime.ListAsync(
            ProductSurfaces.Chat, "operator", null, 20)).Items.Select(static run => run.RunId));
        Assert.Equal(["agent-run"], (await runtime.ListAsync(
            ProductSurfaces.Agent, "operator", null, 20)).Items.Select(static run => run.RunId));
        Assert.False(await runtime.CancelAsync(
            ProductSurfaces.Chat, "agent-run", "operator", "cross-surface"));
    }

    [Fact]
    public async Task ChatRejectsAPlanThatSelectsDesktopCodeTool()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(
            store,
            new CrossSurfaceModelClient(),
            [new QualityTool(), new BuildLoopTool()]);

        var created = await runtime.StartAsync(
            ProductSurfaces.Chat,
            "analyst",
            new CreateAgentRunRequest { Question = "尝试生成连接器代码" });
        var completed = await WaitForTerminalAsync(runtime, ProductSurfaces.Chat, created.RunId);

        Assert.Equal(AgentRunStatuses.Failed, completed.Status);
        Assert.Contains("未授权工具", completed.Error, StringComparison.Ordinal);
        Assert.Empty(completed.ToolInvocations);
    }

    [Fact]
    public async Task Run_AggregatesActualModelUsageAndConfiguredCost()
    {
        var store = new MemoryRunStore();
        var chatOptions = new ChatOptions
        {
            Enabled = true,
            ModelPricing = new Dictionary<string, ModelPricingOptions>
            {
                ["counting-v1"] = new() { InputPerMillionTokens = 10m, OutputPerMillionTokens = 20m }
            }
        };
        var model = new CountingModelClient();
        var runtime = CreateRuntime(store, model, [new QualityTool()], chatOptions: chatOptions);
        var created = await runtime.StartAsync(ProductSurfaces.Chat, "ANALYST-001", new CreateAgentRunRequest { Question = "检查数据质量" });
        AgentRunSnapshot? completed = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            completed = await runtime.GetAsync(ProductSurfaces.Chat, created.RunId);
            if (completed is not null && AgentRunStatuses.IsTerminal(completed.Status)) break;
            await Task.Delay(10);
        }

        Assert.NotNull(completed);
        Assert.Equal(200, completed.Usage.InputTokens);
        Assert.Equal(100, completed.Usage.OutputTokens);
        Assert.Equal(0.004m, completed.Usage.EstimatedCost);
    }

    [Fact]
    public async Task DeepRun_UsesBoundedMultiAgentDiscussion()
    {
        var store = new MemoryRunStore();
        var model = new DeterministicModelClient();
        var chatOptions = new ChatOptions
        {
            Enabled = true,
            EnableMultiAgent = true,
            MaxRunSeconds = 10,
            MaxDiscussionRounds = 2,
            MaxDiscussionTurns = 6
        };
        var runtime = CreateRuntime(store, model, [new QualityTool()], chatOptions: chatOptions);

        var created = await runtime.StartAsync(ProductSurfaces.Chat, "ANALYST-001", new CreateAgentRunRequest
        {
            Question = "调查这次异常可能与哪些变化有关",
            Mode = "deep"
        });
        AgentRunSnapshot? completed = null;
        for (var attempt = 0; attempt < 100; attempt++)
        {
            completed = await runtime.GetAsync(ProductSurfaces.Chat, created.RunId);
            if (completed is not null && AgentRunStatuses.IsTerminal(completed.Status))
                break;
            await Task.Delay(10);
        }

        Assert.NotNull(completed?.Answer?.Investigation);
        Assert.Equal("candidate", completed.Answer.Investigation.Status);
        Assert.Equal(6, completed.Answer.Investigation.Transcript.Count);
        Assert.All(completed.Answer.Investigation.Evidence, item => Assert.Equal("test-1", item.Id));
        var events = await store.ReadEventsAsync(created.RunId, 0, 100);
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.DiscussionStarted);
        Assert.Equal(6, events.Count(item => item.Type == AgentStreamEventTypes.DiscussionMessage));
        Assert.Contains(events, item => item.Type == AgentStreamEventTypes.DiscussionCompleted);
    }

    [Fact]
    public async Task DeepRun_IsRejectedWhenMultiAgentIsDisabled()
    {
        var runtime = CreateRuntime(
            new MemoryRunStore(),
            new DeterministicModelClient(),
            [new QualityTool()],
            chatOptions: new ChatOptions { Enabled = true, EnableMultiAgent = false });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync(
            ProductSurfaces.Chat,
            "ANALYST-001",
            new CreateAgentRunRequest { Question = "调查原因", Mode = "deep" }));

        Assert.Contains("尚未启用", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InsufficientData_ReturnsDeterministicRefusalWithoutFindings()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, new DeterministicModelClient(), [new InsufficientQualityTool()]);

        var created = await runtime.StartAsync(
            ProductSurfaces.Chat,
            "ANALYST-001",
            new CreateAgentRunRequest { Question = "检查数据质量" });
        var completed = await WaitForTerminalAsync(runtime, ProductSurfaces.Chat, created.RunId);

        Assert.Equal(AgentRunStatuses.Completed, completed.Status);
        Assert.NotNull(completed.Answer);
        Assert.Empty(completed.Answer.Findings);
        Assert.Empty(completed.Answer.Charts);
        Assert.Null(completed.Answer.Investigation);
        Assert.NotEmpty(completed.Answer.Limitations);
        Assert.Equal(1, completed.Usage.ModelCalls);
    }

    [Fact]
    public void EvidenceVerifier_RequiresExactNumbersAndRejectsEnglishCausality()
    {
        var verifier = new DefaultEvidenceVerifier();
        var result = new AnalysisToolResult
        {
            Tool = "check_data_quality",
            Summary = "Checked 500 events.",
            Data = JsonSerializer.SerializeToElement(new { eventCount = 500 }),
            Evidence = [new EvidenceRef { Kind = "dataset", Id = "events", Label = "Events" }]
        };

        Assert.False(verifier.TryVerifyAnswer(
            new AnalysisAnswer { Summary = "Checked 50 events." }, [result], out var numberError));
        Assert.Contains("50", numberError, StringComparison.Ordinal);
        Assert.True(verifier.TryVerifyAnswer(
            new AnalysisAnswer { Summary = "Checked 5e2 events." }, [result], out var normalizedError),
            normalizedError);
        Assert.False(verifier.TryVerifyAnswer(
            new AnalysisAnswer { Summary = "The change directly caused the failure." }, [result], out var causeError));
        Assert.Contains("因果", causeError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_MarksPersistedRunWhenNoWorkerIsActive()
    {
        var store = new MemoryRunStore();
        var runtime = CreateRuntime(store, new DeterministicModelClient(), [new QualityTool()]);
        await store.CreateAsync(new AgentRunSnapshot
        {
            RunId = "stale-run",
            ActorId = "operator",
            Surface = ProductSurfaces.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Question = "stale",
            Mode = "standard",
            Status = AgentRunStatuses.Running,
            ModelProvider = "test",
            Model = "test",
            PromptVersion = "v1",
            ToolsetVersion = "v1",
            CreatedAt = DateTimeOffset.UtcNow,
            Usage = new AgentUsageSummary()
        });

        Assert.True(await runtime.CancelAsync(ProductSurfaces.Chat, "stale-run", "OPERATOR", "cancelled"));
        var cancelled = await store.GetAsync("stale-run");
        Assert.Equal(AgentRunStatuses.Cancelled, cancelled!.Status);
        Assert.Equal("cancelled", cancelled.CancellationReason);
        Assert.NotNull(cancelled.CompletedAt);
    }

    [Fact]
    public async Task Investigation_DropsForgedEvidenceAndCausalClaims()
    {
        var options = Options.Create(new ChatOptions
        {
            EnableMultiAgent = true,
            MaxDiscussionRounds = 1,
            MaxDiscussionTurns = 3
        });
        var workflow = new BoundedInvestigationWorkflow(options);
        var result = new AnalysisToolResult
        {
            Tool = "check_data_quality",
            Summary = "数据完整。",
            Data = JsonSerializer.SerializeToElement(new { complete = true }),
            Evidence = [new EvidenceRef { Kind = "dataset", Id = "real", Label = "真实证据" }]
        };

        var verdict = await workflow.RunAsync(
            new CreateAgentRunRequest { Question = "调查原因", Mode = "deep" },
            new AnalysisPlan { Intent = "investigate", Summary = "调查" },
            [result],
            new MaliciousModelClient(),
            (_, _, _) => Task.CompletedTask);

        Assert.Equal("insufficient-data", verdict.Verdict.Status);
        Assert.Empty(verdict.Verdict.Hypotheses);
        Assert.All(verdict.Verdict.Transcript, item => Assert.Contains(item.Role, InvestigationRoles.All));
    }

    [Fact]
    public void PlanValidator_RejectsUnregisteredTool()
    {
        var validator = CreatePlanValidator();
        var valid = validator.TryValidate(
            ProductSurfaces.Chat,
            new AnalysisPlan
            {
                Intent = "unsafe",
                Summary = "unsafe",
                ToolCalls = [new AnalysisToolCall { Tool = "execute_sql" }]
            },
            new Dictionary<string, IAnalysisTool>(),
            out var error);

        Assert.False(valid);
        Assert.Contains("未授权工具", error);
    }

    [Fact]
    public void PlanValidator_EnforcesDeclaredStringSchema()
    {
        var validator = CreatePlanValidator();
        var tool = new SchemaTool();
        var tools = new Dictionary<string, IAnalysisTool> { [tool.Definition.Name] = tool };

        Assert.False(Validate(new Dictionary<string, string?> { ["mode"] = "fast" }, out var missingError));
        Assert.Contains("correlationId", missingError, StringComparison.Ordinal);

        Assert.False(Validate(new Dictionary<string, string?>
        {
            ["correlationId"] = "a",
            ["mode"] = "fast"
        }, out var shortError));
        Assert.Contains("长度", shortError, StringComparison.Ordinal);

        Assert.False(Validate(new Dictionary<string, string?>
        {
            ["correlationId"] = "cycle-001",
            ["mode"] = "unsafe"
        }, out var enumError));
        Assert.Contains("允许值", enumError, StringComparison.Ordinal);

        Assert.False(Validate(new Dictionary<string, string?>
        {
            ["correlationId"] = "cycle-001",
            ["mode"] = "fast",
            ["sql"] = "select 1"
        }, out var additionalError));
        Assert.Contains("未声明参数", additionalError, StringComparison.Ordinal);

        Assert.True(Validate(new Dictionary<string, string?>
        {
            ["correlationId"] = "cycle-001",
            ["mode"] = "deep"
        }, out var validError), validError);

        bool Validate(IReadOnlyDictionary<string, string?> arguments, out string error)
            => validator.TryValidate(
                ProductSurfaces.Chat,
                new AnalysisPlan
                {
                    Intent = "trace",
                    Summary = "trace",
                    ToolCalls = [new AnalysisToolCall { Tool = tool.Definition.Name, Arguments = arguments }]
                },
                tools,
                out error);
    }

    [Fact]
    public void EvidenceVerifier_EnforcesChartWhitelistShapeAndFiniteValues()
    {
        var verifier = new DefaultEvidenceVerifier();
        var result = new AnalysisToolResult
        {
            Tool = "check_data_quality",
            Summary = "Data is available.",
            Data = JsonSerializer.SerializeToElement(new { values = new[] { 1, 2 } }),
            Evidence = [new EvidenceRef { Kind = "dataset", Id = "events", Label = "Events" }]
        };
        var validChart = new ChartSpec
        {
            Type = "line",
            Title = "Cycle duration",
            Labels = ["A", "B"],
            Series = [new ChartSeries { Name = "duration", Values = [1, 2] }]
        };

        Assert.True(verifier.TryVerifyAnswer(
            new AnalysisAnswer { Summary = "Data is available.", Charts = [validChart] },
            [result],
            out var validError), validError);

        Assert.False(verifier.TryVerifyAnswer(
            new AnalysisAnswer { Summary = "Data is available.", Charts = [validChart with { Type = "javascript" }] },
            [result],
            out var typeError));
        Assert.Contains("白名单", typeError, StringComparison.Ordinal);

        Assert.False(verifier.TryVerifyAnswer(
            new AnalysisAnswer
            {
                Summary = "Data is available.",
                Charts = [validChart with
                {
                    Series = [new ChartSeries { Name = "duration", Values = [1] }]
                }]
            },
            [result],
            out var shapeError));
        Assert.Contains("标签数量", shapeError, StringComparison.Ordinal);

        Assert.False(verifier.TryVerifyAnswer(
            new AnalysisAnswer
            {
                Summary = "Data is available.",
                Charts = [validChart with
                {
                    Series = [new ChartSeries { Name = "duration", Values = [1, double.PositiveInfinity] }]
                }]
            },
            [result],
            out var finiteError));
        Assert.Contains("非有限", finiteError, StringComparison.Ordinal);

        Assert.False(verifier.TryVerifyAnswer(
            new AnalysisAnswer
            {
                Summary = "Data is available.",
                Charts = [validChart with
                {
                    Series = [new ChartSeries { Name = "duration", Values = [1, 3] }]
                }]
            },
            [result],
            out var forgedError));
        Assert.Contains("无法支持", forgedError, StringComparison.Ordinal);

        Assert.True(verifier.TryVerifyAnswer(
            new AnalysisAnswer { Summary = "Observed 1.0 and 2e0.", Charts = [validChart] },
            [result],
            out var normalizedError),
            normalizedError);
    }

    [Theory]
    [InlineData("", "standard")]
    [InlineData("有效问题", "unsafe")]
    public void ContractValidator_RejectsInvalidRequest(string question, string mode)
    {
        var valid = AgentContractValidator.TryValidate(
            new CreateAgentRunRequest { Question = question, Mode = mode },
            out _,
            out _);
        Assert.False(valid);
    }

    [Fact]
    public void Contracts_AllowDeepOnlyForChat()
    {
        Assert.False(AgentContractValidator.TryValidate(
            new CreateAgentRunRequest { Question = "生成连接器", Mode = "deep" }, out _, out _));
        Assert.True(AgentContractValidator.TryValidate(
            new CreateChatRunRequest { Question = "深入查找问题", Mode = "deep" }, out _, out _));
    }

    private sealed class QualityTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition { get; } = new()
        {
            Name = "check_data_quality",
            Version = "test",
            Surface = ProductSurfaces.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Description = "test",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        public Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default)
            => Task.FromResult(new AnalysisToolResult
            {
                Tool = Definition.Name,
                Summary = "数据完整。",
                Data = JsonSerializer.SerializeToElement(new { complete = true }),
                Evidence = [new EvidenceRef { Kind = "dataset", Id = "test-1", Label = "测试数据集" }]
            });
    }

    private sealed class SchemaTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition { get; } = new()
        {
            Name = "schema_tool",
            Version = "test",
            Surface = ProductSurfaces.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Description = "test",
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                required = new[] { "correlationId", "mode" },
                properties = new
                {
                    correlationId = new { type = "string", minLength = 2, maxLength = 20 },
                    mode = new { type = "string", @enum = new[] { "fast", "deep" } }
                },
                additionalProperties = false
            })
        };

        public Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class InsufficientQualityTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition { get; } = new()
        {
            Name = "check_data_quality",
            Version = "test",
            Surface = ProductSurfaces.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Description = "test",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        public Task<AnalysisToolResult> ExecuteAsync(
            AnalysisToolCall call,
            AgentExecutionContext context,
            CancellationToken ct = default)
            => Task.FromResult(new AnalysisToolResult
            {
                Tool = Definition.Name,
                Summary = "当前范围没有生产事件，无法确认数据质量。",
                Data = JsonSerializer.SerializeToElement(new { eventCount = 0 }),
                Evidence = [new EvidenceRef { Kind = "event-query", Id = "empty", Label = "空查询范围" }],
                Limitations = ["当前范围没有生产事件。"],
                Outcome = AnalysisToolOutcomes.InsufficientData
            });
    }

    private sealed class ConnectorLoopModelClient : IModelClient
    {
        public string Provider => "test";
        public string Model => "connector-loop";
        public bool SupportsToolContinuation => true;

        public Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
            CreateAgentRunRequest request,
            IReadOnlyCollection<AnalysisToolDefinition> tools,
            CancellationToken ct = default)
            => Task.FromResult(Result(new AnalysisPlan
            {
                Intent = "connector-build",
                Summary = "先构建。",
                ToolCalls = [Call("build_connector_workspace")]
            }, "intent.resolve"));

        public Task<ModelCallResult<AgentContinuation>> ContinueAsync(
            AgentContinuationContext context,
            IReadOnlyCollection<AnalysisToolDefinition> tools,
            CancellationToken ct = default)
            => Task.FromResult(Result(new AgentContinuation
            {
                Summary = context.Iteration == 1 ? "修复并重建。" : "运行测试。",
                ToolCalls = context.Iteration == 1
                    ? [Call("write_connector_workspace_file"), Call("build_connector_workspace")]
                    : [Call("test_connector_workspace")]
            }, "tools.continue"));

        public Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
            CreateAgentRunRequest request,
            AnalysisPlan plan,
            IReadOnlyList<AnalysisToolResult> results,
            CancellationToken ct = default)
            => Task.FromResult(Result(new AnalysisAnswer
            {
                Summary = "连接器测试通过，等待操作者批准。",
                Findings = ["连接器测试通过，等待操作者批准。"]
            }, "answer.compose"));

        public Task<ModelCallResult<InvestigationContribution>> ParticipateAsync(
            InvestigationTurn turn,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        private static AnalysisToolCall Call(string tool) => new()
        {
            Tool = tool,
            Arguments = new Dictionary<string, string?> { ["workspaceId"] = "workspace-test" }
        };

        private ModelCallResult<T> Result<T>(T value, string operation) => new()
        {
            Value = value,
            Usage = new ModelCallUsage { Provider = Provider, Model = Model, Operation = operation }
        };
    }

    private sealed class BuildLoopTool : IAnalysisTool
    {
        private int _calls;
        public AnalysisToolDefinition Definition => LoopDefinition("build_connector_workspace");
        public Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
        {
            var succeeded = Interlocked.Increment(ref _calls) > 1;
            return Task.FromResult(LoopResult(Definition.Name,
                succeeded ? "连接器构建成功。" : "连接器构建失败，需要修复。", succeeded));
        }
    }

    private sealed class WriteLoopTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition => LoopDefinition("write_connector_workspace_file");
        public Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
            => Task.FromResult(LoopResult(Definition.Name, "已修复源码。", true));
    }

    private sealed class TestLoopTool : IAnalysisTool
    {
        public AnalysisToolDefinition Definition => LoopDefinition("test_connector_workspace");
        public Task<AnalysisToolResult> ExecuteAsync(AnalysisToolCall call, AgentExecutionContext context, CancellationToken ct = default)
            => Task.FromResult(LoopResult(Definition.Name, "连接器测试通过，等待操作者批准。", true));
    }

    private static AnalysisToolDefinition LoopDefinition(string name) => new()
    {
        Name = name,
        Version = "test",
        Surface = ProductSurfaces.Agent,
        Purpose = RunPurposes.ConnectorCodeGeneration,
        Description = "test",
        InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
    };

    private static AnalysisToolResult LoopResult(string tool, string summary, bool succeeded) => new()
    {
        Tool = tool,
        Summary = summary,
        Data = JsonSerializer.SerializeToElement(new { result = new { succeeded } }),
        Evidence = [new EvidenceRef { Kind = "connector-workspace", Id = "workspace-test", Label = "测试工作区" }]
    };

    private static AgentRunSnapshot Snapshot(string runId, string surface, string actorId) => new()
    {
        RunId = runId,
        ActorId = actorId,
        Surface = surface,
        Purpose = RunPurposes.ForSurface(surface),
        Question = "test",
        Mode = "standard",
        Status = AgentRunStatuses.Running,
        ModelProvider = "test",
        Model = "test",
        PromptVersion = "test",
        ToolsetVersion = "test",
        CreatedAt = DateTimeOffset.UtcNow,
        Usage = new AgentUsageSummary()
    };

    private static async Task<AgentRunSnapshot> WaitForTerminalAsync(
        IAgentRuntime runtime,
        string surface,
        string runId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var snapshot = await runtime.GetAsync(surface, runId);
            if (snapshot is not null && AgentRunStatuses.IsTerminal(snapshot.Status))
                return snapshot;
            await Task.Delay(10);
        }
        throw new TimeoutException("Agent run did not finish.");
    }

    private static AgentRuntime CreateRuntime(
        IAgentRunStore store,
        IModelClient model,
        IReadOnlyList<IAnalysisTool> tools,
        AgentOptions? agentOptions = null,
        ChatOptions? chatOptions = null)
    {
        agentOptions ??= new AgentOptions { Enabled = true, MaxRunSeconds = 10 };
        chatOptions ??= new ChatOptions { Enabled = true, MaxRunSeconds = 10 };
        var agent = Options.Create(agentOptions);
        var chat = Options.Create(chatOptions);
        return new AgentRuntime(
            store,
            new DefaultModelRouter([model]),
            tools,
            new DefaultPlanValidator(agent, chat),
            new DefaultEvidenceVerifier(),
            new BoundedInvestigationWorkflow(chat),
            agent,
            chat,
            NullLogger<AgentRuntime>.Instance);
    }

    private static DefaultPlanValidator CreatePlanValidator()
        => new(Options.Create(new AgentOptions()), Options.Create(new ChatOptions()));

    private sealed class CrossSurfaceModelClient : IModelClient
    {
        public string Provider => "Deterministic";

        public string Model => "cross-surface-test";

        public Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
            CreateAgentRunRequest request,
            IReadOnlyCollection<AnalysisToolDefinition> tools,
            CancellationToken ct = default)
            => Task.FromResult(Result(new AnalysisPlan
            {
                Intent = "escape",
                Summary = "request a desktop tool",
                ToolCalls = [new AnalysisToolCall { Tool = "build_connector_workspace" }]
            }, "intent.resolve"));

        public Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
            CreateAgentRunRequest request,
            AnalysisPlan plan,
            IReadOnlyList<AnalysisToolResult> results,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ModelCallResult<InvestigationContribution>> ParticipateAsync(
            InvestigationTurn turn,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        private ModelCallResult<T> Result<T>(T value, string operation) => new()
        {
            Value = value,
            Usage = new ModelCallUsage { Provider = Provider, Model = Model, Operation = operation }
        };
    }

    private sealed class SurfaceModelClient(string surface, string model) : IModelClient
    {
        public string Surface => surface;

        public string Provider => "test";

        public string Model => model;

        public Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
            CreateAgentRunRequest request,
            IReadOnlyCollection<AnalysisToolDefinition> tools,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
            CreateAgentRunRequest request,
            AnalysisPlan plan,
            IReadOnlyList<AnalysisToolResult> results,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ModelCallResult<InvestigationContribution>> ParticipateAsync(
            InvestigationTurn turn,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class MaliciousModelClient : IModelClient
    {
        public string Provider => "test";

        public string Model => "malicious";

        public Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
            CreateAgentRunRequest request,
            IReadOnlyCollection<AnalysisToolDefinition> tools,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
            CreateAgentRunRequest request,
            AnalysisPlan plan,
            IReadOnlyList<AnalysisToolResult> results,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ModelCallResult<InvestigationContribution>> ParticipateAsync(
            InvestigationTurn turn,
            CancellationToken ct = default)
            => Task.FromResult(new ModelCallResult<InvestigationContribution>
            {
                Usage = new ModelCallUsage
                {
                    Provider = Provider,
                    Model = Model,
                    Operation = "investigation.participate"
                },
                Value = new InvestigationContribution
                {
                    Role = "administrator",
                    Round = 99,
                    Summary = "尝试越过协调器。",
                    Hypotheses =
                    [
                        new InvestigationHypothesis
                        {
                            HypothesisId = "forged",
                            AuthorRole = "administrator",
                            Statement = "可能与某项变化有关。",
                            Rationale = "引用不存在的数据。",
                            Evidence = [new EvidenceRef { Kind = "dataset", Id = "forged", Label = "伪造证据" }]
                        },
                        new InvestigationHypothesis
                        {
                            HypothesisId = "causal",
                            AuthorRole = "administrator",
                            Statement = "该变化直接导致异常。",
                            Rationale = "已证明因果。",
                            Evidence = [new EvidenceRef { Kind = "dataset", Id = "real", Label = "真实证据" }]
                        }
                    ]
                }
            });
    }

    private sealed class FailingParticipantModelClient : IModelClient
    {
        private readonly DeterministicModelClient _inner = new();
        public string Provider => _inner.Provider;
        public string Model => _inner.Model;
        public Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(CreateAgentRunRequest request, IReadOnlyCollection<AnalysisToolDefinition> tools, CancellationToken ct = default)
            => _inner.ResolveIntentAsync(request, tools, ct);
        public Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(CreateAgentRunRequest request, AnalysisPlan plan, IReadOnlyList<AnalysisToolResult> results, CancellationToken ct = default)
            => _inner.ComposeAnswerAsync(request, plan, results, ct);
        public Task<ModelCallResult<InvestigationContribution>> ParticipateAsync(InvestigationTurn turn, CancellationToken ct = default)
            => turn.Role == InvestigationRoles.Skeptic
                ? Task.FromException<ModelCallResult<InvestigationContribution>>(new TimeoutException("unavailable"))
                : _inner.ParticipateAsync(turn, ct);
    }

    private sealed class CountingModelClient : IModelClient
    {
        private readonly DeterministicModelClient _inner = new();
        public string Provider => "test";
        public string Model => "counting-v1";
        public async Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(CreateAgentRunRequest request, IReadOnlyCollection<AnalysisToolDefinition> tools, CancellationToken ct = default)
            => Count((await _inner.ResolveIntentAsync(request, tools, ct)).Value, "intent.resolve");
        public async Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(CreateAgentRunRequest request, AnalysisPlan plan, IReadOnlyList<AnalysisToolResult> results, CancellationToken ct = default)
            => Count((await _inner.ComposeAnswerAsync(request, plan, results, ct)).Value, "answer.compose");
        public async Task<ModelCallResult<InvestigationContribution>> ParticipateAsync(InvestigationTurn turn, CancellationToken ct = default)
            => Count((await _inner.ParticipateAsync(turn, ct)).Value, "investigation.participate");
        private ModelCallResult<T> Count<T>(T value, string operation) => new()
        {
            Value = value,
            Usage = new ModelCallUsage
            {
                Provider = Provider, Model = Model, Operation = operation,
                InputTokens = 100, OutputTokens = 50, DurationMilliseconds = 5
            }
        };
    }

    private sealed class MemoryRunStore : IAgentRunStore
    {
        private readonly ConcurrentDictionary<string, AgentRunSnapshot> _runs = new();
        private readonly ConcurrentDictionary<string, List<AgentStreamEvent>> _events = new();
        private long _sequence;

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task CreateAsync(AgentRunSnapshot run, CancellationToken ct = default)
        {
            _runs[run.RunId] = run;
            return Task.CompletedTask;
        }

        public Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default)
            => Task.FromResult(_runs.GetValueOrDefault(runId));

        public Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(
            string surface,
            string actorId,
            DateTimeOffset? before,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AgentRunSnapshot>>(_runs.Values
                .Where(run => run.ActorId == actorId && run.Surface == surface &&
                              (!before.HasValue || run.CreatedAt < before))
                .OrderByDescending(static run => run.CreatedAt)
                .Take(limit)
                .ToArray());

        public Task UpdateAsync(AgentRunSnapshot run, CancellationToken ct = default)
        {
            _runs[run.RunId] = run;
            return Task.CompletedTask;
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
            var list = _events.GetOrAdd(runId, static _ => []);
            lock (list)
                list.Add(item);
            return Task.FromResult(item);
        }

        public Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(
            string runId,
            long afterSequence,
            int limit,
            CancellationToken ct = default)
        {
            if (!_events.TryGetValue(runId, out var list))
                return Task.FromResult<IReadOnlyList<AgentStreamEvent>>([]);
            lock (list)
                return Task.FromResult<IReadOnlyList<AgentStreamEvent>>(
                    list.Where(item => item.Sequence > afterSequence).Take(limit).ToArray());
        }
    }
}
