using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ingot.Agent;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngotAgentCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AgentOptions>(configuration.GetSection("Agent"));
        services.Configure<ChatOptions>(configuration.GetSection("Chat"));
        services.AddSingleton<DeterministicModelClient>();
        services.AddSingleton<IModelClient>(static provider =>
            provider.GetRequiredService<DeterministicModelClient>());
        services.AddSingleton<IModelRouter, DefaultModelRouter>();
        services.AddSingleton<IPlanValidator, DefaultPlanValidator>();
        services.AddSingleton<IEvidenceVerifier, DefaultEvidenceVerifier>();
        services.AddSingleton<IInvestigationWorkflow, BoundedInvestigationWorkflow>();
        services.AddSingleton<IAgentRuntime, AgentRuntime>();
        return services;
    }
}
