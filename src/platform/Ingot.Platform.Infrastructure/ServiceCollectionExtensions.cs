using Ingot.Agent;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.Acquisition;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.Manufacturing;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Migrations;
using Ingot.Platform.Infrastructure.ProcessImprovement;
using Ingot.Platform.Infrastructure.Services;
using Ingot.Platform.Infrastructure.TimeSeries;
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
        // 版本化数据库迁移：必须是第一个 HostedService——schema 在一切 Store 初始化器
        // 与业务后台服务之前就绪。各 Store 的启动 DDL 与基线逐字一致（无害冗余），
        // 作为过渡期保留，后续批次移除。
        services.AddSingleton<MigrationRunner>();
        services.AddHostedService<MigrationHostedService>();

        // 边缘注册表（SQLite）
        services.AddSingleton<EdgeRegistry>();

        // 事件生产记录库（PostgreSQL）
        // 生产上下文必须先于事件库就绪；cycle.started 会解析并固化当时有效的工装与配方引用。
        services.AddSingleton<IManufacturingContextStore, PostgresManufacturingContextStore>();
        services.AddHostedService<ManufacturingContextInitializerHostedService>();
        services.AddSingleton<ICycleAnalysisMaterializationStore, PostgresCycleAnalysisMaterializationStore>();
        services.AddSingleton<CycleAnalysisRecomputeQueue>();
        services.AddSingleton<IFeatureDefinitionRegistry, BuiltInFeatureDefinitionRegistry>();
        services.AddSingleton<WholeCycleAnalysisEngine>();
        services.AddSingleton<PostgresCycleScientificComputeEngine>();
        services.AddSingleton<CycleAnalysisMaterializer>();
        services.AddHostedService<CycleAnalysisMaterializationInitializerHostedService>();
        services.Configure<PlatformEventOptions>(configuration.GetSection("EventIngest"));
        services.AddSingleton<PlatformEventMetrics>();
        services.AddSingleton<PostgresTimeSeriesStore>();
        services.AddSingleton<ITimeSeriesStore>(
            provider => provider.GetRequiredService<PostgresTimeSeriesStore>());
        services.AddHostedService<TimeSeriesStoreInitializerHostedService>();
        services.AddSingleton<IPlatformEventStore, PostgresPlatformEventStore>();
        services.AddHostedService<EventStoreInitializerHostedService>();
        // 幂等键修剪（EventIngest:KeyRetentionDays > 0 时启用）：
        // 事件表有保留策略而键表此前无清理机制，会无限增长。
        services.AddHostedService<EventIngestKeyPruneHostedService>();

        // Chat 只能通过显式注册的只读工具访问中心数据。
        services.Configure<ChatDataAccessOptions>(configuration.GetSection("ChatDataAccess"));
        services.AddSingleton<IChatEventReader, ChatEventReader>();
        services.AddSingleton<IAnalysisTool, CheckDataQualityTool>();
        services.AddSingleton<IAnalysisTool, GetCycleTraceTool>();
        services.AddSingleton<IAnalysisTool, FindComparableCyclesTool>();
        services.AddSingleton<IAnalysisTool, CompareCyclesTool>();
        services.AddSingleton<IAnalysisTool, CompareProcessWindowsTool>();
        services.AddSingleton<IAnalysisTool, SearchProcessKnowledgeTool>();

        // 人工检测结果记录（PostgreSQL）；与生产事件分表、分 API 建模
        services.Configure<InspectionAttachmentOptions>(configuration.GetSection("InspectionAttachments"));
        services.AddSingleton<IInspectionRecordStore, PostgresInspectionRecordStore>();
        services.AddSingleton<IInspectionAttachmentStore, PostgresInspectionAttachmentStore>();
        services.AddSingleton<IInspectionMasterDataStore, PostgresInspectionMasterDataStore>();
        services.AddSingleton<IInspectionReviewStore, PostgresInspectionReviewStore>();
        services.AddSingleton<IInspectionWorkflowService, InspectionWorkflowService>();
        services.AddHostedService<InspectionStoreInitializerHostedService>();
        services.AddSingleton<ICycleComparisonService, CycleComparisonService>();
        services.AddSingleton<IProcessWindowComparisonService, ProcessWindowComparisonService>();
        services.AddSingleton<ICycleRecordService, CycleRecordService>();
        services.AddHostedService<CycleAnalysisRecomputeHostedService>();
        services.AddSingleton<CycleAnalysisBackfillService>();
        services.AddHostedService(provider => provider.GetRequiredService<CycleAnalysisBackfillService>());
        services.AddSingleton<IQualityAnalysisService, QualityAnalysisService>();

        // 工艺数据模型、配方版本与分析方案使用独立的版本化配置存储。
        services.AddSingleton<IProcessConfigurationStore, PostgresProcessConfigurationStore>();
        services.AddSingleton<ProcessAnalysisResolver>();
        services.AddHostedService<ProcessConfigurationInitializerHostedService>();

        // 模型、工艺调查、受控试验、现场知识和参数建议共享同一条可审计改进链。
        services.Configure<ProcessKnowledgeOptions>(configuration.GetSection("ProcessKnowledge"));
        services.AddSingleton<IProcessImprovementStore, PostgresProcessImprovementStore>();
        services.AddSingleton<ITrialEvidenceSource, PostgresTrialEvidenceSource>();
        services.AddSingleton<ScientificTrialResultCalculator>();
        services.AddSingleton<ProcessImprovementWorkflow>();
        services.AddSingleton<MechanismModelService>();
        services.AddSingleton<IKnowledgeContentExtractor, PdfKnowledgeExtractor>();
        services.AddSingleton<IKnowledgeContentExtractor, ExcelKnowledgeExtractor>();
        services.AddSingleton<IKnowledgeContentExtractor, PlainTextKnowledgeExtractor>();
        services.AddHttpClient("knowledge-image-ocr");
        services.AddSingleton<IKnowledgeContentExtractor>(provider =>
            new ImageKnowledgeExtractor(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("knowledge-image-ocr"),
                configuration));
        services.AddSingleton<KnowledgeExtractionService>();
        services.AddSingleton<ScientificValidationRunner>();
        services.AddHostedService<ProcessImprovementInitializerHostedService>();

        // 采集配置由平台统一管理并按边缘节点发布；采集执行器只运行已发布版本。
        services.AddSingleton<IAcquisitionProfileStore, PostgresAcquisitionProfileStore>();
        services.AddHostedService<AcquisitionProfileInitializerHostedService>();

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
