using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PlatformValidator = Ingot.Platform.Api.Configuration.ProductionConfigurationValidator;
using EdgeValidator = Ingot.Edge.ConnectorHost.Configuration.ProductionConfigurationValidator;
using Xunit;

namespace Ingot.Core.Tests.Configuration;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void Platform_RejectsMissingCredentials()
    {
        var configuration = Build(new Dictionary<string, string?>());
        Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
    }

    [Fact]
    public void Platform_AcceptsCompleteConfiguration()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["Authentication:Authority"] = "https://identity.example.com",
            ["Authentication:Audience"] = "ingot-platform",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Chat_AcceptsPlatformIdentityDataScope()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["Authentication:Authority"] = "https://identity.example.com",
            ["Authentication:Audience"] = "ingot-platform",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "OpenAI",
            ["Chat:FastModel"] = "chat-fast-model",
            ["Chat:ReasoningModel"] = "chat-reasoning-model",
            ["ChatDataAccess:Users:analyst:EdgeIds:0"] = "EDGE-001",
            ["OPENAI_API_KEY"] = "secret-store-value",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Chat_RejectsMissingPlatformUserScopes()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "OpenAI",
            ["Chat:FastModel"] = "chat-fast-model",
            ["Chat:ReasoningModel"] = "chat-reasoning-model",
            ["OPENAI_API_KEY"] = "secret-store-value",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("ChatDataAccess:Users must contain at least one platform user scope", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatDefaults_MatchPublishedLimits()
    {
        var chat = new Ingot.Agent.ChatOptions();

        Assert.Equal(8, chat.MaxToolCalls);
        Assert.Equal(60, chat.MaxRunSeconds);
    }

    [Fact]
    public void ConnectorHost_RejectsShortCredentials()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "short-connector-token",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "short-edge-token"
        });

        Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
    }

    [Fact]
    public void ConnectorHost_AcceptsCompleteConfiguration()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters"
        });

        EdgeValidator.Validate(configuration);
    }

    [Fact]
    public void ConnectorHost_RejectsInvalidPublicBaseUrl()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["Edge:EnablePlatformReporting"] = "true",
            ["Edge:PlatformApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:PublicBaseUrl"] = "connector-host:8001",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters"
        });

        var error = Assert.Throws<InvalidOperationException>(() => EdgeValidator.Validate(configuration));
        Assert.Contains("Edge:PublicBaseUrl", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_RejectsMissingUnifiedIdentityProvider()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["Authentication:Mode"] = "Oidc",     // OIDC 模式下才要求 Authority/Audience
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("Authentication:Authority", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Platform_AcceptsLocalAuthWithoutIdentityProvider()
    {
        // 内置本地认证（默认 Local 模式）不需要外部 OIDC —— 这是消除强制 OIDC 部署摩擦的关键。
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["InspectionAttachments:ArchiveRootPath"] = "/archive/inspection-attachments",
            ["ProcessKnowledge:ArchiveRootPath"] = "/archive/process-knowledge",
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
            // 无 Authentication:Mode → 默认 Local；无 Authority/Audience 也应通过校验
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void ConnectorHost_AcceptsLegacyCentralApiConfiguration()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "connector-token-with-at-least-24-characters",
            ["Edge:EnableCentralReporting"] = "true",
            ["Edge:CentralApiBaseUrl"] = "http://platform-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters"
        });

        EdgeValidator.Validate(configuration);
    }

    [Fact]
    public void DisabledChat_DoesNotConstructProviderClient()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Chat:Enabled"] = "false",
            ["Chat:Provider"] = "OpenAI",
            ["Chat:DatabasePath"] = Path.Combine(Path.GetTempPath(), $"ingot-disabled-chat-{Guid.NewGuid():N}.db")
        });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        Ingot.Agent.ServiceCollectionExtensions.AddIngotAgentCore(services, configuration);
        Ingot.Agent.Providers.ServiceCollectionExtensions.AddIngotAgentProviders(services, configuration);
        using var provider = services.BuildServiceProvider();

        var capabilities = provider.GetRequiredService<Ingot.Agent.IAgentRuntime>()
            .GetCapabilities(Ingot.Contracts.Agents.ProductEntryPoints.Chat);

        Assert.False(capabilities.Enabled);
        Assert.Empty(capabilities.Modes);
    }

    private static IConfiguration Build(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
