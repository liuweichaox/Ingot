// 验证正式 Chat 会话在读取时按当前研究项目站点权限重新授权。

using System.Reflection;
using System.Security.Claims;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Application.Chat;
using Ingot.Platform.Application.ProcessResearch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ChatConversationSiteScopeTests
{
    [Fact]
    public async Task ListAndGet_DenyConversation_WhenOwnerNoLongerHasProjectSiteAccess()
    {
        var projectId = Guid.CreateVersion7();
        var conversation = Conversation(projectId);
        var researchStore = DispatchProxy.Create<IProcessResearchStore, ResearchStoreProxy>();
        ((ResearchStoreProxy)(object)researchStore).Project = Project(projectId, "SITE-A");
        var controller = Controller(conversation, researchStore, Identity("operator", "SITE-B"));

        var list = Assert.IsType<OkObjectResult>(await controller.List(ct: default));
        Assert.Empty(Assert.IsType<ChatConversationPage>(list.Value).Items);

        var get = Assert.IsType<ObjectResult>(await controller.Get(conversation.ConversationId, ct: default));
        Assert.Equal(StatusCodes.Status403Forbidden, get.StatusCode);
    }

    [Fact]
    public async Task ListAndGet_DenyConversation_WhenCapturedRunScopeIsNoLongerAccessible()
    {
        var conversation = Conversation(null);
        var researchStore = DispatchProxy.Create<IProcessResearchStore, ResearchStoreProxy>();
        var run = new AgentRunSnapshot
        {
            RunId = Guid.CreateVersion7().ToString(),
            UserId = "operator",
            EntryPoint = ProductEntryPoints.Chat,
            Purpose = RunPurposes.ReadOnlyAnalysis,
            Question = "历史问题",
            AccessScope = new AgentRunAccessScopeSnapshot { SiteIds = ["SITE-A"] },
            Mode = "quick",
            Status = AgentRunStatuses.Completed,
            ModelProvider = "test",
            Model = "test",
            PromptVersion = "test",
            ToolsetVersion = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            Usage = new AgentUsageSummary()
        };
        var controller = Controller(conversation, researchStore, Identity("operator", "SITE-B"), [run]);

        var list = Assert.IsType<OkObjectResult>(await controller.List(ct: default));
        Assert.Empty(Assert.IsType<ChatConversationPage>(list.Value).Items);

        var get = Assert.IsType<ObjectResult>(await controller.Get(conversation.ConversationId, ct: default));
        Assert.Equal(StatusCodes.Status403Forbidden, get.StatusCode);
    }

    private static ChatConversationsController Controller(
        ChatConversationSummary conversation,
        IProcessResearchStore researchStore,
        ClaimsPrincipal principal,
        IReadOnlyList<AgentRunSnapshot>? runs = null)
    {
        var context = new DefaultHttpContext { User = principal };
        var runtime = DispatchProxy.Create<IAgentRuntime, RuntimeProxy>();
        ((RuntimeProxy)(object)runtime).Runs = runs ?? [];
        return new ChatConversationsController(
            new ChatConversationApplication(new ReadOnlyConversationStore(conversation), new NoopRunGateway()),
            new ProcessResearchQueries(researchStore),
            runtime,
            new PlatformUserResolver(new ProductionEnvironment()))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static ChatConversationSummary Conversation(Guid? projectId) => new()
    {
        ConversationId = Guid.CreateVersion7().ToString(),
        Title = "站点范围会话",
        PageContext = projectId is null ? null : new PageContextRef { Kind = "research-project", Id = projectId.Value.ToString() },
        Status = ChatConversationStatuses.Active,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
        LastMessageAt = DateTimeOffset.UtcNow
    };

    private static ResearchProject Project(Guid projectId, string siteCode) => new()
    {
        ProjectId = projectId,
        Code = "chat-site-scope",
        Name = "Chat site scope",
        ProcessName = "test-process",
        OwnerUserId = "operator",
        SiteCode = siteCode
    };

    private static ClaimsPrincipal Identity(string userId, string siteId)
        => new(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(PlatformClaimTypes.SiteId, siteId)
            ],
            "test"));

    private sealed class ReadOnlyConversationStore(ChatConversationSummary conversation) : IChatConversationStore
    {
        public Task<ChatConversationSummary?> GetAsync(string conversationId, string userId, CancellationToken ct = default)
            => Task.FromResult<ChatConversationSummary?>(conversation.ConversationId == conversationId ? conversation : null);

        public Task<ChatConversationPage> ListAsync(string userId, DateTimeOffset? before, int limit, CancellationToken ct = default)
            => Task.FromResult(new ChatConversationPage { Items = [conversation] });

        public Task<ChatConversationDetail?> GetDetailAsync(
            string conversationId,
            string userId,
            long? beforeSequence,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult<ChatConversationDetail?>(conversation.ConversationId == conversationId
                ? new ChatConversationDetail { Conversation = conversation }
                : null);

        public Task<ChatTurnReservation> CreateConversationWithTurnAsync(ChatConversationSummary value, string userId, string clientMessageId, string text, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ChatTurnReservation> AppendTurnAsync(string conversationId, string userId, string clientMessageId, string text, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task BindRunAsync(string assistantMessageId, string runId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task CompleteAssistantMessageAsync(AgentRunSnapshot run, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task FailAssistantMessageAsync(string assistantMessageId, string error, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string conversationId, string userId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopRunGateway : IChatRunGateway
    {
        public Task<AgentRunSnapshot> StartAsync(string userId, CreateChatRunRequest request, AgentRunAccessScopeSnapshot accessScope, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteConversationAsync(string conversationId, string userId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    public class ResearchStoreProxy : DispatchProxy
    {
        public ResearchProject? Project { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == nameof(IProcessResearchStore.GetProjectAsync)
                ? Task.FromResult(Project)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    public class RuntimeProxy : DispatchProxy
    {
        public IReadOnlyList<AgentRunSnapshot> Runs { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod?.Name == nameof(IAgentRuntime.GetConversationAsync)
                ? Task.FromResult(Runs)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ingot.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
