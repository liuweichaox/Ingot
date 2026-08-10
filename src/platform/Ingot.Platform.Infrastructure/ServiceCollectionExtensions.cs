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
using Ingot.Platform.Infrastructure.ResearchAssets;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.Services;
using Ingot.Platform.Infrastructure.TimeSeries;
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
        // 版本化数据库迁移是 PostgreSQL user schema 的唯一真相源，且必须先于
        // Timescale 拓扑、文件存储目录和业务后台服务初始化。
        services.AddSingleton<MigrationRunner>();
        services.AddHostedService<MigrationHostedService>();

        // 边缘注册表（SQLite）
        services.AddSingleton<EdgeRegistry>();

        // 事件生产记录库（PostgreSQL）
        // 生产上下文必须先于事件库就绪；cycle.started 会解析并固化当时有效的工装与配方引用。
        services.AddSingleton<IManufacturingContextStore, PostgresManufacturingContextStore>();
        services.AddSingleton<ICycleAnalysisMaterializationStore, PostgresCycleAnalysisMaterializationStore>();
        services.AddSingleton<CycleAnalysisRecomputeQueue>();
        services.AddSingleton<IFeatureDefinitionRegistry, BuiltInFeatureDefinitionRegistry>();
        services.AddSingleton<WholeCycleAnalysisEngine>();
        services.AddSingleton<PostgresCycleScientificComputeEngine>();
        services.AddSingleton<CycleAnalysisMaterializer>();
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
        services.AddSingleton<ChatEventReader>();
        services.AddSingleton<IChatEventReader>(
            provider => provider.GetRequiredService<ChatEventReader>());
        services.AddSingleton<IChatDataObjectReader>(
            provider => provider.GetRequiredService<ChatEventReader>());
        services.AddSingleton<IAnalysisTool, ListDataObjectsTool>();
        services.AddSingleton<IAnalysisTool, CheckDataQualityTool>();
        services.AddSingleton<IAnalysisTool, GetCycleTraceTool>();
        services.AddSingleton<IAnalysisTool, FindComparableCyclesTool>();
        services.AddSingleton<IAnalysisTool, CompareCyclesTool>();
        services.AddSingleton<IAnalysisTool, CompareProcessWindowsTool>();
        services.AddSingleton<IAnalysisTool, SearchProcessKnowledgeTool>();
        services.AddSingleton<IAnalysisTool, GetResearchProjectTool>();

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
        services.AddSingleton<ResearchContextAdmissionEvaluator>();
        services.AddSingleton<IDataReliabilityBaselineService, DataReliabilityBaselineService>();

        services.AddSingleton<Insight.IGoldenQuestionStore, Insight.PostgresGoldenQuestionStore>();
        services.AddSingleton<Insight.GoldenQuestionEvaluator>();

        // 工艺数据模型、配方版本与分析方案使用独立的版本化配置存储。
        services.AddSingleton<IProcessConfigurationStore, PostgresProcessConfigurationStore>();
        services.AddSingleton<ProcessAnalysisResolver>();

        // 研发资产保存版本化数据集、模型、机理模型和项目知识来源。
        services.Configure<ProcessKnowledgeOptions>(configuration.GetSection("ProcessKnowledge"));
        services.AddSingleton<IResearchAssetStore, PostgresResearchAssetStore>();
        services.AddSingleton<ResearchAssetWorkflow>();
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
        services.AddSingleton<DatasetQualityValidationRunner>();
        services.AddHostedService<ResearchAssetInitializerHostedService>();

        // 工艺研发项目是实验、假设、工艺窗口与知识沉淀的产品主对象。
        services.AddSingleton<IProcessResearchStore, PostgresProcessResearchStore>();
        services.AddSingleton<ProcessResearchWorkflow>();
        services.AddSingleton<IResearchObservationAssembler, ResearchObservationAssembler>();
        services.AddSingleton<ResearchProcessWindowMaterializer>();
        services.AddSingleton<ResearchExperimentResultMaterializer>();
        services.Configure<ProcessOptimizerOptions>(configuration.GetSection("ProcessOptimizer"));
        services.AddHttpClient<IProcessOptimizerClient, ProcessOptimizerClient>((provider, client) =>
        {
            var optimizerOptions = provider.GetRequiredService<IOptions<ProcessOptimizerOptions>>().Value;
            if (!Uri.TryCreate(optimizerOptions.BaseUrl, UriKind.Absolute, out var baseAddress))
                throw new InvalidOperationException("ProcessOptimizer:BaseUrl 必须是绝对 URL。");
            client.BaseAddress = new Uri(
                baseAddress.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
                    ? baseAddress.AbsoluteUri
                    : $"{baseAddress.AbsoluteUri}/");
            client.Timeout = TimeSpan.FromSeconds(
                Math.Clamp(optimizerOptions.RequestTimeoutSeconds, 1, 300));
        });
        services.AddSingleton<ResearchExperimentOptimizer>();
        services.AddSingleton<ResearchShadowRecommendationService>();
        services.AddSingleton<ResearchHistoricalReplayService>();
        services.AddSingleton<ResearchOnlineAdmissionService>();
        services.AddSingleton<ResearchOnlineCampaignService>();
        services.AddSingleton<ResearchRollbackDrillService>();
        services.AddSingleton<ResearchTransferAssessmentService>();
        services.AddHostedService<ResearchExperimentAutomationHostedService>();

        // 采集配置由平台统一管理并按边缘节点发布；采集执行器只运行已发布版本。
        services.AddSingleton<IAcquisitionProfileStore, PostgresAcquisitionProfileStore>();
        services.AddSingleton<AcquisitionProbeTaskCoordinator>();

        return services;
    }
}
