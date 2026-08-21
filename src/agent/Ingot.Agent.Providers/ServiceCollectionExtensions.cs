using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
namespace Ingot.Agent.Providers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngotAgentProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddHostedService<AgentRunStoreInitializerHostedService>();

        var useOpenAi = configuration.GetValue<bool>("Chat:Enabled") &&
                        string.Equals(
                            configuration["Chat:Provider"],
                            "OpenAI",
                            StringComparison.OrdinalIgnoreCase);
        services.TryAddSingleton<DeterministicModelClient>();
        if (useOpenAi)
        {
            services.TryAddSingleton<ChatFrameworkOpenAiModelClient>();
            services.AddHttpClient(nameof(OpenAiCompatibleCapabilityProbe));
            services.AddHostedService<OpenAiCompatibleCapabilityProbe>();
        }

        services.Replace(ServiceDescriptor.Singleton<IModelClient>(provider => useOpenAi
            ? provider.GetRequiredService<ChatFrameworkOpenAiModelClient>()
            : provider.GetRequiredService<DeterministicModelClient>()));
        return services;
    }
}
