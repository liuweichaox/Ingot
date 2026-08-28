// 编排受限 Agent 运行、持久化快照、工具调用、取消和并发预算。
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Agent;

/// <summary>编排 Agent 运行、持久化状态、执行只读工具并实施并发与时限预算。</summary>
public sealed class AgentRuntime : IAgentRuntime, IAgentRunProcessor
{
    private const string ChatPromptVersion = "ingot-chat-v1";
    private const string ChatToolsetVersion = "production-records-readonly-v2";
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new(StringComparer.Ordinal);
    private readonly object _admissionGate = new();
    private readonly Dictionary<string, int> _activeByUser = new(StringComparer.OrdinalIgnoreCase);
    private int _activeCount;
    private readonly IAnalysisResultValidator _relatedRecordsVerifier;
    private readonly ILogger<AgentRuntime> _logger;
    private readonly ICombinedAnalysisWorkflow _investigationWorkflow;
    private readonly IAgentRunLifecycleSink _lifecycleSink;
    private readonly IModelRouter _models;
    private readonly IModelServiceConfigurationProvider _modelSettings;
    private readonly ChatOptions _chatOptions;
    private readonly IPlanValidator _planValidator;
    private readonly IAgentRunStore _store;
    private readonly IReadOnlyDictionary<string, IAnalysisTool> _tools;

    public AgentRuntime(
        IAgentRunStore store,
        IModelRouter models,
        IEnumerable<IAnalysisTool> tools,
        IPlanValidator planValidator,
        IAnalysisResultValidator relatedRecordsVerifier,
        ICombinedAnalysisWorkflow investigationWorkflow,
        IAgentRunLifecycleSink lifecycleSink,
        IOptions<ChatOptions> chatOptions,
        ILogger<AgentRuntime> logger,
        IModelServiceConfigurationProvider? modelSettings = null)
    {
        _store = store;
        _models = models;
        _planValidator = planValidator;
        _relatedRecordsVerifier = relatedRecordsVerifier;
        _investigationWorkflow = investigationWorkflow;
        _lifecycleSink = lifecycleSink;
        _chatOptions = chatOptions.Value;
        _modelSettings = modelSettings ?? new DeploymentModelServiceConfigurationProvider(chatOptions);
        _logger = logger;
        _tools = tools.ToDictionary(static tool => tool.Definition.Name, StringComparer.Ordinal);
    }

    public async Task<AgentRunSnapshot> StartAsync(
        string entryPoint,
        string userId,
        CreateChatRunRequest request,
        AgentAccessScope accessScope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accessScope);
        ValidateEntryPoint(entryPoint);
        var settings = GetSettings();
        if (!settings.Enabled)
            throw new InvalidOperationException("Chat 功能尚未启用。");
        if (string.Equals(request.Mode, "combined", StringComparison.Ordinal) &&
            (!settings.EnableCombinedAnalysis || IsDeterministic(settings)))
            throw new InvalidOperationException("多视角研判尚未启用。");

        var tools = ToolsForEntryPoint(entryPoint);
        if (tools.Count == 0)
            throw new InvalidOperationException($"{entryPoint} 没有已注册工具。");

        var conversationId = request.ConversationId;
        IReadOnlyList<AgentRunSnapshot> priorRuns = [];
        if (conversationId is not null)
        {
            priorRuns = await _store.ListConversationAsync(entryPoint, userId, conversationId, 100, ct)
                .ConfigureAwait(false);
            var isFormalFirstTurn = priorRuns.Count == 0 &&
                                    !string.IsNullOrWhiteSpace(request.TriggerMessageId) &&
                                    !string.IsNullOrWhiteSpace(request.ResponseMessageId);
            if (priorRuns.Count == 0 && !isFormalFirstTurn)
                throw new InvalidOperationException("要继续的对话不存在。");
            if (priorRuns.Any(static run => !AgentRunStatuses.IsTerminal(run.Status)))
                throw new InvalidOperationException("当前对话仍在分析，请等待完成或先停止分析。");
            if (priorRuns.Any(run => !CanReuseCapturedScope(run.AccessScope, accessScope)))
                throw new UnauthorizedAccessException("当前身份已无权继续访问该对话的历史数据范围。");
            if (priorRuns.Any(run => !SamePageContext(run.PageContext, request.PageContext)))
                throw new InvalidOperationException("不能在不同页面上下文中继续同一对话。");

            request = request with
            {
                ConversationHistory = priorRuns
                    .Where(static run => run.Status == AgentRunStatuses.Completed && run.Answer is not null)
                    .TakeLast(10)
                    .Select(static run => new ChatConversationContextTurn
                    {
                        Question = run.Question,
                        Summary = run.Answer!.Summary,
                        Findings = run.Answer.Findings
                            .Select(static finding => finding.Statement)
                            .Take(8)
                            .ToArray(),
                        Limitations = run.Answer.Limitations.Take(5).ToArray()
                    })
                    .ToArray()
            };
        }

