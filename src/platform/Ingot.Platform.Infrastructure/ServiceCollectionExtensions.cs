using Ingot.Agent;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.Services;
using Ingot.Platform.Infrastructure.Webhooks;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure;

/// <summary>
///     中心侧基础设施的组合入口。宿主只调用本方法完成注册，保持纯组合根。
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngotPlatformInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // 边缘注册表（SQLite）
        services.AddSingleton<EdgeRegistry>();

        // 事件生产记录库（PostgreSQL）
        services.Configure<PlatformEventOptions>(configuration.GetSection("EventIngest"));
        services.AddSingleton<PlatformEventMetrics>();
        services.AddSingleton<IPlatformEventStore, PostgresPlatformEventStore>();
        services.AddHostedService<EventStoreInitializerHostedService>();

        // Chat 只能通过显式注册的只读工具访问中心数据。
        services.Configure<ChatDataAccessOptions>(configuration.GetSection("ChatDataAccess"));
        services.AddSingleton<IChatEventReader, ChatEventReader>();
        services.AddSingleton<IAnalysisTool, CheckDataQualityTool>();
        services.AddSingleton<IAnalysisTool, GetCycleTraceTool>();
        services.AddSingleton<IAnalysisTool, FindComparableCyclesTool>();
        services.AddSingleton<IAnalysisTool, CompareCyclesTool>();

        // 人工检测结果记录（PostgreSQL）；与生产事件分表、分 API 建模
        services.Configure<InspectionSubmissionOptions>(configuration.GetSection("InspectionSubmission"));
        services.Configure<InspectionAttachmentOptions>(configuration.GetSection("InspectionAttachments"));
        services.AddSingleton<IInspectionRecordStore, PostgresInspectionRecordStore>();
        services.AddSingleton<IInspectionAttachmentStore, PostgresInspectionAttachmentStore>();
        services.AddSingleton<IInspectionMasterDataStore, PostgresInspectionMasterDataStore>();
        services.AddHostedService<InspectionStoreInitializerHostedService>();
        services.AddSingleton<ICycleAnalyticsStore, PostgresCycleAnalyticsStore>();
        services.AddHostedService<CycleAnalyticsInitializerHostedService>();

        // Webhook 订阅与投递（PostgreSQL + CloudEvents）
        services.Configure<WebhookOptions>(configuration.GetSection("Webhook"));
        services.AddHttpClient("webhook", (provider, client) =>
        {
            var webhookOptions = provider.GetRequiredService<IOptions<WebhookOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(webhookOptions.RequestTimeoutSeconds, 1, 300));
        });
        services.AddSingleton<IWebhookSubscriptionStore, PostgresWebhookSubscriptionStore>();
        services.AddSingleton<WebhookDispatcher>();
        services.AddSingleton<WebhookMetrics>();
        services.AddHostedService<WebhookStoreInitializerHostedService>();
        services.AddHostedService<WebhookDeliveryHostedService>();

        return services;
    }
}
