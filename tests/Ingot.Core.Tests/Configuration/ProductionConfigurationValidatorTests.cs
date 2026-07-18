using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CentralValidator = Ingot.Central.Api.Configuration.ProductionConfigurationValidator;
using EdgeValidator = Ingot.Connector.Host.Configuration.ProductionConfigurationValidator;
using Xunit;

namespace Ingot.Core.Tests.Configuration;

public sealed class ProductionConfigurationValidatorTests
{
    [Fact]
    public void Central_RejectsMissingCredentials()
    {
        var configuration = Build(new Dictionary<string, string?>());
        Assert.Throws<InvalidOperationException>(() => CentralValidator.Validate(configuration));
    }

    [Fact]
    public void Central_AcceptsCompleteConfiguration()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["InspectionSubmission:RequireToken"] = "true",
            ["InspectionSubmission:ActorTokens:OPERATOR-001"] = "operator-token-with-at-least-24-characters",
            ["Agent:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        CentralValidator.Validate(configuration);
    }

    [Fact]
    public void Central_RejectsDeterministicProviderInProduction()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["InspectionSubmission:RequireToken"] = "true",
            ["InspectionSubmission:ActorTokens:OPERATOR-001"] = "operator-token-with-at-least-24-characters",
            ["Agent:Enabled"] = "true",
            ["Agent:Provider"] = "Deterministic",
            ["Agent:RequireToken"] = "true",
            ["Agent:ActorTokens:OPERATOR-001"] = "agent-token-with-at-least-24-characters",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => CentralValidator.Validate(configuration));
        Assert.Contains("Agent:Provider must be OpenAI", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Central_AcceptsConfiguredOpenAiProviderInProduction()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["InspectionSubmission:RequireToken"] = "true",
            ["InspectionSubmission:ActorTokens:OPERATOR-001"] = "operator-token-with-at-least-24-characters",
            ["Agent:Enabled"] = "true",
            ["Agent:Provider"] = "OpenAI",
            ["Agent:FastModel"] = "fast-model",
            ["Agent:ReasoningModel"] = "reasoning-model",
            ["Agent:RequireToken"] = "true",
            ["Agent:ActorTokens:operator"] = "agent-token-with-at-least-24-characters",
            ["Agent:PackagingApprovers:0"] = "operator",
            ["OPENAI_API_KEY"] = "secret-store-value",
            ["ConnectorBuilder:ContainerWorkspaceVolume"] = "ingot-connector-workspaces",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        CentralValidator.Validate(configuration);
    }

    [Fact]
    public void Central_RejectsAgentWithoutConnectorWorkspaceVolume()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Events"] = "Host=postgres;Database=ingot",
            ["EventIngest:RequireToken"] = "true",
            ["EventIngest:EdgeTokens:EDGE-001"] = "edge-token-with-at-least-24-characters",
            ["InspectionSubmission:RequireToken"] = "true",
            ["InspectionSubmission:ActorTokens:OPERATOR-001"] = "operator-token-with-at-least-24-characters",
            ["Agent:Enabled"] = "true",
            ["Agent:Provider"] = "OpenAI",
            ["Agent:FastModel"] = "fast-model",
            ["Agent:ReasoningModel"] = "reasoning-model",
            ["Agent:RequireToken"] = "true",
            ["Agent:ActorTokens:operator"] = "agent-token-with-at-least-24-characters",
            ["OPENAI_API_KEY"] = "secret-store-value",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => CentralValidator.Validate(configuration));
        Assert.Contains("ConnectorBuilder:ContainerWorkspaceVolume is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentDefaults_MatchPublishedExecutionLimits()
    {
        var options = new Ingot.Agent.AgentOptions();

        Assert.Equal(24, options.MaxToolCalls);
        Assert.Equal(8, options.MaxIterations);
        Assert.Equal(300, options.MaxRunSeconds);
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
            ["Agent:Enabled"] = "false",
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

        CentralValidator.Validate(configuration);
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
            ["Agent:Enabled"] = "false",
            ["Chat:Enabled"] = "true",
            ["Chat:Provider"] = "OpenAI",
            ["Chat:FastModel"] = "chat-fast-model",
            ["Chat:ReasoningModel"] = "chat-reasoning-model",
            ["Chat:RequireToken"] = "true",
            ["Chat:ActorTokens:analyst"] = "chat-token-with-at-least-24-characters",
            ["OPENAI_API_KEY"] = "secret-store-value",
            ["Cors:AllowedOrigins:0"] = "https://ingotstack.com"
        });

        var error = Assert.Throws<InvalidOperationException>(() => CentralValidator.Validate(configuration));
        Assert.Contains("ChatDataAccess:Actors:analyst is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatDefaults_AreIndependentFromAgentDefaults()
    {
        var chat = new Ingot.Agent.ChatOptions();
        var agent = new Ingot.Agent.AgentOptions();

        Assert.Equal(8, chat.MaxToolCalls);
        Assert.Equal(60, chat.MaxRunSeconds);
        Assert.NotEqual(agent.MaxToolCalls, chat.MaxToolCalls);
        Assert.NotSame(agent.ActorTokens, chat.ActorTokens);
        Assert.NotSame(agent.ModelPricing, chat.ModelPricing);
    }

    [Fact]
    public void ConnectorHost_RejectsShortCredentials()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["ConnectorHost:IngestToken"] = "short-connector-token",
            ["Edge:EnableCentralReporting"] = "true",
            ["Edge:CentralApiBaseUrl"] = "http://central-api:8000",
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
            ["Edge:EnableCentralReporting"] = "true",
            ["Edge:CentralApiBaseUrl"] = "http://central-api:8000",
            ["Edge:EnableEventShipping"] = "true",
            ["Edge:EventIngestToken"] = "edge-token-with-at-least-24-characters"
        });

        EdgeValidator.Validate(configuration);
    }

    [Fact]
    public void DisabledAgent_DoesNotConstructProviderClient()
    {
        var configuration = Build(new Dictionary<string, string?>
        {
            ["Agent:Enabled"] = "false",
            ["Agent:Provider"] = "OpenAI",
            ["Agent:DatabasePath"] = Path.Combine(Path.GetTempPath(), $"ingot-disabled-agent-{Guid.NewGuid():N}.db")
        });
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        Ingot.Agent.ServiceCollectionExtensions.AddIngotAgentCore(services, configuration);
        Ingot.Agent.Infrastructure.ServiceCollectionExtensions.AddIngotAgentInfrastructure(services, configuration);
        using var provider = services.BuildServiceProvider();

        var capabilities = provider.GetRequiredService<Ingot.Agent.IAgentRuntime>()
            .GetCapabilities(Ingot.Contracts.Agents.ProductSurfaces.Agent);

        Assert.False(capabilities.Enabled);
        Assert.Empty(capabilities.Modes);
    }

    private static IConfiguration Build(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
