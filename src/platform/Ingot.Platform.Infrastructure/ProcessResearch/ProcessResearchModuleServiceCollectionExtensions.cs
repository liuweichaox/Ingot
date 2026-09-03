// 注册配方优化及其基础设施适配器的模块组合根。
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>为 Platform 宿主注册工艺优化模块的应用服务和基础设施实现。</summary>
public static class ProcessResearchModuleServiceCollectionExtensions
{
    public static IServiceCollection AddIngotProcessResearch(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<IProcessResearchStore, PostgresProcessResearchStore>();
        services.AddSingleton<ProcessResearchQueries>();
        services.AddSingleton<IResearchProjectContextReader, ResearchProjectContextReader>();
        services.AddSingleton<ProcessResearchWorkflow>();
        services.AddSingleton<IResearchObservationAssembler, ResearchObservationAssembler>();
        services.Configure<ProcessOptimizerOptions>(configuration.GetSection("ProcessOptimizer"));
        services.AddTransient<ProcessOptimizerCircuitBreakerHandler>();
        services.AddHttpClient<IProcessOptimizerClient, ProcessOptimizerClient>((provider, client) =>
        {
            var options = provider.GetRequiredService<IOptions<ProcessOptimizerOptions>>().Value;
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseAddress))
                throw new InvalidOperationException("ProcessOptimizer:BaseUrl 必须是绝对 URL。");
            client.BaseAddress = new Uri(
                baseAddress.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                    ? baseAddress.AbsoluteUri
                    : $"{baseAddress.AbsoluteUri}/");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(options.RequestTimeoutSeconds, 1, 300));
        }).AddHttpMessageHandler<ProcessOptimizerCircuitBreakerHandler>();
        return services;
    }
}
