using Ingot.Agent;
using Ingot.Agent.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ingot.Core.Tests.Agent;

[CollectionDefinition(EnvironmentVariableCollection.Name, DisableParallelization = true)]
public sealed class EnvironmentVariableCollection
{
    public const string Name = "Environment variables";
}

[Collection(EnvironmentVariableCollection.Name)]
public sealed class ModelClientRegistrationTests
{
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

        Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IModelClient));
        Assert.IsType<DeterministicModelClient>(provider.GetRequiredService<IModelClient>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RegistrationOrder_WhenOpenAiIsEnabled_ResolvesOpenAiClient(bool providersFirst)
    {
        var previousApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "registration-test-key");
            var services = BuildServices(
                new Dictionary<string, string?>
                {
                    ["Chat:Enabled"] = "true",
                    ["Chat:Provider"] = "OpenAI",
                    ["Chat:FastModel"] = "test-fast",
                    ["Chat:ReasoningModel"] = "test-reasoning",
                    ["Chat:ProbeOnStartup"] = "false"
                },
                providersFirst);

            using var provider = services.BuildServiceProvider();

            Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IModelClient));
            Assert.IsType<ChatFrameworkOpenAiModelClient>(provider.GetRequiredService<IModelClient>());
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", previousApiKey);
        }
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

        return services;
    }
}
