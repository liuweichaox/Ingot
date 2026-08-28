// 编排正式对话、幂等消息预留和助手运行绑定，不依赖数据库或模型供应商。
using Ingot.Contracts.Agents;

namespace Ingot.Platform.Application.Chat;

/// <summary>提供以 Conversation 和 Message 为边界的 Chat 应用用例。</summary>
public sealed class ChatConversationApplication(
    IChatConversationStore conversations,
    IChatRunGateway runs)
{
    public Task<ChatConversationSummary?> GetSummaryAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default)
        => conversations.GetAsync(NormalizeConversationId(conversationId), userId, ct);

    public Task<ChatConversationPage> ListAsync(
        string userId,
        DateTimeOffset? before,
        int limit,
        CancellationToken ct = default)
        => conversations.ListAsync(userId, before, Math.Clamp(limit, 1, 100), ct);

    public Task<ChatConversationDetail?> GetAsync(
        string conversationId,
        string userId,
        long? beforeSequence,
        int limit,
        CancellationToken ct = default)
        => conversations.GetDetailAsync(
            NormalizeConversationId(conversationId),
            userId,
            beforeSequence,
            Math.Clamp(limit, 1, 200),
            ct);

    public async Task<ChatMessageAccepted> StartAsync(
        string userId,
        StartChatConversationRequest request,
        AgentRunAccessScopeSnapshot accessScope,
        CancellationToken ct = default)
    {
        var normalized = ValidateMessage(request.Text, request.ClientMessageId, request.Mode, request.PageContext);
        var conversationId = Guid.CreateVersion7().ToString();
        var now = DateTimeOffset.UtcNow;
        var conversation = new ChatConversationSummary
        {
            ConversationId = conversationId,
            Title = BuildTitle(normalized.Question),
            PageContext = normalized.PageContext,
            Status = ChatConversationStatuses.Active,
            CreatedAt = now,
            UpdatedAt = now,
            LastMessageAt = now,
            LastMessagePreview = normalized.Question,
            LastMessageStatus = ChatMessageStatuses.Completed
        };
        var reservation = await conversations.CreateConversationWithTurnAsync(
            conversation,
            userId,
            NormalizeClientMessageId(request.ClientMessageId),
            normalized.Question,
            ct).ConfigureAwait(false);
        return await StartRunAsync(userId, normalized, reservation, accessScope, ct).ConfigureAwait(false);
    }

    public async Task<ChatMessageAccepted> SendAsync(
        string conversationId,
        string userId,
        SendChatMessageRequest request,
        AgentRunAccessScopeSnapshot accessScope,
        CancellationToken ct = default)
    {
        var normalizedConversationId = NormalizeConversationId(conversationId);
        var conversation = await conversations.GetAsync(normalizedConversationId, userId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("对话不存在。");
        if (!string.Equals(conversation.Status, ChatConversationStatuses.Active, StringComparison.Ordinal))
            throw new InvalidOperationException("已归档的对话不能继续发送消息。");
        var normalized = ValidateMessage(request.Text, request.ClientMessageId, request.Mode, conversation.PageContext)
            with { ConversationId = normalizedConversationId };
        var reservation = await conversations.AppendTurnAsync(
            normalizedConversationId,
            userId,
            NormalizeClientMessageId(request.ClientMessageId),
            normalized.Question,
            ct).ConfigureAwait(false);
        return await StartRunAsync(userId, normalized, reservation, accessScope, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(
        string conversationId,
        string userId,
        CancellationToken ct = default)
    {
        var normalized = NormalizeConversationId(conversationId);
        if (await conversations.GetAsync(normalized, userId, ct).ConfigureAwait(false) is null)
            return false;
        await runs.DeleteConversationAsync(normalized, userId, ct).ConfigureAwait(false);
        return await conversations.DeleteAsync(normalized, userId, ct).ConfigureAwait(false);
    }

    private async Task<ChatMessageAccepted> StartRunAsync(
        string userId,
        CreateChatRunRequest request,
        ChatTurnReservation reservation,
        AgentRunAccessScopeSnapshot accessScope,
        CancellationToken ct)
    {
        if (reservation.AlreadyExists && !string.IsNullOrWhiteSpace(reservation.RunId))
        {
            return Accepted(reservation, reservation.RunId, AgentRunStatuses.Queued);
        }

        try
        {
            var run = await runs.StartAsync(
                userId,
                request with
                {
                    ConversationId = reservation.ConversationId,
                    TriggerMessageId = reservation.UserMessageId,
                    ResponseMessageId = reservation.AssistantMessageId
                },
                accessScope,
                ct).ConfigureAwait(false);
            await conversations.BindRunAsync(reservation.AssistantMessageId, run.RunId, ct).ConfigureAwait(false);
            return Accepted(reservation, run.RunId, run.Status);
        }
        catch (Exception exception)
        {
            await conversations.FailAssistantMessageAsync(
                reservation.AssistantMessageId,
                exception.Message,
                CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private static ChatMessageAccepted Accepted(
        ChatTurnReservation reservation,
        string runId,
        string status)
        => new()
        {
            ConversationId = reservation.ConversationId,
            UserMessageId = reservation.UserMessageId,
            AssistantMessageId = reservation.AssistantMessageId,
            RunId = runId,
            Status = status,
            StreamUrl = $"/api/v1/chat/runs/{runId}/stream"
        };

    private static CreateChatRunRequest ValidateMessage(
        string? text,
        string? clientMessageId,
        string? mode,
        PageContextRef? pageContext)
    {
        _ = NormalizeClientMessageId(clientMessageId);
        if (!AgentContractValidator.TryValidate(
                new CreateChatRunRequest
                {
                    Question = text ?? string.Empty,
                    Mode = mode ?? "quick",
                    PageContext = pageContext
                },
                out var normalized,
                out var error))
            throw new ArgumentException(error);
        return normalized!;
    }

    private static string NormalizeConversationId(string conversationId)
        => Guid.TryParse(conversationId, out var value)
            ? value.ToString()
            : throw new ArgumentException("对话标识必须是合法的 UUID。");

    private static string NormalizeClientMessageId(string? clientMessageId)
        => Guid.TryParse(clientMessageId, out var value)
            ? value.ToString()
            : throw new ArgumentException("ClientMessageId 必须是合法的 UUID。");

    private static string BuildTitle(string text)
    {
        var value = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= 80 ? value : $"{value[..77]}…";
    }
}
