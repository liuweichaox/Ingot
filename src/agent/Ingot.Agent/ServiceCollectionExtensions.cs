// 注册独立于模型供应商的 Agent 核心运行时与安全校验器。
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
        services.TryAddSingleton<IModelServiceConfigurationProvider, UnconfiguredModelServiceConfigurationProvider>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelClient, DeterministicModelClient>());
        services.AddSingleton<IModelRouter, DefaultModelRouter>();
        services.AddSingleton<IPlanValidator, DefaultPlanValidator>();
        services.AddSingleton<IAnalysisResultValidator, DefaultAnalysisResultValidator>();
        services.AddSingleton<ICombinedAnalysisWorkflow, BoundedCombinedAnalysisWorkflow>();
        services.TryAddSingleton<IAgentRunLifecycleSink, NullAgentRunLifecycleSink>();
        services.AddSingleton<IAgentRuntime, AgentRuntime>();
        services.AddSingleton<IAgentRunProcessor>(provider =>
            (AgentRuntime)provider.GetRequiredService<IAgentRuntime>());
        return services;
    }
}
