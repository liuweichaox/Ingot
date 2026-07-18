using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ingot.Connector.Builder;

namespace Ingot.Agent.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngotAgentInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<SqliteAgentStore>();
        services.AddSingleton<IAgentRunStore>(provider => provider.GetRequiredService<SqliteAgentStore>());
        services.AddSingleton<IAgentArtifactStore>(provider => provider.GetRequiredService<SqliteAgentStore>());
        services.AddSingleton<IAnalysisTool, DraftConnectorSpecificationTool>();
        services.AddIngotConnectorBuilder(configuration);
        services.AddHostedService<AgentRunStoreInitializerHostedService>();

        var provider = configuration["Agent:Provider"];
        if (configuration.GetValue<bool>("Agent:Enabled") &&
            string.Equals(provider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IModelClient, AgentFrameworkOpenAiModelClient>();
        var chatProvider = configuration["Chat:Provider"];
        if (configuration.GetValue<bool>("Chat:Enabled") &&
            string.Equals(chatProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IModelClient, ChatFrameworkOpenAiModelClient>();
        return services;
    }
}
