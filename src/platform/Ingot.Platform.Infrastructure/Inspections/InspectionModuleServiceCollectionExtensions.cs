
using Ingot.Platform.Application.Inspections;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ingot.Platform.Infrastructure.Inspections;

public static class InspectionModuleServiceCollectionExtensions
{
    public static IServiceCollection AddIngotInspectionInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<InspectionAttachmentOptions>(configuration.GetSection("InspectionAttachments"));
        services.AddSingleton<IInspectionRecordStore, PostgresInspectionRecordStore>();
        services.AddSingleton<IInspectionAttachmentStore, PostgresInspectionAttachmentStore>();
        services.AddSingleton<IInspectionMasterDataStore, PostgresInspectionMasterDataStore>();
        services.AddSingleton<IInspectionReviewStore, PostgresInspectionReviewStore>();
        services.AddSingleton<IInspectionWorkflowService, InspectionWorkflowService>();
        services.AddSingleton<InspectionCommands>();
        services.AddSingleton<InspectionQueries>();
        services.AddHostedService<InspectionStoreInitializerHostedService>();
        return services;
    }
}
