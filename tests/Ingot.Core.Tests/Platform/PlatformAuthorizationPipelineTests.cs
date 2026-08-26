// 验证平台组件 PlatformAuthorizationPipeline 的成功、拒绝和安全边界。

using System.Net;
using Ingot.Contracts.Analytics;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Ingot.Contracts.Events;
using Ingot.Platform.Application.Events;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class PlatformAuthorizationPipelineTests : IClassFixture<PlatformAuthorizationPipelineTests.Factory>
{
    private readonly HttpClient client;

    public PlatformAuthorizationPipelineTests(Factory factory)
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task AnonymousResearchRequest_IsRejectedByRealAuthorizationMiddleware()
    {
        var response = await client.GetAsync("/api/v1/research-projects");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnonymousAuthenticationConfiguration_IsAvailable()
    {
        var response = await client.GetAsync("/api/v1/auth/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"mode\":\"local\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnonymousOidcConfiguration_ExposesOnlySpaBootstrapValues()
    {
        await using var factory = new Factory();
        await using var oidcFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Authentication:Mode", "Oidc");
            builder.UseSetting("Authentication:Authority", "https://identity.example.com/tenant/");
            builder.UseSetting("Authentication:Audience", "private-platform-audience");
            builder.UseSetting("Authentication:Oidc:ClientId", "ingot-platform-spa");
            builder.UseSetting("Authentication:Oidc:Scope", "openid profile ingot-platform-api");
        });
        using var oidcClient = oidcFactory.CreateClient();

        var response = await oidcClient.GetAsync("/api/v1/auth/config");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"mode\":\"oidc\"", body);
        Assert.Contains("\"authority\":\"https://identity.example.com/tenant\"", body);
        Assert.Contains("\"clientId\":\"ingot-platform-spa\"", body);
        Assert.Contains("\"callbackPath\":\"/auth/callback\"", body);
        Assert.DoesNotContain("private-platform-audience", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AuthenticatedUserWithoutPlatformRole_IsForbiddenByRealAuthorizationMiddleware()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/research-projects");
        request.Headers.Add(TestAuthenticationHandler.RoleHeaderName, "unrelated.role");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<Ingot.Platform.Api.Controllers.ResearchProjectsController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting(
                "ConnectionStrings:Events",
                "Host=127.0.0.1;Port=1;Database=ingot_pipeline_test;Username=ingot;Password=ingot;Timeout=1");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IPlatformEventStore>();
                services.AddSingleton<IPlatformEventStore, AvailableEventStore>();
                services.AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        static _ => { });
            });
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "PipelineTest";
        public const string RoleHeaderName = "X-Test-Role";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var roles = Request.Headers[RoleHeaderName]
                .SelectMany(static value => value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [])
                .ToArray();
            if (roles.Length == 0)
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "pipeline-user"),
                new(ClaimTypes.Name, "pipeline-user")
            };
            claims.AddRange(roles.Select(static role => new Claim(ClaimTypes.Role, role)));
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class AvailableEventStore : IPlatformEventStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<EventBatchResponse> IngestAsync(EventBatchRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlatformProductionEvent>>([]);

        public Task<IReadOnlyList<PlatformProductionEvent>> QueryByExecutionIdsAsync(
            IReadOnlyCollection<string> executionIds,
            string siteId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlatformProductionEvent>>([]);

        public Task<IReadOnlyList<PlatformProcessExecutionSummarySource>> QueryExecutionSummarySourcesAsync(
            IReadOnlyCollection<string> executionIds,
            string siteId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlatformProcessExecutionSummarySource>>([]);

        public Task<PlatformEventScopeStats> GetScopeStatsAsync(
            PlatformEventQuery query,
            CancellationToken ct = default)
            => Task.FromResult(new PlatformEventScopeStats());

        public Task<bool> CanConnectAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<DataObjectPage> QueryDataObjectsAsync(DataObjectQuery query, CancellationToken ct = default)
            => Task.FromResult(new DataObjectPage { Limit = query.Limit, Offset = query.Offset });
    }
}
