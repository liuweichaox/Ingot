
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ingot.Agent;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngotAgentCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ChatOptions>(configuration.GetSection("Chat"));
        services.TryAddSingleton<DeterministicModelClient>();
        services.TryAddSingleton<IModelClient>(static provider =>
            provider.GetRequiredService<DeterministicModelClient>());
        services.AddSingleton<IModelRouter, DefaultModelRouter>();
        services.AddSingleton<IPlanValidator, DefaultPlanValidator>();
        services.AddSingleton<IAnalysisResultValidator, DefaultAnalysisResultValidator>();
        services.AddSingleton<ICombinedAnalysisWorkflow, BoundedCombinedAnalysisWorkflow>();
        services.AddSingleton<IAgentRuntime, AgentRuntime>();
        return services;
    }
}
