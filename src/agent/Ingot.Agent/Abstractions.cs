// 定义 Agent 运行、模型路由、分析工具和结果校验的实现中立边界。
using System.Text.Json;
using Ingot.Contracts.Agents;

namespace Ingot.Agent;

/// <summary>编排一次受治理的 Agent 运行及其生命周期。</summary>
public interface IAgentRuntime
{
    AgentCapabilities GetCapabilities(string entryPoint);

    Task<AgentRunPage> ListAsync(
        string entryPoint,
        string userId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default);

    Task<AgentRunSnapshot> StartAsync(
        string entryPoint,
        string userId,
        CreateChatRunRequest request,
        AgentAccessScope accessScope,
        CancellationToken ct = default);

    Task<AgentRunSnapshot?> GetAsync(string entryPoint, string runId, CancellationToken ct = default);

    Task<IReadOnlyList<AgentRunSnapshot>> GetConversationAsync(
        string entryPoint,
        string userId,
        string conversationId,
        CancellationToken ct = default);

    IAsyncEnumerable<AgentStreamEvent> StreamAsync(
        string entryPoint,
        string runId,
        long afterSequence = 0,
        CancellationToken ct = default);

    Task<bool> CancelAsync(
        string entryPoint,
        string runId,
        string userId,
        string reason,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(
        string entryPoint,
        string runId,
        string userId,
        CancellationToken ct = default);

    Task<bool> DeleteConversationAsync(
        string entryPoint,
        string conversationId,
        string userId,
        CancellationToken ct = default);
}

/// <summary>持久化 Agent 运行快照和有序流事件。</summary>
public interface IAgentRunStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task CreateAsync(AgentRunSnapshot run, CancellationToken ct = default);

    Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default);

    Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(
        string entryPoint,
        string userId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentRunSnapshot>> ListConversationAsync(
        string entryPoint,
        string userId,
        string conversationId,
        int limit,
        CancellationToken ct = default);

    Task UpdateAsync(AgentRunSnapshot run, CancellationToken ct = default);

    Task<bool> DeleteAsync(string runId, CancellationToken ct = default);

    Task<bool> DeleteConversationAsync(
        string entryPoint,
        string userId,
        string conversationId,
        CancellationToken ct = default);

    Task<AgentStreamEvent> AppendEventAsync(
        string runId,
        string type,
        object? data,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(
        string runId,
        long afterSequence,
        int limit,
        CancellationToken ct = default);
}

/// <summary>由生产存储实现的持久运行队列；租约允许 Worker 崩溃后重新领取只读任务。</summary>
public interface IDurableAgentRunStore : IAgentRunStore
{
    Task<AgentRunSnapshot?> ClaimNextAsync(
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default);

    Task<bool> RenewLeaseAsync(
        string runId,
        string leaseOwner,
        TimeSpan leaseDuration,
        CancellationToken ct = default);

    Task ReleaseLeaseAsync(
        string runId,
        string leaseOwner,
        CancellationToken ct = default);
}

/// <summary>由独立 Worker 驱动一次持久队列领取与执行。</summary>
public interface IAgentRunProcessor
{
    Task<bool> ProcessNextAsync(string leaseOwner, CancellationToken ct = default);
}

/// <summary>在运行进入终态后更新宿主持有的消息投影；默认实现为空操作。</summary>
public interface IAgentRunLifecycleSink
{
    Task OnTerminalAsync(AgentRunSnapshot run, CancellationToken ct = default);
}

/// <summary>为不持有正式消息投影的宿主提供空终态通知实现。</summary>
public sealed class NullAgentRunLifecycleSink : IAgentRunLifecycleSink
{
    public Task OnTerminalAsync(AgentRunSnapshot run, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>按功能入口和模型角色选择已配置的模型客户端。</summary>
public interface IModelRouter
{
    IModelClient GetClient(string entryPoint, ModelRole role, string? provider = null);
}

/// <summary>为单次运行冻结当前模型连接，防止运行中配置变化造成模型切换。</summary>
public interface IModelClientSnapshotFactory
{
    IModelClient CreateSnapshot();
}

public enum ModelRole
{
    Fast,
    Reasoning
}

/// <summary>执行意图解析、答案组织和多视角分析的模型端口。</summary>
public interface IModelClient
{
    string EntryPoint { get; }

    string Provider { get; }

    string Model { get; }

    string ModelFor(ModelRole role);

    Task<ModelCallResult<AnalysisPlan>> ResolveIntentAsync(
        CreateChatRunRequest request,
        IReadOnlyCollection<AnalysisToolDefinition> tools,
        CancellationToken ct = default);

    Task<ModelCallResult<AnalysisAnswer>> ComposeAnswerAsync(
        CreateChatRunRequest request,
        AnalysisPlan plan,
        IReadOnlyList<AnalysisToolResult> results,
        CancellationToken ct = default);

    Task<ModelCallResult<AnalysisAnswer>> ComposeConversationAsync(
        CreateChatRunRequest request,
        AnalysisPlan plan,
        CancellationToken ct = default);

    Task<ModelCallResult<PerspectiveAnalysis>> ParticipateAsync(
        CombinedAnalysisTurn turn,
        CancellationToken ct = default);
}

public sealed record CombinedAnalysisTurn
{
    public required string Role { get; init; }

    public required int Round { get; init; }

    public required CombinedAnalysisTask Task { get; init; }

    public required CreateChatRunRequest Request { get; init; }

    public required AnalysisPlan Plan { get; init; }

    public required IReadOnlyList<AnalysisToolResult> ToolResults { get; init; }

    public IReadOnlyList<PossibleCause> PossibleCauses { get; init; } = [];

    public IReadOnlyList<FindingReview> Reviews { get; init; } = [];
}

public sealed record ModelCallUsage
{
    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required string Operation { get; init; }

    public long InputTokens { get; init; }

    public long OutputTokens { get; init; }

    public long DurationMilliseconds { get; init; }
}

public sealed record ModelCallResult<T>
{
    public required T Value { get; init; }

    public required ModelCallUsage Usage { get; init; }
}

public sealed record CombinedAnalysisWorkflowResult
{
    public required CombinedAnalysisResult Verdict { get; init; }

    public IReadOnlyList<ModelCallUsage> ModelCalls { get; init; } = [];
}

/// <summary>执行受治理的多视角分析并保留模型调用记录。</summary>
public interface ICombinedAnalysisWorkflow
{
    Task<CombinedAnalysisWorkflowResult> RunAsync(
        CreateChatRunRequest request,
        AnalysisPlan plan,
        IReadOnlyList<AnalysisToolResult> results,
        IModelClient model,
        Func<string, object?, CancellationToken, Task> publish,
        CancellationToken ct = default);
}

/// <summary>声明并执行一个只读分析工具。</summary>
public interface IAnalysisTool
{
    AnalysisToolDefinition Definition { get; }

    Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default);
}

public sealed record AnalysisToolDefinition
{
    public required string Name { get; init; }

    public required string Version { get; init; }

    public required string Description { get; init; }

    public required string EntryPoint { get; init; }

    public required string Purpose { get; init; }

    public required JsonElement InputSchema { get; init; }

    public string Access { get; init; } = AgentToolAccess.Read;
}

public static class AgentToolAccess
{
    public const string Read = "read";
}

public sealed record AnalysisToolResult
{
    public required string Tool { get; init; }

    public required string Summary { get; init; }

    public required JsonElement Data { get; init; }

    public IReadOnlyList<ResultDetailLink> Details { get; init; } = [];

    public IReadOnlyList<RelatedRecordRef> RelatedRecords { get; init; } = [];

    public IReadOnlyList<string> Limitations { get; init; } = [];

    public string Outcome { get; init; } = AnalysisToolOutcomes.Sufficient;
}

public sealed record ResultDetailLink
{
    public required string Kind { get; init; }

    public required string Label { get; init; }

    public required string Url { get; init; }

    public long? SizeBytes { get; init; }
}

public static class AnalysisToolOutcomes
{
    public const string Sufficient = "sufficient";

    public const string InsufficientData = "insufficient-data";
}

public sealed record AgentExecutionContext
{
    public required string RunId { get; init; }

    public required string UserId { get; init; }

    public required string EntryPoint { get; init; }

    public required string Purpose { get; init; }

    public required CreateChatRunRequest Request { get; init; }

    public required AgentAccessScope AccessScope { get; init; }
}

/// <summary>由宿主根据已认证身份构造、不得接受客户端直接声明的站点授权范围。</summary>
public sealed record AgentAccessScope
{
    public bool AllowAllSites { get; init; }

    public IReadOnlySet<string> SiteIds { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public string EnsureAuthorizedSite(string? requestedSiteId)
    {
        var siteId = requestedSiteId?.Trim();
        if (string.IsNullOrWhiteSpace(siteId))
            throw new UnauthorizedAccessException("分析工具必须指定站点范围。");
        if (!AllowAllSites && !SiteIds.Contains(siteId))
            throw new UnauthorizedAccessException("当前用户不能访问请求的站点数据。");
        return siteId;
    }

    public string? SingleAuthorizedSiteOrDefault()
    {
        var values = SiteIds
            .Where(static value => !string.IsNullOrWhiteSpace(value) && value != "*")
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        return values.Length == 1 ? values[0] : null;
    }
}

/// <summary>验证模型生成的分析计划只能调用获准工具。</summary>
public interface IPlanValidator
{
    bool TryValidate(
        string entryPoint,
        AnalysisPlan plan,
        IReadOnlyDictionary<string, IAnalysisTool> tools,
        out string error);
}

/// <summary>验证工具结果和最终答案的证据引用与结论边界。</summary>
public interface IAnalysisResultValidator
{
    bool TryVerify(
        IReadOnlyList<AnalysisToolResult> results,
        out IReadOnlyList<RelatedRecordRef> relatedRecords,
        out string error);

    bool TryVerifyAnswer(
        AnalysisAnswer answer,
        IReadOnlyList<AnalysisToolResult> results,
        out string error);
}
