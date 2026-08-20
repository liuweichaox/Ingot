using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Inspections.Infrastructure;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ArchitectureDependencyTests
{
    [Fact]
    public void ApplicationAssembly_ShouldNotReferenceInfrastructureOrDatabaseProviders()
    {
        var references = ReferencedAssemblies(typeof(ProcessExecutionAnalysisOperationsService).Assembly);

        Assert.DoesNotContain(references, static name =>
            name.Contains("Infrastructure", StringComparison.Ordinal) ||
            name.StartsWith("Npgsql", StringComparison.Ordinal) ||
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
    }

    [Fact]
    public void InfrastructureModules_ShouldDependOnApplicationButNotEachOther()
    {
        var platform = ReferencedAssemblies(typeof(PostgresExecutionBoundaryStore).Assembly);
        var inspections = ReferencedAssemblies(typeof(PostgresInspectionRecordStore).Assembly);

        Assert.Contains("Ingot.Platform.Application", platform);
        Assert.Contains("Ingot.Platform.Application", inspections);
        Assert.DoesNotContain("Ingot.Platform.Inspections.Infrastructure", platform);
        Assert.DoesNotContain("Ingot.Platform.Infrastructure", inspections);
    }

    [Fact]
    public void ApiHost_ShouldBeTheCompositionRootForInfrastructureModules()
    {
        var api = ReferencedAssemblies(typeof(Ingot.Platform.Api.Controllers.ProcessCurvesController).Assembly);

        Assert.Contains("Ingot.Platform.Infrastructure", api);
        Assert.Contains("Ingot.Platform.Inspections.Infrastructure", api);
    }

    private static string[] ReferencedAssemblies(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
