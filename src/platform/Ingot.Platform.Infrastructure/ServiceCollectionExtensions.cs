using Ingot.Agent;
using Ingot.Platform.Infrastructure.ProcessExecutions;
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
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

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
        // WebApplicationBuilder 通常已经注册 IConfiguration；独立宿主、集成测试和工具未必如此。
        // TryAdd 保留宿主已有实例，同时保证下游存储可直接注入传入的同一份配置。
        services.TryAddSingleton(configuration);
        // 每个宿主进程只建立一个连接池。所有 PostgreSQL Store 共享该 DataSource，
        // 避免每个 Store 独立创建默认 100 连接的池并耗尽数据库连接。
        services.TryAddSingleton<NpgsqlDataSource>(provider =>
            PostgresDataSourceFactory.Create(provider.GetRequiredService<IConfiguration>()));
        // 边缘注册与心跳是多 API 副本共享的 PostgreSQL 运维事实。
        services.AddSingleton<EdgeRegistry>();

        // 事件生产记录库（PostgreSQL）
        // 生产上下文必须先于事件库就绪；process.execution.started 会解析并固化当时有效的工装与工艺规范引用。
        services.AddSingleton<IManufacturingContextStore, PostgresManufacturingContextStore>();
        services.AddSingleton<IProcessExecutionAnalysisMaterializationStore, PostgresProcessExecutionAnalysisMaterializationStore>();
        services.AddSingleton<IFeatureDefinitionRegistry, BuiltInFeatureDefinitionRegistry>();
        services.AddSingleton<ProcessExecutionAnalysisEngine>();
        services.AddSingleton<PostgresProcessExecutionScientificComputeEngine>();
        services.AddSingleton<IExecutionAnalysisLockProvider, PostgresExecutionAnalysisLockProvider>();
        services.AddSingleton<ProcessExecutionAnalysisMaterializer>();
        services.Configure<PlatformEventOptions>(configuration.GetSection("EventIngest"));
        services.AddSingleton<PlatformEventMetrics>();
        services.AddSingleton<PostgresTimeSeriesStore>();
        services.AddSingleton<ITimeSeriesStore>(
            provider => provider.GetRequiredService<PostgresTimeSeriesStore>());
        services.AddHostedService<TimeSeriesStoreInitializerHostedService>();
        services.AddSingleton<IPlatformEventStore, PostgresPlatformEventStore>();
        services.AddHostedService<EventStoreInitializerHostedService>();

        // Chat 只能通过显式注册的只读工具访问中心数据。
        services.Configure<ChatDataAccessOptions>(configuration.GetSection("ChatDataAccess"));
        services.AddSingleton<ChatEventReader>();
        services.AddSingleton<IChatEventReader>(
            provider => provider.GetRequiredService<ChatEventReader>());
        services.AddSingleton<IChatDataObjectReader>(
            provider => provider.GetRequiredService<ChatEventReader>());
        services.AddSingleton<IAnalysisTool, ListDataObjectsTool>();
        services.AddSingleton<IAnalysisTool, CheckDataQualityTool>();
        services.AddSingleton<IAnalysisTool, GetProcessExecutionTraceTool>();
        services.AddSingleton<IAnalysisTool, FindComparableExecutionsTool>();
        services.AddSingleton<IAnalysisTool, CompareExecutionsTool>();
        services.AddSingleton<IAnalysisTool, CompareTimeWindowsTool>();
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
        services.AddSingleton<IExecutionComparisonService, ExecutionComparisonService>();
        services.AddSingleton<ITimeWindowComparisonService, TimeWindowComparisonService>();
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<ProcessExecutionAnalysisBackfillService>();
        services.AddSingleton<IQualityAnalysisService, QualityAnalysisService>();
        services.AddSingleton<ResearchContextAdmissionEvaluator>();
        services.AddSingleton<IDataReliabilityBaselineService, DataReliabilityBaselineService>();

        services.AddSingleton<Insight.IGoldenQuestionStore, Insight.PostgresGoldenQuestionStore>();
        services.AddSingleton<Insight.GoldenQuestionEvaluator>();

        // 工艺数据模型、工艺规范版本与分析方案使用独立的版本化配置存储。
        services.AddSingleton<IProcessConfigurationStore, PostgresProcessConfigurationStore>();
        services.AddSingleton<ProcessAnalysisResolver>();

        // 研发资产保存版本化数据集、模型、机理模型和项目知识来源。
        services.Configure<ProcessKnowledgeOptions>(configuration.GetSection("ProcessKnowledge"));
        services.AddSingleton<IResearchAssetStore, PostgresResearchAssetStore>();
        services.AddSingleton<ResearchAssetWorkflow>();
        services.AddSingleton<MechanismModelService>();
        services.AddSingleton<IMechanismKnowledgeStore, PostgresMechanismKnowledgeStore>();
        services.AddSingleton<MechanismKnowledgeService>();
        services.AddSingleton<IKnowledgeContentExtractor, PdfKnowledgeExtractor>();
        services.AddSingleton<IKnowledgeContentExtractor, ExcelKnowledgeExtractor>();
        services.AddSingleton<IKnowledgeContentExtractor, PlainTextKnowledgeExtractor>();
        services.AddHttpClient("knowledge-image-ocr");
        services.AddSingleton<IKnowledgeContentExtractor>(provider =>
            new ImageKnowledgeExtractor(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("knowledge-image-ocr"),
                configuration));
        services.AddSingleton<KnowledgeExtractionService>();
        services.Configure<KnowledgeExtractionWorkerOptions>(
            configuration.GetSection("KnowledgeExtractionWorker"));
        services.AddSingleton<DatasetQualityValidationRunner>();
        services.AddHostedService<ResearchAssetInitializerHostedService>();

        // 工艺研发项目是实验、假设、工艺窗口与知识沉淀的产品主对象。
        services.AddSingleton<IProcessResearchStore, PostgresProcessResearchStore>();
        services.AddSingleton<ResearchExperimentValidationService>();
        services.AddSingleton<ProcessResearchWorkflow>();
        services.AddSingleton<IResearchObservationAssembler, ResearchObservationAssembler>();
        services.AddSingleton<ResearchOperatingRegionMaterializer>();
        services.AddSingleton<ResearchExperimentResultMaterializer>();
        services.Configure<ProcessOptimizerOptions>(configuration.GetSection("ProcessOptimizer"));
        services.AddTransient<ProcessOptimizerCircuitBreakerHandler>();
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
        }).AddHttpMessageHandler<ProcessOptimizerCircuitBreakerHandler>();
        services.AddSingleton<ResearchExperimentDesignService>();
        services.AddSingleton<ResearchExperimentOptimizer>();
        services.AddSingleton<ResearchShadowRecommendationService>();
        services.AddSingleton<ResearchHistoricalReplayService>();
        services.AddSingleton<ResearchOnlineAdmissionService>();
        services.AddSingleton<ResearchOnlineCampaignService>();
        services.AddSingleton<ResearchRollbackDrillService>();
        services.AddSingleton<ResearchTransferAssessmentService>();

        // 采集配置由平台统一管理并按边缘节点发布；采集执行器只运行已发布版本。
        services.AddSingleton<IIngestionTaskStore, PostgresIngestionTaskStore>();
        services.AddSingleton<IIngestionConfigurationStore, PostgresIngestionConfigurationStore>();
        services.AddSingleton<AcquisitionProbeTaskCoordinator>();

        return services;
    }

    /// <summary>
    ///     注册所有会持续修改业务状态的后台处理器。API 宿主不得调用本方法；
    ///     独立 Worker 可以横向扩容，但每个任务必须通过数据库租约原子领取。
    /// </summary>
    public static IServiceCollection AddIngotPlatformWorkers(this IServiceCollection services)
    {
        services.AddOptions<KnowledgeExtractionWorkerOptions>()
            .Validate(
                static value => value.HeartbeatInterval > TimeSpan.Zero &&
                                value.LeaseTimeout > value.HeartbeatInterval * 2 &&
                                value.MaxAttempts > 0 &&
                                value.InitialRetryDelay >= TimeSpan.Zero &&
                                value.MaxRetryDelay >= value.InitialRetryDelay,
                "知识提取 Worker 的租约、心跳、重试次数或退避配置无效。")
            .ValidateOnStart();
        services.AddHostedService<TimeSeriesRetentionHostedService>();
        services.AddHostedService<EventIngestKeyPruneHostedService>();
        services.AddHostedService<ProcessExecutionAnalysisRecomputeHostedService>();
        services.AddHostedService(provider => provider.GetRequiredService<ProcessExecutionAnalysisBackfillService>());
        services.AddHostedService<KnowledgeExtractionWorker>();
        services.AddHostedService<ResearchExperimentAutomationHostedService>();
        return services;
    }
}
