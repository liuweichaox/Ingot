// 定义 Chat 正式会话存储和只读 Agent 运行网关的数据库无关端口。
using Ingot.Contracts.Agents;

namespace Ingot.Platform.Application.Chat;

/// <summary>原子保存正式对话、用户消息和助手消息投影。</summary>
public interface IChatConversationStore
{
    Task<ChatConversationSummary?> GetAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default);

    Task<ChatConversationPage> ListAsync(
        string userId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default);

    Task<ChatConversationDetail?> GetDetailAsync(
        string conversationId,
        string userId,
        long? beforeSequence,
        int limit,
        CancellationToken ct = default);

    Task<ChatTurnReservation> CreateConversationWithTurnAsync(
        ChatConversationSummary conversation,
        string userId,
        string clientMessageId,
        string text,
        CancellationToken ct = default);

    Task<ChatTurnReservation> AppendTurnAsync(
        string conversationId,
        string userId,
        string clientMessageId,
        string text,
        CancellationToken ct = default);

    Task BindRunAsync(
        string assistantMessageId,
        string runId,
        CancellationToken ct = default);

    Task CompleteAssistantMessageAsync(
        AgentRunSnapshot run,
        CancellationToken ct = default);

    Task FailAssistantMessageAsync(
        string assistantMessageId,
        string error,
        CancellationToken ct = default);

    Task<bool> DeleteAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default);
}

public sealed record ChatTurnReservation
{
    public required string ConversationId { get; init; }

    public required string UserMessageId { get; init; }

    public required string AssistantMessageId { get; init; }

    public string? RunId { get; init; }

    public bool AlreadyExists { get; init; }
}

/// <summary>把助手消息委托给只读 Agent 运行，并支持按对话删除执行明细。</summary>
public interface IChatRunGateway
{
    Task<AgentRunSnapshot> StartAsync(
        string userId,
        CreateChatRunRequest request,
        AgentRunAccessScopeSnapshot accessScope,
        CancellationToken ct = default);

    Task<bool> DeleteConversationAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default);
}
