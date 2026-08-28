// 验证 Agent 的 ModelClientRegistration 能力、只读边界和拒绝路径。

using Ingot.Agent;
using Ingot.Agent.Providers;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class ModelClientRegistrationTests
{
    [Fact]
    public void Providers_DoNotRegisterAnAgentRunStore()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();

        services.AddIngotAgentProviders(configuration);

        Assert.DoesNotContain(services, static descriptor =>
            descriptor.ServiceType == typeof(IAgentRunStore));
    }

    [Fact]
    public void Providers_DoNotReplaceHostOwnedAgentRunStore()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        var marker = new MarkerRunStore();
        services.AddSingleton<IAgentRunStore>(marker);

        services.AddIngotAgentProviders(configuration);
        using var provider = services.BuildServiceProvider();

        Assert.Same(marker, provider.GetRequiredService<IAgentRunStore>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationOrder_WhenChatIsDisabled_ResolvesDeterministicClient(bool providersFirst)
    {
        var services = BuildServices(
            new Dictionary<string, string?>
            {
                ["Chat:Enabled"] = "false",
                ["Chat:Provider"] = "OpenAI"
            },
            providersFirst);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(2, services.Count(static descriptor => descriptor.ServiceType == typeof(IModelClient)));
        var client = provider.GetRequiredService<IModelRouter>()
            .GetClient(ProductEntryPoints.Chat, ModelRole.Fast, "Deterministic");
        Assert.IsType<DeterministicModelClient>(client);
    }

    [Theory]
    [InlineData(false, "Responses")]
    [InlineData(true, "Responses")]
    [InlineData(false, "ChatCompletions")]
    [InlineData(true, "ChatCompletions")]
    public void RegistrationOrder_WhenCompatibleProviderIsEnabled_UsesConfiguredProtocol(
        bool providersFirst,
        string protocol)
    {
        var services = BuildServices(
            new Dictionary<string, string?>
            {
                ["Chat:Enabled"] = "true",
                ["Chat:Provider"] = "AnyCompatibleProvider",
                ["Chat:Protocol"] = protocol,
                ["Chat:FastModel"] = "test-fast",
                ["Chat:ReasoningModel"] = "test-reasoning",
                ["Chat:ProbeOnStartup"] = "false"
            },
            providersFirst);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IModelRouter>()
            .GetClient(ProductEntryPoints.Chat, ModelRole.Fast, "AnyCompatibleProvider");

        Assert.Equal(2, services.Count(static descriptor => descriptor.ServiceType == typeof(IModelClient)));
        Assert.IsType<ChatFrameworkOpenAiModelClient>(client);
        Assert.Equal("AnyCompatibleProvider", client.Provider);
    }

    private static ServiceCollection BuildServices(
        IReadOnlyDictionary<string, string?> values,
        bool providersFirst)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        if (providersFirst)
        {
            services.AddIngotAgentProviders(configuration);
            services.AddIngotAgentCore(configuration);
        }
        else
        {
            services.AddIngotAgentCore(configuration);
            services.AddIngotAgentProviders(configuration);
        }
        services.AddSingleton<IModelServiceConfigurationProvider>(
            new TestModelServiceConfigurationProvider(new ModelServiceConnectionSettings
            {
                Enabled = configuration.GetValue<bool>("Chat:Enabled"),
                Provider = configuration["Chat:Provider"] ?? "Deterministic",
                Protocol = configuration["Chat:Protocol"] ?? "Responses",
                FastModel = configuration["Chat:FastModel"] ?? "deterministic-v1",
                ReasoningModel = configuration["Chat:ReasoningModel"] ?? "deterministic-v1",
                ApiKey = "registration-test-key"
            }));

        return services;
    }

    private sealed class TestModelServiceConfigurationProvider(ModelServiceConnectionSettings settings)
        : IModelServiceConfigurationProvider
    {
        public ModelServiceConnectionSettings Current { get; } = settings;
    }

    private sealed class MarkerRunStore : IAgentRunStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task CreateAsync(AgentRunSnapshot run, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AgentRunSnapshot?> GetAsync(string runId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSnapshot>> ListAsync(string entryPoint, string userId, DateTimeOffset? before, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSnapshot>> ListConversationAsync(string entryPoint, string userId, string conversationId, int limit, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(AgentRunSnapshot run, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string runId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteConversationAsync(string entryPoint, string userId, string conversationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AgentStreamEvent> AppendEventAsync(string runId, string type, object? data, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentStreamEvent>> ReadEventsAsync(string runId, long afterSequence, int limit, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
