using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace Ingot.Agent.Providers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngotAgentProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<SqliteAgentStore>();
        services.AddSingleton<IAgentRunStore>(provider => provider.GetRequiredService<SqliteAgentStore>());
        services.AddHostedService<AgentRunStoreInitializerHostedService>();

        var chatProvider = configuration["Chat:Provider"];
        if (configuration.GetValue<bool>("Chat:Enabled") &&
            string.Equals(chatProvider, "OpenAI", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<IModelClient, ChatFrameworkOpenAiModelClient>();
        return services;
    }
}
