using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ingot.Agent;

namespace Ingot.Connector.Builder;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIngotConnectorBuilder(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<ConnectorBuilderOptions>(configuration.GetSection("ConnectorBuilder"));
        services.AddSingleton<IConnectorCommandRunner, ConnectorCommandRunner>();
        services.AddSingleton<IConnectorWorkspaceService, FileConnectorWorkspaceService>();
        services.AddSingleton<IAnalysisTool, CreateConnectorWorkspaceTool>();
        services.AddSingleton<IAnalysisTool, ListConnectorWorkspaceFilesTool>();
        services.AddSingleton<IAnalysisTool, ReadConnectorWorkspaceFileTool>();
        services.AddSingleton<IAnalysisTool, WriteConnectorWorkspaceFileTool>();
        services.AddSingleton<IAnalysisTool, BuildConnectorWorkspaceTool>();
        services.AddSingleton<IAnalysisTool, TestConnectorWorkspaceTool>();
        services.AddSingleton<IAnalysisTool, PackageConnectorWorkspaceTool>();
        return services;
    }
}
