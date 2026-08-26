// 验证持久化 Chat 运行只能在创建时捕获的站点授权仍被当前身份覆盖时读取。

using System.Security.Claims;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Application.ProcessResearch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ChatRunSiteScopeTests
{
    [Fact]
    public async Task ReadEndpoints_DenySameOwner_WhenCurrentIdentityCannotAccessCapturedSite()
    {
        var run = Snapshot("site-a", "operator", Scope("SITE-A"));
        var runtime = new RecordingRuntime([run]);
        var controller = Controller(runtime, Identity("operator", ["SITE-B"]));

        var get = Assert.IsType<ObjectResult>(await controller.Get(run.RunId, default));
        Assert.Equal(StatusCodes.Status403Forbidden, get.StatusCode);

        var list = Assert.IsType<OkObjectResult>(await controller.List(ct: default));
        Assert.Empty(Assert.IsType<ChatRunPage>(list.Value).Items);

        await controller.Stream(run.RunId, default);
        Assert.Equal(StatusCodes.Status403Forbidden, controller.Response.StatusCode);
        Assert.Equal(0, runtime.StreamCallCount);
    }

    [Fact]
    public async Task ReadEndpoints_DenyLegacyRunWithoutCapturedScope()
    {
        var run = Snapshot("legacy", "operator", accessScope: null);
        var controller = Controller(
            new RecordingRuntime([run]),
            Identity("operator", ["SITE-A", "SITE-B"]));

        var get = Assert.IsType<ObjectResult>(await controller.Get(run.RunId, default));
        Assert.Equal(StatusCodes.Status403Forbidden, get.StatusCode);

        var list = Assert.IsType<OkObjectResult>(await controller.List(ct: default));
        Assert.Empty(Assert.IsType<ChatRunPage>(list.Value).Items);
    }

    [Fact]
    public async Task Get_DeniesAllowAllRun_AfterOwnerLosesAdministratorRole()
    {
        var run = Snapshot("former-admin", "operator", new AgentRunAccessScopeSnapshot
        {
            AllowAllSites = true
        });
        var controller = Controller(
            new RecordingRuntime([run]),
            Identity("operator", ["SITE-A"]));

        var result = Assert.IsType<ObjectResult>(await controller.Get(run.RunId, default));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task ReadEndpoints_AllowOwner_WhenCurrentScopeIsCapturedScopeSuperset()
    {
        var run = Snapshot("still-authorized", "operator", Scope("SITE-A"));
        var controller = Controller(
            new RecordingRuntime([run]),
            Identity("operator", ["SITE-A", "SITE-B"]));

        var get = Assert.IsType<OkObjectResult>(await controller.Get(run.RunId, default));
        Assert.Equal(run.RunId, Assert.IsType<ChatRunSnapshot>(get.Value).RunId);

        var list = Assert.IsType<OkObjectResult>(await controller.List(ct: default));
        var item = Assert.Single(Assert.IsType<ChatRunPage>(list.Value).Items);
        Assert.Equal(run.RunId, item.RunId);
    }

    private static ChatRunsController Controller(RecordingRuntime runtime, ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext
        {
            User = principal,
            Response = { Body = new MemoryStream() }
        };
        return new ChatRunsController(
            runtime,
            new ProcessResearchQueries(null!),
            new PlatformUserResolver(new ProductionEnvironment()))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };
    }

    private static ClaimsPrincipal Identity(
        string userId,
        IReadOnlyList<string> siteIds,
        params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        claims.AddRange(siteIds.Select(static siteId => new Claim(PlatformClaimTypes.SiteId, siteId)));
        claims.AddRange(roles.Select(static role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    private static AgentRunAccessScopeSnapshot Scope(params string[] siteIds) => new()
    {
        SiteIds = siteIds
    };

    private static AgentRunSnapshot Snapshot(
        string runId,
        string userId,
        AgentRunAccessScopeSnapshot? accessScope) => new()
    {
        RunId = runId,
        UserId = userId,
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Question = "核对站点数据",
        AccessScope = accessScope,
        Mode = "quick",
        Status = AgentRunStatuses.Completed,
        ModelProvider = "test",
        Model = "test-model",
        PromptVersion = "test-prompt",
        ToolsetVersion = "test-tools",
        CreatedAt = DateTimeOffset.UtcNow,
        CompletedAt = DateTimeOffset.UtcNow,
        Answer = new AnalysisAnswer { Summary = "完成" },
        Usage = new AgentUsageSummary()
    };

    private sealed class RecordingRuntime(IReadOnlyList<AgentRunSnapshot> runs) : IAgentRuntime
    {
        public int StreamCallCount { get; private set; }

        public AgentCapabilities GetCapabilities(string entryPoint) => throw new NotSupportedException();

        public Task<AgentRunPage> ListAsync(
            string entryPoint,
            string userId,
            DateTimeOffset? before,
            int limit,
            CancellationToken ct = default)
            => Task.FromResult(new AgentRunPage
            {
                Items = runs
                    .Where(run => string.Equals(run.UserId, userId, StringComparison.OrdinalIgnoreCase))
                    .Select(static run => new AgentRunListItem
                    {
                        RunId = run.RunId,
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
                    })
                    .ToArray()
            });

        public Task<AgentRunSnapshot> StartAsync(
            string entryPoint,
            string userId,
            CreateChatRunRequest request,
            AgentAccessScope accessScope,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AgentRunSnapshot?> GetAsync(
            string entryPoint,
            string runId,
            CancellationToken ct = default)
            => Task.FromResult(runs.FirstOrDefault(run => run.RunId == runId));

        public IAsyncEnumerable<AgentStreamEvent> StreamAsync(
            string entryPoint,
            string runId,
            long afterSequence = 0,
            CancellationToken ct = default)
        {
            StreamCallCount++;
            return EmptyStream();
        }

        public Task<bool> CancelAsync(
            string entryPoint,
            string runId,
            string userId,
            string reason,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(
            string entryPoint,
            string runId,
            string userId,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        private static async IAsyncEnumerable<AgentStreamEvent> EmptyStream()
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ingot.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
