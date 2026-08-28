// 注册通用模型客户端、能力探查和 Agent 运行持久化初始化。
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

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelClient, DeterministicModelClient>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IModelClient, ChatFrameworkOpenAiModelClient>());
        services.AddHttpClient(nameof(OpenAiCompatibleCapabilityProbe));
        services.AddHostedService<OpenAiCompatibleCapabilityProbe>();
        return services;
    }
}
