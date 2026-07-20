using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Agent;

public sealed class AgentRuntime : IAgentRuntime
{
    private const string ChatPromptVersion = "ingot-chat-v1";
    private const string ChatToolsetVersion = "production-facts-readonly-v1";
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new(StringComparer.Ordinal);
    private readonly IEvidenceVerifier _evidenceVerifier;
    private readonly ILogger<AgentRuntime> _logger;
    private readonly IInvestigationWorkflow _investigationWorkflow;
    private readonly IModelRouter _models;
    private readonly ChatOptions _chatOptions;
    private readonly IPlanValidator _planValidator;
    private readonly IAgentRunStore _store;
    private readonly IReadOnlyDictionary<string, IAnalysisTool> _tools;

    public AgentRuntime(
        IAgentRunStore store,
        IModelRouter models,
        IEnumerable<IAnalysisTool> tools,
        IPlanValidator planValidator,
        IEvidenceVerifier evidenceVerifier,
        IInvestigationWorkflow investigationWorkflow,
        IOptions<ChatOptions> chatOptions,
        ILogger<AgentRuntime> logger)
    {
        _store = store;
        _models = models;
        _planValidator = planValidator;
        _evidenceVerifier = evidenceVerifier;
        _investigationWorkflow = investigationWorkflow;
        _chatOptions = chatOptions.Value;
        _logger = logger;
        _tools = tools.ToDictionary(static tool => tool.Definition.Name, StringComparer.Ordinal);
    }

