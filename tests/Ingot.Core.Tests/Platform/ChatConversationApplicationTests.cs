// 验证正式 Chat 消息先落库、运行绑定和客户端消息幂等边界。
using Ingot.Contracts.Agents;
using Ingot.Platform.Application.Chat;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ChatConversationApplicationTests
{
    [Fact]
    public async Task StartCreatesFormalTurnBeforeBindingExecution()
    {
        var store = new MemoryConversationStore();
        var gateway = new RecordingRunGateway();
        var application = new ChatConversationApplication(store, gateway);
        var clientMessageId = Guid.CreateVersion7().ToString();

        var accepted = await application.StartAsync(
            "operator",
            new StartChatConversationRequest
            {
                Text = "检查最近数据是否完整",
                ClientMessageId = clientMessageId,
                Mode = "quick"
            },
            new AgentRunAccessScopeSnapshot());

        Assert.Equal(accepted.UserMessageId, gateway.Request!.TriggerMessageId);
        Assert.Equal(accepted.AssistantMessageId, gateway.Request.ResponseMessageId);
        Assert.Equal(accepted.ConversationId, gateway.Request.ConversationId);
        Assert.Equal(gateway.RunId, store.Reservations[clientMessageId].RunId);
    }

    [Fact]
    public async Task RetryingClientMessageReturnsExistingRunWithoutDuplicateExecution()
    {
        var store = new MemoryConversationStore();
        var gateway = new RecordingRunGateway();
        var application = new ChatConversationApplication(store, gateway);
        var firstClientId = Guid.CreateVersion7().ToString();
        var first = await application.StartAsync(
            "operator",
            new StartChatConversationRequest
            {
                Text = "第一条",
                ClientMessageId = firstClientId
            },
            new AgentRunAccessScopeSnapshot());
        var nextClientId = Guid.CreateVersion7().ToString();
        var request = new SendChatMessageRequest
        {
            Text = "继续",
            ClientMessageId = nextClientId
        };

        var sent = await application.SendAsync(
            first.ConversationId, "operator", request, new AgentRunAccessScopeSnapshot());
        var retried = await application.SendAsync(
            first.ConversationId, "operator", request, new AgentRunAccessScopeSnapshot());

        Assert.Equal(sent.RunId, retried.RunId);
        Assert.Equal(2, gateway.StartCount);
    }

    private sealed class RecordingRunGateway : IChatRunGateway
    {
        public int StartCount { get; private set; }
        public string RunId { get; private set; } = string.Empty;
        public CreateChatRunRequest? Request { get; private set; }

        public Task<AgentRunSnapshot> StartAsync(
            string userId,
            CreateChatRunRequest request,
            AgentRunAccessScopeSnapshot accessScope,
            CancellationToken ct = default)
        {
            StartCount++;
            Request = request;
            RunId = Guid.CreateVersion7().ToString();
            return Task.FromResult(new AgentRunSnapshot
            {
                RunId = RunId,
                ConversationId = request.ConversationId,
                UserId = userId,
                EntryPoint = ProductEntryPoints.Chat,
                Purpose = RunPurposes.ReadOnlyAnalysis,
                Question = request.Question,
                Mode = request.Mode,
                Status = AgentRunStatuses.Queued,
                ModelProvider = "test",
                Model = "test",
                PromptVersion = "test",
                ToolsetVersion = "test",
                CreatedAt = DateTimeOffset.UtcNow,
                Usage = new AgentUsageSummary()
            });
        }

        public Task<bool> DeleteConversationAsync(
            string conversationId,
            string userId,
            CancellationToken ct = default)
            => Task.FromResult(true);
    }

    private sealed class MemoryConversationStore : IChatConversationStore
    {
        private readonly Dictionary<string, ChatConversationSummary> _conversations = [];
        public Dictionary<string, ChatTurnReservation> Reservations { get; } = [];

        public Task<ChatConversationSummary?> GetAsync(
            string conversationId, string userId, CancellationToken ct = default)
            => Task.FromResult(_conversations.GetValueOrDefault(conversationId));

        public Task<ChatConversationPage> ListAsync(
            string userId, DateTimeOffset? before, int limit, CancellationToken ct = default)
            => Task.FromResult(new ChatConversationPage { Items = _conversations.Values.ToArray() });

        public Task<ChatConversationDetail?> GetDetailAsync(
            string conversationId, string userId, long? beforeSequence, int limit,
            CancellationToken ct = default)
            => Task.FromResult<ChatConversationDetail?>(null);

        public Task<ChatTurnReservation> CreateConversationWithTurnAsync(
            ChatConversationSummary conversation,
            string userId,
            string clientMessageId,
            string text,
            CancellationToken ct = default)
        {
            _conversations.Add(conversation.ConversationId, conversation);
            return Task.FromResult(CreateReservation(conversation.ConversationId, clientMessageId));
        }

        public Task<ChatTurnReservation> AppendTurnAsync(
            string conversationId,
            string userId,
            string clientMessageId,
            string text,
            CancellationToken ct = default)
        {
            if (Reservations.TryGetValue(clientMessageId, out var existing))
                return Task.FromResult(existing with { AlreadyExists = true });
            return Task.FromResult(CreateReservation(conversationId, clientMessageId));
        }

        public Task BindRunAsync(
            string assistantMessageId, string runId, CancellationToken ct = default)
        {
            var pair = Reservations.Single(item => item.Value.AssistantMessageId == assistantMessageId);
            Reservations[pair.Key] = pair.Value with { RunId = runId };
            return Task.CompletedTask;
        }

        public Task CompleteAssistantMessageAsync(
            AgentRunSnapshot run, CancellationToken ct = default) => Task.CompletedTask;

        public Task FailAssistantMessageAsync(
            string assistantMessageId, string error, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> DeleteAsync(
            string conversationId, string userId, CancellationToken ct = default)
            => Task.FromResult(_conversations.Remove(conversationId));

        private ChatTurnReservation CreateReservation(string conversationId, string clientMessageId)
        {
            var value = new ChatTurnReservation
            {
                ConversationId = conversationId,
                UserMessageId = Guid.CreateVersion7().ToString(),
                AssistantMessageId = Guid.CreateVersion7().ToString()
            };
            Reservations.Add(clientMessageId, value);
            return value;
        }
    }
}
