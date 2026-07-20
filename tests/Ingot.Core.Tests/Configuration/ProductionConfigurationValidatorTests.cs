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
            ["InspectionSubmission:RequireToken"] = "true",
            ["InspectionSubmission:ActorTokens:OPERATOR-001"] = "operator-token-with-at-least-24-characters",
            ["Chat:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Chat_AcceptsIndependentCredentialsAndDataScope()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["InspectionSubmission:RequireToken"] = "true",
            ["InspectionSubmission:ActorTokens:OPERATOR-001"] = "operator-token-with-at-least-24-characters",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "OpenAI",
            ["Chat:FastModel"] = "chat-fast-model",
            ["Chat:ReasoningModel"] = "chat-reasoning-model",
            ["Chat:RequireToken"] = "true",
            ["Chat:ActorTokens:analyst"] = "chat-token-with-at-least-24-characters",
            ["ChatDataAccess:Actors:analyst:EdgeIds:0"] = "EDGE-001",
            ["OPENAI_API_KEY"] = "secret-store-value",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        PlatformValidator.Validate(configuration);
    }

    [Fact]
    public void Chat_RejectsActorWithoutDataScope()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["InspectionSubmission:RequireToken"] = "true",
            ["InspectionSubmission:ActorTokens:OPERATOR-001"] = "operator-token-with-at-least-24-characters",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "OpenAI",
            ["Chat:FastModel"] = "chat-fast-model",
            ["Chat:ReasoningModel"] = "chat-reasoning-model",
            ["Chat:RequireToken"] = "true",
            ["Chat:ActorTokens:analyst"] = "chat-token-with-at-least-24-characters",
            ["OPENAI_API_KEY"] = "secret-store-value",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => PlatformValidator.Validate(configuration));
        Assert.Contains("ChatDataAccess:Actors:analyst is required", error.Message, StringComparison.Ordinal);
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
            .GetCapabilities(Ingot.Contracts.Agents.ProductSurfaces.Chat);

        Assert.False(capabilities.Enabled);
        Assert.Empty(capabilities.Modes);
    }

    private static IConfiguration Build(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