    public async Task<AgentRunSnapshot> StartAsync(
        string surface,
        string actorId,
        CreateChatRunRequest request,
        CancellationToken ct = default)
    {
        ValidateSurface(surface);
        var settings = GetSettings();
        if (!settings.Enabled)
            throw new InvalidOperationException("Chat 功能尚未启用。");
        if (string.Equals(request.Mode, "deep", StringComparison.Ordinal) && !settings.EnableDeepInvestigation)
            throw new InvalidOperationException("Chat 深度分析模式尚未启用。");

        var tools = ToolsForSurface(surface);
        if (tools.Count == 0)
            throw new InvalidOperationException($"{surface} 没有已注册工具。");

        var model = _models.GetClient(surface, request.Mode == "deep" ? ModelRole.Reasoning : ModelRole.Fast);
        if (!string.Equals(model.Provider, settings.Provider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{surface} 配置的模型 Provider 没有对应客户端。");
        var run = new AgentRunSnapshot
        {
            RunId = Guid.CreateVersion7().ToString(),
            ActorId = actorId,
            Surface = surface,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Question = request.Question,
            PageContext = request.PageContext,
            Mode = request.Mode,
            Status = AgentRunStatuses.Queued,
            ModelProvider = model.Provider,
            Model = model.Model,
            PromptVersion = ChatPromptVersion,
            ToolsetVersion = ChatToolsetVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            WorkflowStage = "analysis",
            Usage = new AgentUsageSummary()
        };
        await _store.CreateAsync(run, ct).ConfigureAwait(false);

        var timeout = TimeSpan.FromSeconds(Math.Clamp(settings.MaxRunSeconds, 1, 900));
        var runCts = new CancellationTokenSource(timeout);
        if (!_active.TryAdd(run.RunId, runCts))
            throw new InvalidOperationException("无法注册 Chat 运行。");
        _ = ExecuteAsync(run, request, model, tools, settings, runCts.Token);
        return run;
    }

    public AgentCapabilities GetCapabilities(string surface)
    {
        ValidateSurface(surface);
        var settings = GetSettings();
        var tools = ToolsForSurface(surface);
        return new()
        {
            Surface = surface,
            Purpose = RunPurposes.ForSurface(surface),
            Enabled = settings.Enabled,
            DeepInvestigationEnabled = settings.Enabled && settings.EnableDeepInvestigation,
            Provider = settings.Provider,
            FastModel = settings.FastModel,
            ReasoningModel = settings.ReasoningModel,
            Modes = settings.Enabled
                ? settings.EnableDeepInvestigation ? ["standard", "deep"] : ["standard"]
                : [],
            Roles = settings.Enabled && settings.EnableDeepInvestigation ? InvestigationRoles.All : [],
            Tools = tools.Values.Select(static tool => new AgentToolCapability
            {
                Name = tool.Definition.Name,
                Version = tool.Definition.Version,
                Description = tool.Definition.Description,
                Surface = tool.Definition.Surface,
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
        string surface,
        string actorId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default)
    {
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        ValidateSurface(surface);
        var runs = await _store.ListAsync(surface, actorId, before, normalizedLimit + 1, ct).ConfigureAwait(false);
        var hasMore = runs.Count > normalizedLimit;
        var page = runs.Take(normalizedLimit).ToArray();
        return new AgentRunPage
        {
            Items = page.Select(static run => new AgentRunListItem
            {
                RunId = run.RunId,
                Question = run.Question,
                Surface = run.Surface,
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

    public async Task<AgentRunSnapshot?> GetAsync(string surface, string runId, CancellationToken ct = default)
    {
        ValidateSurface(surface);
        var run = await _store.GetAsync(runId, ct).ConfigureAwait(false);
        return run is not null && string.Equals(run.Surface, surface, StringComparison.Ordinal) ? run : null;
    }

    public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        string surface,
        string runId,
        long afterSequence = 0,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ValidateSurface(surface);
        if (await GetAsync(surface, runId, ct).ConfigureAwait(false) is null)
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

            var run = await GetAsync(surface, runId, ct).ConfigureAwait(false);
            if (run is null || (AgentRunStatuses.IsTerminal(run.Status) && events.Count == 0))
                yield break;
            await Task.Delay(TimeSpan.FromMilliseconds(350), ct).ConfigureAwait(false);
        }
    }

    public async Task<bool> CancelAsync(
        string surface,
        string runId,
        string actorId,
        string reason,
        CancellationToken ct = default)
    {
        ValidateSurface(surface);
        var run = await GetAsync(surface, runId, ct).ConfigureAwait(false);
        if (run is null || !string.Equals(run.ActorId, actorId, StringComparison.OrdinalIgnoreCase) ||
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
            await EmitAsync(runId, AgentStreamEventTypes.RunCancelled,
                new { reason = cancellationReason }, CancellationToken.None).ConfigureAwait(false);
        }
        return true;
    }

    private async Task ExecuteAsync(
        AgentRunSnapshot initial,
        CreateChatRunRequest request,
        IModelClient model,
        IReadOnlyDictionary<string, IAnalysisTool> tools,
        SurfaceSettings settings,
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
        activity?.SetTag("ingot.product.surface", run.Surface);
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
            var plan = planResult.Value with { Surface = run.Surface };
            if (!_planValidator.TryValidate(run.Surface, plan, tools, out var planError))
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
            IReadOnlyList<AnalysisToolCall> pendingCalls = plan.ToolCalls;
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
                                    ActorId = run.ActorId,
                                    Surface = run.Surface,
                                    Purpose = run.Purpose,
                                    Request = request
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
                            Evidence = result.Evidence
                        };
                        invocations[^1] = invocation;
                        run = run with { ToolInvocations = invocations.ToArray() };
                        await _store.UpdateAsync(run, CancellationToken.None).ConfigureAwait(false);
                        await EmitAsync(run.RunId, AgentStreamEventTypes.ToolCompleted,
                                new { tool = invocation.Tool, result.Summary, result.Evidence, iteration }, ct)
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

            if (!_evidenceVerifier.TryVerify(results, out var evidence, out var evidenceError))
                throw new InvalidOperationException(evidenceError);
            await EmitAsync(run.RunId, AgentStreamEventTypes.EvidenceVerified,
                    new { count = evidence.Count, evidence }, ct)
                .ConfigureAwait(false);

            var insufficientData = results.Any(static result =>
                string.Equals(result.Outcome, AnalysisToolOutcomes.InsufficientData, StringComparison.Ordinal));
            InvestigationVerdict? investigation = null;
            if (!insufficientData && settings.EnableDeepInvestigation && request.Mode == "deep")
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
                    Evidence = evidence,
                    FollowUpQuestions = ["补充缺失的生产事件后重新运行分析。"]
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
                    Evidence = evidence,
                    Investigation = investigation,
                    Limitations = limitations
                };
            }
            if (!_evidenceVerifier.TryVerifyAnswer(answer, results, out var answerError))
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
        }
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

    private IReadOnlyDictionary<string, IAnalysisTool> ToolsForSurface(string surface)
        => _tools.Values
            .Where(tool => string.Equals(tool.Definition.Surface, surface, StringComparison.Ordinal))
            .Where(tool => tool.Definition.Access == AgentToolAccess.Read)
            .ToDictionary(static tool => tool.Definition.Name, StringComparer.Ordinal);

    private SurfaceSettings GetSettings() => new(
            _chatOptions.Enabled,
            _chatOptions.Provider,
            _chatOptions.FastModel,
            _chatOptions.ReasoningModel,
            Math.Clamp(_chatOptions.MaxToolCalls, 1, 8),
            Math.Clamp(_chatOptions.MaxRunSeconds, 1, 900),
            _chatOptions.EnableDeepInvestigation,
            Math.Clamp(_chatOptions.MaxDiscussionRounds, 1, 5),
            Math.Clamp(_chatOptions.MaxDiscussionTurns, 3, 15),
            _chatOptions.ModelPricing);

    private static void ValidateSurface(string surface)
    {
        if (!ProductSurfaces.All.Contains(surface))
            throw new ArgumentOutOfRangeException(nameof(surface), surface, "不支持的产品面。");
    }

    private sealed record SurfaceSettings(
        bool Enabled,
        string Provider,
        string FastModel,
        string ReasoningModel,
        int MaxToolCalls,
        int MaxRunSeconds,
        bool EnableDeepInvestigation,
        int MaxDiscussionRounds,
        int MaxDiscussionTurns,
        IReadOnlyDictionary<string, ModelPricingOptions> ModelPricing);
}
