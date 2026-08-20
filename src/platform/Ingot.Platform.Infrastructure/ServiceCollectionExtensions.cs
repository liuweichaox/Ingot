using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.Acquisition;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.Insight;
using Ingot.Platform.Application.Manufacturing;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Agent;
using Ingot.Platform.Application.Inspections;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Microsoft.Extensions.Logging;
using Ingot.Platform.Infrastructure.Events;
using Ingot.Platform.Infrastructure.AgentTools;
using Ingot.Platform.Infrastructure.AgentRuns;
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
        services.AddSingleton<PostgresAgentRunStore>();
        services.AddSingleton<IAgentRunStore>(provider =>
            provider.GetRequiredService<PostgresAgentRunStore>());
        services.AddSingleton<IAgentRunSnapshotReader, AgentRunSnapshotReader>();
        // 边缘注册与心跳是多 API 副本共享的 PostgreSQL 运维事实。
        services.AddSingleton<EdgeRegistry>();

        // 事件生产记录库（PostgreSQL）
        // 生产上下文必须先于事件库就绪；process.execution.started 会解析并固化当时有效的工装与工艺规范引用。
        services.AddSingleton<IManufacturingContextStore, PostgresManufacturingContextStore>();
        services.AddSingleton<ManufacturingContextApplication>();
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
        services.AddSingleton<ProcessCurveQueryService>();
        services.AddHostedService<TimeSeriesStoreInitializerHostedService>();
        services.AddSingleton<IPlatformEventStore, PostgresPlatformEventStore>();
        services.AddSingleton<PlatformEventApplication>();
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

        services.AddSingleton<InspectionExecutionReferenceValidator>();
        // 跨上下文的生产事件读取适配器属于 Platform 集成基础设施；检验模块自己的
        // PostgreSQL 适配器由宿主通过 AddIngotInspectionInfrastructure 独立组合。
        services.AddSingleton<IInspectionProductionEventReader, InspectionProductionEventReader>();
        services.AddSingleton<ExecutionComparisonMetrics>();
        services.AddSingleton<IExecutionComparisonService, ExecutionComparisonService>();
        services.AddSingleton<ITimeWindowComparisonService, TimeWindowComparisonService>();
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<IProcessExecutionAnalysisOperationsStore>(provider =>
            provider.GetRequiredService<IProcessExecutionAnalysisMaterializationStore>());
        services.AddSingleton<ProcessExecutionAnalysisOperationsService>();
        services.AddSingleton<ProcessExecutionAnalysisBackfillService>();
        services.AddSingleton<IQualityAnalysisService, QualityAnalysisService>();
        services.AddSingleton<ResearchContextAdmissionEvaluator>();
        services.AddSingleton<IDataReliabilityBaselineService, DataReliabilityBaselineService>();

        services.AddSingleton<IGoldenQuestionStore, Ingot.Platform.Infrastructure.Insight.PostgresGoldenQuestionStore>();
        services.AddSingleton<GoldenQuestionEvaluator>();
        services.AddSingleton<GoldenQuestionApplication>();

        // 工艺数据模型、工艺规范版本与分析方案使用独立的版本化配置存储。
        services.AddSingleton<IProcessConfigurationStore, PostgresProcessConfigurationStore>();
        services.AddSingleton<ProcessAnalysisResolver>();
        services.AddSingleton<ProcessConfigurationApplication>();
        services.AddSingleton<ScenarioPackageService>();

        // 研发资产保存版本化数据集、模型、机理模型和项目知识来源。
        services.Configure<ProcessKnowledgeOptions>(configuration.GetSection("ProcessKnowledge"));
        services.AddSingleton<IResearchAssetStore, PostgresResearchAssetStore>();
        services.AddSingleton<ResearchAssetApplication>();
        services.AddSingleton<ResearchAssetWorkflow>();
        services.AddSingleton<MechanismModelService>();
        services.AddSingleton<IMechanismKnowledgeStore, PostgresMechanismKnowledgeStore>();
        services.AddSingleton<MechanismKnowledgeService>();
        services.AddSingleton<MechanismKnowledgeQueries>();
        services.AddSingleton<MechanismClaimDraftService>();
        services.AddHttpClient("mechanism-draft-generation");
        services.AddSingleton<IMechanismClaimDraftGenerator, OpenAiCompatibleMechanismClaimDraftGenerator>();
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
        services.AddSingleton<IDatasetQualityValidationService>(provider =>
            provider.GetRequiredService<DatasetQualityValidationRunner>());
        services.AddHostedService<ResearchAssetInitializerHostedService>();

        services.AddIngotProcessResearch(configuration);

        // 采集配置由平台统一管理并按边缘节点发布；采集执行器只运行已发布版本。
        services.AddSingleton<IIngestionTaskStore, PostgresIngestionTaskStore>();
        services.AddSingleton<IIngestionConfigurationStore, PostgresIngestionConfigurationStore>();
        services.AddSingleton<AcquisitionApplication>();
        services.AddSingleton<IAcquisitionProbeTaskStore, PostgresAcquisitionProbeTaskStore>();
        services.AddSingleton<AcquisitionProbeTaskCoordinator>();
        services.AddSingleton<IngestionConfigurationWorkflow>();

        // 运行边界识别与存储
        services.AddSingleton<PostgresExecutionBoundaryStore>();
        services.AddSingleton<IExecutionBoundaryStore>(
            provider => provider.GetRequiredService<PostgresExecutionBoundaryStore>());
        services.AddSingleton<ExecutionBoundaryQueries>();
        services.AddSingleton<ExecutionBoundaryRecognizer>();
        services.AddSingleton<IExecutionBoundaryRecognizer>(
            provider => provider.GetRequiredService<ExecutionBoundaryRecognizer>());
        services.Configure<ExecutionBoundaryProjectionOptions>(
            configuration.GetSection("ExecutionBoundaryProjection"));
        services.Configure<ProcessExecutionAnalysisRecomputeOptions>(
            configuration.GetSection("ProcessExecutionAnalysisRecompute"));

        return services;
    }

    /// <summary>
    ///     注册所有会持续修改业务状态的后台处理器。API 宿主不得调用本方法；
    ///     独立 Worker 可以横向扩容，但每个任务必须通过数据库租约原子领取。
    /// </summary>
    public static IServiceCollection AddIngotPlatformWorkers(this IServiceCollection services)
    {
        services.AddOptions<ExecutionBoundaryProjectionOptions>()
            .Validate(
                static value => value.PollInterval > TimeSpan.Zero &&
                                value.LeaseTimeout > value.PollInterval &&
                                value.ExecutionTimeoutWithoutCompletion > TimeSpan.Zero &&
                                value.MaximumRetryDelay > TimeSpan.Zero &&
                                value.MaxAttempts > 0,
                "运行边界投影 Worker 的轮询、租约、运行超时或退避配置无效。")
            .ValidateOnStart();
        services.AddOptions<ProcessExecutionAnalysisRecomputeOptions>()
            .Validate(static value => value.MaxAttempts > 0, "过程执行分析重算最大尝试次数必须大于 0。")
            .ValidateOnStart();
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
        services.AddHostedService<ExecutionBoundaryProjectionHostedService>();
        services.AddHostedService(provider => provider.GetRequiredService<ProcessExecutionAnalysisBackfillService>());
        services.AddHostedService<KnowledgeExtractionWorker>();
        services.AddHostedService<ResearchExperimentAutomationHostedService>();
        return services;
    }
}