        var modelRole = request.Mode == "combined" ? ModelRole.Reasoning : ModelRole.Fast;
        var model = _models.GetClient(entryPoint, modelRole, settings.Provider);
        if (!string.Equals(model.Provider, settings.Provider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{entryPoint} 配置的模型 Provider 没有对应客户端。");
        ReserveRun(userId, settings);
        var admitted = true;
        var runId = Guid.CreateVersion7().ToString();
        var run = new AgentRunSnapshot
        {
            RunId = runId,
            ConversationId = conversationId ?? runId,
            TriggerMessageId = request.TriggerMessageId,
            ResponseMessageId = request.ResponseMessageId,
            UserId = userId,
            EntryPoint = entryPoint,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Question = request.Question,
            PageContext = request.PageContext,
            AccessScope = SnapshotAccessScope(accessScope),
            Mode = request.Mode,
            Status = AgentRunStatuses.Queued,
            ModelProvider = model.Provider,
            Model = model.ModelFor(modelRole),
            PromptVersion = ChatPromptVersion,
            ToolsetVersion = ChatToolsetVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            WorkflowStage = "analysis",
            Usage = new AgentUsageSummary()
        };

        try
        {
            await _store.CreateAsync(run, ct).ConfigureAwait(false);

            if (_store is IDurableAgentRunStore)
                return run;

            var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.MaxRunSeconds, 1, 900));
            var runCts = new CancellationTokenSource(timeout);
            if (!_active.TryAdd(run.RunId, runCts))
            {
                runCts.Dispose();
                throw new InvalidOperationException("无法注册 Chat 运行。");
            }
            _ = ExecuteAsync(run, request, accessScope, model, tools, settings, runCts.Token);
            admitted = false;
            return run;
        }
        finally
        {
            if (admitted)
                ReleaseRun(userId);
        }
    }

    public async Task<bool> ProcessNextAsync(string leaseOwner, CancellationToken ct = default)
    {
        if (_store is not IDurableAgentRunStore durableStore)
            return false;
        var settings = GetSettings();
        if (!settings.Enabled)
            return false;
        var leaseDuration = TimeSpan.FromSeconds(Math.Clamp(settings.MaxRunSeconds + 60, 90, 960));
        var claimed = await durableStore.ClaimNextAsync(leaseOwner, leaseDuration, ct).ConfigureAwait(false);
        if (claimed is null)
            return false;

        var reserved = false;
        var executionOwnsAdmission = false;
        try
        {
            ReserveRun(claimed.UserId, settings);
            reserved = true;
            var tools = ToolsForEntryPoint(claimed.EntryPoint);
            var modelRole = claimed.Mode == "combined" ? ModelRole.Reasoning : ModelRole.Fast;
            var model = _models.GetClient(claimed.EntryPoint, modelRole, settings.Provider);
            var request = new CreateChatRunRequest
            {
                Question = claimed.Question,
                ConversationId = claimed.ConversationId,
                PageContext = claimed.PageContext,
                Mode = claimed.Mode,
                TriggerMessageId = claimed.TriggerMessageId,
                ResponseMessageId = claimed.ResponseMessageId,
                ConversationHistory = await BuildConversationHistoryAsync(claimed, ct).ConfigureAwait(false)
            };
            var accessScope = RestoreAccessScope(claimed.AccessScope);
            var run = claimed with
            {
                ModelProvider = model.Provider,
                Model = model.ModelFor(modelRole)
            };
            using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            runCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.MaxRunSeconds, 1, 900)));
            if (!_active.TryAdd(run.RunId, runCts))
                throw new InvalidOperationException("无法注册持久 Chat 运行。");
            using var monitorStop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var monitor = MonitorLeaseAndCancellationAsync(
                durableStore, run.RunId, leaseOwner, leaseDuration, runCts, monitorStop.Token);
            try
            {
                executionOwnsAdmission = true;
                await ExecuteAsync(run, request, accessScope, model, tools, settings, runCts.Token)
                    .ConfigureAwait(false);
            }
            finally
            {
                await monitorStop.CancelAsync().ConfigureAwait(false);
                await monitor.ConfigureAwait(false);
            }
            return true;
        }
        finally
        {
            await durableStore.ReleaseLeaseAsync(claimed.RunId, leaseOwner, CancellationToken.None)
                .ConfigureAwait(false);
            if (reserved && !executionOwnsAdmission)
                ReleaseRun(claimed.UserId);
        }
    }

    private async Task<IReadOnlyList<ChatConversationContextTurn>> BuildConversationHistoryAsync(
        AgentRunSnapshot current,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(current.ConversationId))
            return [];
        var priorRuns = await _store.ListConversationAsync(
            current.EntryPoint, current.UserId, current.ConversationId, 100, ct).ConfigureAwait(false);
        return priorRuns
            .Where(run => run.RunId != current.RunId &&
                          run.Status == AgentRunStatuses.Completed && run.Answer is not null)
            .TakeLast(10)
            .Select(static run => new ChatConversationContextTurn
            {
                Question = run.Question,
                Summary = run.Answer!.Summary,
                Findings = run.Answer.Findings.Select(static finding => finding.Statement).Take(8).ToArray(),
                Limitations = run.Answer.Limitations.Take(5).ToArray()
            })
            .ToArray();
    }

    private async Task MonitorLeaseAndCancellationAsync(
        IDurableAgentRunStore store,
        string runId,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationTokenSource runCts,
        CancellationToken hostToken)
    {
        try
        {
            while (!runCts.IsCancellationRequested && !hostToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), hostToken).ConfigureAwait(false);
                var persisted = await _store.GetAsync(runId, hostToken).ConfigureAwait(false);
                if (persisted is null || AgentRunStatuses.IsTerminal(persisted.Status) ||
                    string.Equals(persisted.Status, AgentRunStatuses.Cancelling, StringComparison.Ordinal))
                {
                    await runCts.CancelAsync().ConfigureAwait(false);
                    return;
                }
                if (!await store.RenewLeaseAsync(runId, leaseOwner, leaseDuration, hostToken)
                        .ConfigureAwait(false))
                {
                    await runCts.CancelAsync().ConfigureAwait(false);
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested || hostToken.IsCancellationRequested)
        {
        }
    }

    public AgentCapabilities GetCapabilities(string entryPoint)
    {
        ValidateEntryPoint(entryPoint);
        var settings = GetSettings();
        var tools = ToolsForEntryPoint(entryPoint);
        return new()
        {
            EntryPoint = entryPoint,
            Purpose = RunPurposes.ForEntryPoint(entryPoint),
            Enabled = settings.Enabled,
            CombinedAnalysisEnabled = settings.Enabled && settings.EnableCombinedAnalysis && !IsDeterministic(settings),
            Provider = settings.Provider,
            FastModel = settings.FastModel,
            ReasoningModel = settings.ReasoningModel,
            IsDeterministic = IsDeterministic(settings),
            Modes = settings.Enabled
                ? settings.EnableCombinedAnalysis && !IsDeterministic(settings) ? ["quick", "combined"] : ["quick"]
                : [],
            Roles = settings.Enabled && settings.EnableCombinedAnalysis && !IsDeterministic(settings) ? AnalysisPerspectives.All : [],
            Tools = tools.Values.Select(static tool => new AgentToolCapability
            {
                Name = tool.Definition.Name,
                Version = tool.Definition.Version,
                Description = tool.Definition.Description,
                EntryPoint = tool.Definition.EntryPoint,
                Purpose = tool.Definition.Purpose,
                Access = tool.Definition.Access
            }).OrderBy(static tool => tool.Name, StringComparer.Ordinal).ToArray(),
            MaxToolCalls = settings.MaxToolCalls,
            MaxRunSeconds = settings.MaxRunSeconds,
            MaxDiscussionRounds = settings.MaxDiscussionRounds,
            MaxDiscussionTurns = settings.MaxDiscussionTurns
        };
    }

    public async Task<AgentRunPage> ListAsync(
        string entryPoint,
        string userId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        ValidateEntryPoint(entryPoint);
        var runs = await _store.ListAsync(entryPoint, userId, before, normalizedLimit + 1, ct).ConfigureAwait(false);
        var hasMore = runs.Count > normalizedLimit;
        var page = runs.Take(normalizedLimit).ToArray();
        return new AgentRunPage
        {
            Items = page.Select(static run => new AgentRunListItem
            {
                RunId = run.RunId,
                ConversationId = run.ConversationId ?? run.RunId,
                UserId = run.UserId,
                Question = run.Question,
                PageContext = run.PageContext,
                AccessScope = run.AccessScope,
                EntryPoint = run.EntryPoint,
                Purpose = run.Purpose,
                Mode = run.Mode,
                Status = run.Status,
                CreatedAt = run.CreatedAt,
                CompletedAt = run.CompletedAt,
                Summary = run.Answer?.Summary,
                Usage = run.Usage
            }).ToArray(),
            NextBefore = hasMore && page.Length > 0 ? page[^1].CreatedAt : null
        };
    }

    public async Task<AgentRunSnapshot?> GetAsync(string entryPoint, string runId, CancellationToken ct = default)
    {
        ValidateEntryPoint(entryPoint);
        var run = await _store.GetAsync(runId, ct).ConfigureAwait(false);
        return run is not null && string.Equals(run.EntryPoint, entryPoint, StringComparison.Ordinal) ? run : null;
    }

    public async Task<IReadOnlyList<AgentRunSnapshot>> GetConversationAsync(
        string entryPoint,
        string userId,
        string conversationId,
        CancellationToken ct = default)
    {
        ValidateEntryPoint(entryPoint);
        return await _store.ListConversationAsync(entryPoint, userId, conversationId, 100, ct)
            .ConfigureAwait(false);
    }

    private static AgentRunAccessScopeSnapshot SnapshotAccessScope(AgentAccessScope accessScope)
        => new()
        {
            AllowAllSites = accessScope.AllowAllSites,
            SiteIds = accessScope.AllowAllSites
                ? []
                : accessScope.SiteIds
                    .Where(static siteId => !string.IsNullOrWhiteSpace(siteId) && siteId != "*")
                    .Select(static siteId => siteId.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Order(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
        };

    private static AgentAccessScope RestoreAccessScope(AgentRunAccessScopeSnapshot? snapshot)
        => new()
        {
            AllowAllSites = snapshot?.AllowAllSites ?? false,
            SiteIds = new HashSet<string>(
                snapshot?.SiteIds ?? [],
                StringComparer.OrdinalIgnoreCase)
        };

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        string entryPoint,
        string runId,
        long afterSequence = 0,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ValidateEntryPoint(entryPoint);
        if (await GetAsync(entryPoint, runId, ct).ConfigureAwait(false) is null)
            yield break;

        var cursor = Math.Max(0, afterSequence);
        while (!ct.IsCancellationRequested)
        {
            var events = await _store.ReadEventsAsync(runId, cursor, 100, ct).ConfigureAwait(false);
            foreach (var item in events)
            {
                cursor = item.Sequence;
                yield return item;
            }

            var run = await GetAsync(entryPoint, runId, ct).ConfigureAwait(false);

            if (run is null || (AgentRunStatuses.IsTerminal(run.Status) && events.Count == 0))
                yield break;
            await Task.Delay(TimeSpan.FromMilliseconds(350), ct).ConfigureAwait(false);
        }
    }

    public async Task<bool> CancelAsync(
        string entryPoint,
        string runId,
        string userId,
        string reason,
        CancellationToken ct = default)
    {
        ValidateEntryPoint(entryPoint);
        var run = await GetAsync(entryPoint, runId, ct).ConfigureAwait(false);
        if (run is null || !string.Equals(run.UserId, userId, StringComparison.OrdinalIgnoreCase) ||
            AgentRunStatuses.IsTerminal(run.Status))
            return false;
        var cancellationReason = string.IsNullOrWhiteSpace(reason) ? "用户请求取消。" : reason.Trim();
        if (_active.TryGetValue(runId, out var source))
        {
            await _store.UpdateAsync(run with
            {
                Status = AgentRunStatuses.Cancelling,
                CancellationReason = cancellationReason
            }, CancellationToken.None).ConfigureAwait(false);
            await source.CancelAsync().ConfigureAwait(false);
        }
        else
        {
            var cancelled = run with
            {
                Status = AgentRunStatuses.Cancelled,
                CompletedAt = DateTimeOffset.UtcNow,
                CancellationReason = cancellationReason
            };
            await _store.UpdateAsync(cancelled, CancellationToken.None).ConfigureAwait(false);
            await NotifyTerminalAsync(cancelled).ConfigureAwait(false);
            await EmitAsync(runId, AgentStreamEventTypes.RunCancelled,
                new { reason = cancellationReason }, CancellationToken.None).ConfigureAwait(false);
        }
        return true;
    }

    public async Task<bool> DeleteAsync(
        string entryPoint,
        string runId,
        string userId,
        CancellationToken ct = default)
    {
        ValidateEntryPoint(entryPoint);
        var run = await GetAsync(entryPoint, runId, ct).ConfigureAwait(false);
        if (run is null || !string.Equals(run.UserId, userId, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!AgentRunStatuses.IsTerminal(run.Status))
            throw new InvalidOperationException("运行中的对话不能删除，请先停止并等待运行结束。");
        return await _store.DeleteAsync(runId, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteConversationAsync(
        string entryPoint,
        string conversationId,
        string userId,
        CancellationToken ct = default)
    {
        ValidateEntryPoint(entryPoint);
        var runs = await _store.ListConversationAsync(entryPoint, userId, conversationId, 100, ct)
            .ConfigureAwait(false);
        if (runs.Count == 0)
            return false;
        if (runs.Any(static run => !AgentRunStatuses.IsTerminal(run.Status)))
            throw new InvalidOperationException("运行中的对话不能删除，请先停止并等待运行结束。");
        return await _store.DeleteConversationAsync(entryPoint, userId, conversationId, ct)
            .ConfigureAwait(false);
    }

    private static bool SamePageContext(PageContextRef? left, PageContextRef? right)
        => left is null && right is null ||
           left is not null && right is not null &&
           string.Equals(left.Kind, right.Kind, StringComparison.Ordinal) &&
           string.Equals(left.Id, right.Id, StringComparison.Ordinal);

    private static bool CanReuseCapturedScope(
        AgentRunAccessScopeSnapshot? captured,
        AgentAccessScope current)
    {
        if (captured is null)
            return false;
        if (captured.AllowAllSites)
            return current.AllowAllSites;
        return current.AllowAllSites || captured.SiteIds.All(current.SiteIds.Contains);
    }

    private async Task ExecuteAsync(
        AgentRunSnapshot initial,
        CreateChatRunRequest request,
        AgentAccessScope accessScope,
        IModelClient model,
        IReadOnlyDictionary<string, IAnalysisTool> tools,
        EntryPointSettings settings,
        CancellationToken ct)
    {
        var run = initial;
        var started = Stopwatch.StartNew();
        var modelCalls = new List<ModelCallUsage>();
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.run", ActivityKind.Internal);
        activity?.SetTag("gen_ai.operation.name", "agent.run");
        activity?.SetTag("gen_ai.provider.name", model.Provider);
        activity?.SetTag("gen_ai.request.model", model.Model);
        activity?.SetTag("ingot.agent.run.id", run.RunId);
        activity?.SetTag("ingot.agent.mode", run.Mode);
        activity?.SetTag("ingot.product.entryPoint", run.EntryPoint);
        activity?.SetTag("ingot.run.purpose", run.Purpose);
        try
        {
            run = run with { Status = AgentRunStatuses.Running, StartedAt = DateTimeOffset.UtcNow };
            await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
            await EmitAsync(run.RunId, AgentStreamEventTypes.RunStarted, new { run.RunId }, ct)
                .ConfigureAwait(false);

            var planResult = await model.ResolveIntentAsync(request, tools.Values.Select(static x => x.Definition).ToArray(), ct)
                .ConfigureAwait(false);
            modelCalls.Add(planResult.Usage);
            RecordModelCall(planResult.Usage);
            var plan = BindSingleSiteScope(
                planResult.Value with { EntryPoint = run.EntryPoint },
                accessScope,
                tools);
            if (plan.ToolCalls.Count == 0)
            {
                run = run with { Plan = plan };
                await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                await EmitAsync(run.RunId, AgentStreamEventTypes.PlanCreated, plan, ct).ConfigureAwait(false);

                var conversationResult = await model.ComposeConversationAsync(request, plan, ct)
                    .ConfigureAwait(false);
                modelCalls.Add(conversationResult.Usage);
                RecordModelCall(conversationResult.Usage);
                var guidance = conversationResult.Value with
                {
                    SummaryStrength = AnalysisClaimStrengths.Observation,
                    Findings = [],
                    RelatedRecords = [],
                    Charts = [],
                    Proposals = [],
                    CombinedAnalysis = null
                };
                run = run with
                {
                    Status = AgentRunStatuses.Completed,
                    Answer = guidance,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Usage = BuildUsage(modelCalls, 0, settings.ModelPricing)
                };
                await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                await NotifyTerminalAsync(run).ConfigureAwait(false);
                await EmitAsync(run.RunId, AgentStreamEventTypes.AnswerDelta,
                    new { text = guidance.Summary }, CancellationToken.None).ConfigureAwait(false);
                await EmitAsync(run.RunId, AgentStreamEventTypes.RunCompleted,
                    new { run.RunId, answer = guidance }, CancellationToken.None).ConfigureAwait(false);
                return;
            }
            if (!_planValidator.TryValidate(run.EntryPoint, plan, tools, out var planError))
            {
                await EmitAsync(run.RunId, AgentStreamEventTypes.PlanRejected, new { error = planError }, ct)
                    .ConfigureAwait(false);
                throw new InvalidOperationException(planError);
            }

            run = run with { Plan = plan };
            await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
            await EmitAsync(run.RunId, AgentStreamEventTypes.PlanCreated, plan, ct).ConfigureAwait(false);

            var results = new List<AnalysisToolResult>();
            var invocations = new List<AgentToolInvocation>();
            var pendingCalls = plan.ToolCalls;
            AnalysisToolCall? previousCall = null;
            var maxToolCalls = settings.MaxToolCalls;
            if (pendingCalls.Count > 0)
            {
                const int iteration = 1;
                run = run with { Iteration = iteration };
                await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                await EmitAsync(run.RunId, AgentStreamEventTypes.IterationStarted,
                    new { iteration, calls = pendingCalls.Count }, ct).ConfigureAwait(false);

                foreach (var call in pendingCalls)
                {
                    ct.ThrowIfCancellationRequested();
                    if (invocations.Count >= maxToolCalls)
                        break;
                    if (previousCall is not null && SameCall(previousCall, call))
                        throw new InvalidOperationException($"模型连续重复了没有进展的工具调用: {call.Tool}");
                    previousCall = call;
                    var tool = tools[call.Tool];
                    var invocation = new AgentToolInvocation
                    {
                        Tool = tool.Definition.Name,
                        Version = tool.Definition.Version,
                        Status = "running",
                        StartedAt = DateTimeOffset.UtcNow
                    };
                    invocations.Add(invocation);
                    run = run with { ToolInvocations = invocations.ToArray() };
                    await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                    await EmitAsync(run.RunId, AgentStreamEventTypes.ToolStarted,
                            new { tool = invocation.Tool, version = invocation.Version, iteration }, ct)
                        .ConfigureAwait(false);

                    try
                    {
                        var toolStarted = Stopwatch.StartNew();
                        using var toolActivity = AgentTelemetry.ActivitySource.StartActivity("agent.tool", ActivityKind.Internal);
                        toolActivity?.SetTag("gen_ai.operation.name", "execute_tool");
                        toolActivity?.SetTag("gen_ai.tool.name", tool.Definition.Name);
                        toolActivity?.SetTag("gen_ai.tool.call.id", $"{run.RunId}:{invocations.Count}");
                        var result = await tool.ExecuteAsync(
                                call,
                                new AgentExecutionContext
                                {
                                    RunId = run.RunId,
                                    UserId = run.UserId,
                                    EntryPoint = run.EntryPoint,
                                    Purpose = run.Purpose,
                                    Request = request,
                                    AccessScope = accessScope
                                },
                                ct)
                            .ConfigureAwait(false);
                        AgentTelemetry.ToolDuration.Record(toolStarted.Elapsed.TotalMilliseconds,
                            new KeyValuePair<string, object?>("ingot.agent.tool.name", tool.Definition.Name));
                        results.Add(result);
                        invocation = invocation with
                        {
                            Status = "completed",
                            CompletedAt = DateTimeOffset.UtcNow,
                            Summary = result.Summary,
                            RelatedRecords = result.RelatedRecords
                        };
                        invocations[^1] = invocation;
                        run = run with { ToolInvocations = invocations.ToArray() };
                        await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                        await EmitAsync(run.RunId, AgentStreamEventTypes.ToolCompleted,
                                new { tool = invocation.Tool, result.Summary, result.RelatedRecords, iteration }, ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        invocations[^1] = invocation with
                        {
                            Status = "failed",
                            CompletedAt = DateTimeOffset.UtcNow,
                            Error = ex.Message
                        };
                        run = run with { ToolInvocations = invocations.ToArray() };
                        await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                        await EmitAsync(run.RunId, AgentStreamEventTypes.ToolFailed,
                                new { tool = invocation.Tool, error = ex.Message, iteration }, ct)
                            .ConfigureAwait(false);
                        throw;
                    }
                }

                await EmitAsync(run.RunId, AgentStreamEventTypes.IterationCompleted,
                    new { iteration, stage = run.WorkflowStage, toolCalls = invocations.Count }, ct).ConfigureAwait(false);

            }

            if (!_relatedRecordsVerifier.TryVerify(results, out var relatedRecords, out var relatedRecordsError))
                throw new InvalidOperationException(relatedRecordsError);
            run = run with
            {
                ToolResults = results.Select((result, index) => Snapshot(
                    result,
                    invocations[index].Version)).ToArray()
            };
            await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
            await EmitAsync(run.RunId, AgentStreamEventTypes.RelatedRecordsChecked,
                    new { count = relatedRecords.Count, relatedRecords }, ct)
                .ConfigureAwait(false);

            var insufficientData = results.Any(static result =>
                string.Equals(result.Outcome, AnalysisToolOutcomes.InsufficientData, StringComparison.Ordinal));
            CombinedAnalysisResult? investigation = null;
            if (!insufficientData && settings.EnableCombinedAnalysis && request.Mode == "combined")
            {
                var investigationResult = await _investigationWorkflow.RunAsync(
                        request,
                        plan,
                        results,
                        model,
                        (type, data, token) => EmitAsync(run.RunId, type, data, token),
                        ct)
                    .ConfigureAwait(false);
                investigation = investigationResult.Verdict;
                modelCalls.AddRange(investigationResult.ModelCalls);
                foreach (var usage in investigationResult.ModelCalls)
                    RecordModelCall(usage);
            }

            AnalysisAnswer answer;
            if (insufficientData)
            {
                var insufficientResults = results.Where(static result =>
                    string.Equals(result.Outcome, AnalysisToolOutcomes.InsufficientData, StringComparison.Ordinal)).ToArray();
                var limitations = results.SelectMany(static result => result.Limitations)
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                answer = new AnalysisAnswer
                {
                    Summary = string.Join(" ", insufficientResults.Select(static result => result.Summary)),
                    Limitations = limitations.Length > 0
                        ? limitations
                        : ["当前数据不足，无法得出确定性结论。"],
                    RelatedRecords = relatedRecords,
                    FollowUpQuestions = ["补充缺失的生产记录后重新分析。"]
                };
            }
            else
            {
                var answerResult = await model.ComposeAnswerAsync(request, plan, results, ct).ConfigureAwait(false);
                modelCalls.Add(answerResult.Usage);
                RecordModelCall(answerResult.Usage);
                var limitations = answerResult.Value.Limitations
                    .Concat(results.SelectMany(static result => result.Limitations))
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                answer = answerResult.Value with
                {
                    RelatedRecords = relatedRecords,
                    CombinedAnalysis = investigation,
                    Limitations = limitations
                };
            }
            if (!_relatedRecordsVerifier.TryVerifyAnswer(answer, results, out var answerError))
                throw new InvalidOperationException(answerError);
            await EmitAsync(run.RunId, AgentStreamEventTypes.AnswerDelta,
                    new { text = answer.Summary }, ct)
                .ConfigureAwait(false);
            foreach (var chart in answer.Charts)
                await EmitAsync(run.RunId, AgentStreamEventTypes.ChartCompleted, chart, ct).ConfigureAwait(false);

            run = run with
            {
                Status = AgentRunStatuses.Completed,
                Answer = answer,
                CompletedAt = DateTimeOffset.UtcNow,
                Usage = BuildUsage(modelCalls, invocations.Count, settings.ModelPricing)
            };
            await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
            await NotifyTerminalAsync(run).ConfigureAwait(false);
            await EmitAsync(run.RunId, AgentStreamEventTypes.RunCompleted,
                    new { run.RunId, answer }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            var persisted = await _store.GetAsync(run.RunId, CancellationToken.None).ConfigureAwait(false);
            run = run with
            {
                Status = AgentRunStatuses.Cancelled,
                CompletedAt = DateTimeOffset.UtcNow,
                CancellationReason = persisted?.CancellationReason ?? "运行已由用户取消或超过时间限制。",
                Usage = BuildUsage(modelCalls, run.ToolInvocations.Count, settings.ModelPricing)
            };
            await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
            await NotifyTerminalAsync(run).ConfigureAwait(false);
            await EmitAsync(run.RunId, AgentStreamEventTypes.RunCancelled,
                    new { reason = run.CancellationReason }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent 运行失败: {RunId}", run.RunId);
            run = run with
            {
                Status = AgentRunStatuses.Failed,
                CompletedAt = DateTimeOffset.UtcNow,
                Error = ex.Message,
                Usage = BuildUsage(modelCalls, run.ToolInvocations.Count, settings.ModelPricing)
            };
            await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
            await NotifyTerminalAsync(run).ConfigureAwait(false);
            await EmitAsync(run.RunId, AgentStreamEventTypes.RunFailed,
                    new { error = ex.Message }, CancellationToken.None)
                .ConfigureAwait(false);
        }
        finally
        {
            started.Stop();
            var outcome = run.Status;
            activity?.SetTag("ingot.agent.run.outcome", outcome);
            AgentTelemetry.Runs.Add(1, new KeyValuePair<string, object?>("ingot.agent.run.outcome", outcome));
            AgentTelemetry.RunDuration.Record(started.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("ingot.agent.run.outcome", outcome));
            if (_active.TryRemove(run.RunId, out var source))
                source.Dispose();
            ReleaseRun(run.UserId);
        }
    }

    private async Task NotifyTerminalAsync(AgentRunSnapshot run)
    {
        try
        {
            await _lifecycleSink.OnTerminalAsync(run, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Chat 消息终态投影更新失败: {RunId}", run.RunId);
        }
    }

    private static AgentToolResultSnapshot Snapshot(AnalysisToolResult result, string version)
    {
        return new AgentToolResultSnapshot
        {
            Tool = result.Tool,
            Version = version,
            Summary = result.Summary,
            Data = result.Data.Clone(),
            RelatedRecords = result.RelatedRecords,
            Limitations = result.Limitations,
            Outcome = result.Outcome,
            ContentHash = AgentToolResultIntegrity.ComputeContentHash(
                result.Tool,
                version,
                result.Summary,
                result.Data,
                result.RelatedRecords,
                result.Limitations,
                result.Outcome),
            VerifiedAt = DateTimeOffset.UtcNow
        };
    }

    private async Task EmitAsync(string runId, string type, object? data, CancellationToken ct)
    {
        if (type == AgentStreamEventTypes.DiscussionParticipantFailed)
            AgentTelemetry.DiscussionParticipantFailures.Add(1);
        await _store.AppendEventAsync(runId, type, data, ct).ConfigureAwait(false);
    }

    private static bool SameCall(AnalysisToolCall left, AnalysisToolCall right)
        => string.Equals(left.Tool, right.Tool, StringComparison.Ordinal) &&
           left.Arguments.OrderBy(static item => item.Key, StringComparer.Ordinal)
               .SequenceEqual(right.Arguments.OrderBy(static item => item.Key, StringComparer.Ordinal));

    private static AnalysisPlan BindSingleSiteScope(
        AnalysisPlan plan,
        AgentAccessScope accessScope,
        IReadOnlyDictionary<string, IAnalysisTool> tools)
    {
        var siteId = accessScope.SingleAuthorizedSiteOrDefault();
        if (siteId is null)
            return plan;
        var calls = plan.ToolCalls.Select(call =>
        {
            if (call.Arguments.ContainsKey("siteId") ||
                !tools.TryGetValue(call.Tool, out var tool) ||
                !tool.Definition.InputSchema.TryGetProperty("properties", out var properties) ||
                !properties.TryGetProperty("siteId", out _))
                return call;
            return call with
            {
                Arguments = new Dictionary<string, string?>(call.Arguments, StringComparer.Ordinal)
                {
                    ["siteId"] = siteId
                }
            };
        }).ToArray();
        return plan with { ToolCalls = calls };
    }

    private void ReserveRun(string userId, EntryPointSettings settings)
    {
        lock (_admissionGate)
        {
            if (_activeCount >= settings.MaxConcurrentRuns)
                throw new InvalidOperationException("当前 Agent 运行已达到系统并发上限，请稍后重试。");
            var userCount = _activeByUser.GetValueOrDefault(userId);
            if (userCount >= settings.MaxConcurrentRunsPerUser)
                throw new InvalidOperationException("当前用户的 Agent 运行已达到并发上限，请等待现有运行结束。");
            _activeCount++;
            _activeByUser[userId] = userCount + 1;
        }
    }

    private void ReleaseRun(string userId)
    {
        lock (_admissionGate)
        {
            if (_activeCount > 0)
                _activeCount--;
            var userCount = _activeByUser.GetValueOrDefault(userId);
            if (userCount <= 1)
                _activeByUser.Remove(userId);
            else
                _activeByUser[userId] = userCount - 1;
        }
    }

    private static AgentUsageSummary BuildUsage(
        IReadOnlyList<ModelCallUsage> calls,
        int toolCalls,
        IReadOnlyDictionary<string, ModelPricingOptions> modelPricing)
    {
        decimal totalCost = 0;
        string? currency = null;
        var costKnown = calls.Count > 0;
        foreach (var call in calls)
        {
            if (!modelPricing.TryGetValue(call.Model, out var pricing))
            {
                costKnown = false;
                continue;
            }
            if (currency is not null && !string.Equals(currency, pricing.Currency, StringComparison.OrdinalIgnoreCase))
            {
                costKnown = false;
                continue;
            }
            currency = pricing.Currency;
            totalCost += call.InputTokens / 1_000_000m * pricing.InputPerMillionTokens;
            totalCost += call.OutputTokens / 1_000_000m * pricing.OutputPerMillionTokens;
        }

        return new AgentUsageSummary
        {
            InputTokens = calls.Sum(static item => item.InputTokens),
            OutputTokens = calls.Sum(static item => item.OutputTokens),
            TotalTokens = calls.Sum(static item => item.InputTokens + item.OutputTokens),
            ModelCalls = calls.Count,
            ToolCalls = toolCalls,
            EstimatedCost = costKnown ? totalCost : null,
            Currency = currency ?? "USD"
        };
    }

    private static void RecordModelCall(ModelCallUsage usage)
    {
        using var activity = AgentTelemetry.ActivitySource.StartActivity("agent.model", ActivityKind.Client);
        activity?.SetTag("gen_ai.operation.name", usage.Operation);
        activity?.SetTag("gen_ai.provider.name", usage.Provider);
        activity?.SetTag("gen_ai.request.model", usage.Model);
        activity?.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
        activity?.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
        var tags = new TagList
        {
            { "gen_ai.provider.name", usage.Provider },
            { "gen_ai.request.model", usage.Model },
            { "gen_ai.operation.name", usage.Operation }
        };
        AgentTelemetry.ModelTokens.Record(usage.InputTokens + usage.OutputTokens, tags);
        AgentTelemetry.ModelDuration.Record(usage.DurationMilliseconds, tags);
    }

    private IReadOnlyDictionary<string, IAnalysisTool> ToolsForEntryPoint(string entryPoint)
        => _tools.Values
            .Where(tool => string.Equals(tool.Definition.EntryPoint, entryPoint, StringComparison.Ordinal))
            .Where(tool => tool.Definition.Access == AgentToolAccess.Read)
            .ToDictionary(static tool => tool.Definition.Name, StringComparer.Ordinal);

    private static bool IsDeterministic(EntryPointSettings settings) =>
        string.Equals(settings.Provider, "Deterministic", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(settings.FastModel, "deterministic-v1", StringComparison.OrdinalIgnoreCase);

    private EntryPointSettings GetSettings()
    {
        var model = _modelSettings.Current;
        return new(
            model.Enabled,
            model.Provider,
            model.FastModel,
            model.ReasoningModel,
            Math.Clamp(_chatOptions.MaxToolCalls, 1, 8),
            Math.Clamp(_chatOptions.MaxRunSeconds, 1, 900),
            Math.Clamp(_chatOptions.MaxConcurrentRuns, 1, 128),
            Math.Clamp(_chatOptions.MaxConcurrentRunsPerUser, 1, 32),
            _chatOptions.EnableCombinedAnalysis,
            Math.Clamp(_chatOptions.MaxDiscussionRounds, 1, 5),
            Math.Clamp(_chatOptions.MaxDiscussionTurns, 3, 15),
            _chatOptions.ModelPricing);
    }

    private static void ValidateEntryPoint(string entryPoint)
    {
        if (!ProductEntryPoints.All.Contains(entryPoint))
            throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, "不支持的功能入口。");
    }

    private sealed record EntryPointSettings(
        bool Enabled,
        string Provider,
        string FastModel,
        string ReasoningModel,
        int MaxToolCalls,
        int MaxRunSeconds,
        int MaxConcurrentRuns,
        int MaxConcurrentRunsPerUser,
        bool EnableCombinedAnalysis,
        int MaxDiscussionRounds,
        int MaxDiscussionTurns,
        IReadOnlyDictionary<string, ModelPricingOptions> ModelPricing);
}
