// 定义面向客户端的正式 Chat Conversation、Message 和幂等发送契约。
namespace Ingot.Contracts.Agents;

public static class ChatConversationStatuses
{
    public const string Active = "active";
    public const string Archived = "archived";
}

public static class ChatMessageRoles
{
    public const string User = "user";
    public const string Assistant = "assistant";
}

public static class ChatMessageStatuses
{
    public const string Pending = "pending";
    public const string Generating = "generating";
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed record StartChatConversationRequest
{
    public required string Text { get; init; }

    public required string ClientMessageId { get; init; }

    public PageContextRef? PageContext { get; init; }

    public string Mode { get; init; } = "quick";
}

public sealed record SendChatMessageRequest
{
    public required string Text { get; init; }

    public required string ClientMessageId { get; init; }

    public string Mode { get; init; } = "quick";
}

public sealed record ChatMessageSnapshot
{
    public required string MessageId { get; init; }

    public required string ConversationId { get; init; }

    public required long Sequence { get; init; }

    public required string Role { get; init; }

    public required string Status { get; init; }

    public string? Text { get; init; }

    public AnalysisAnswer? Answer { get; init; }

    public string? RunId { get; init; }

    public string? Error { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed record ChatConversationSummary
{
    public required string ConversationId { get; init; }

    public required string Title { get; init; }

    public PageContextRef? PageContext { get; init; }

    public required string Status { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required DateTimeOffset LastMessageAt { get; init; }

    public string? LastMessagePreview { get; init; }

    public string? LastMessageStatus { get; init; }
}

public sealed record ChatConversationDetail
{
    public required ChatConversationSummary Conversation { get; init; }

    public IReadOnlyList<ChatMessageSnapshot> Messages { get; init; } = [];

    public long? NextBeforeSequence { get; init; }
}

public sealed record ChatConversationPage
{
    public IReadOnlyList<ChatConversationSummary> Items { get; init; } = [];

    public DateTimeOffset? NextBefore { get; init; }
}

public sealed record ChatMessageAccepted
{
    public required string ConversationId { get; init; }

    public required string UserMessageId { get; init; }

    public required string AssistantMessageId { get; init; }

    public required string RunId { get; init; }

    public required string Status { get; init; }

    public required string StreamUrl { get; init; }
}
