using Ingot.Platform.Application.Inspections;
using Ingot.Platform.Inspections.Infrastructure;

namespace Ingot.Platform.Infrastructure.Inspections;

public static class InspectionModuleServiceCollectionExtensions
{
    public static IServiceCollection AddIngotInspections(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<InspectionAttachmentOptions>(configuration.GetSection("InspectionAttachments"));
        services.AddSingleton<IInspectionRecordStore, PostgresInspectionRecordStore>();
        services.AddSingleton<IInspectionAttachmentStore, PostgresInspectionAttachmentStore>();
        services.AddSingleton<IInspectionMasterDataStore, PostgresInspectionMasterDataStore>();
        services.AddSingleton<IInspectionReviewStore, PostgresInspectionReviewStore>();
        services.AddSingleton<IInspectionProductionEventReader, InspectionProductionEventReader>();
        services.AddSingleton<IInspectionWorkflowService, InspectionWorkflowService>();
        services.AddSingleton<InspectionCommands>();
        services.AddHostedService<InspectionStoreInitializerHostedService>();
        return services;
    }
}
