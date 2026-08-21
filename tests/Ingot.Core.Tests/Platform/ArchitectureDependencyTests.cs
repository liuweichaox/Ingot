// 验证平台组件 ArchitectureDependency 的成功、拒绝和安全边界。

using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Microsoft.AspNetCore.Mvc;
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
    public void Infrastructure_ShouldOwnInspectionAdaptersAndDependOnApplication()
    {
        var platform = ReferencedAssemblies(typeof(PostgresExecutionBoundaryStore).Assembly);

        Assert.Contains("Ingot.Platform.Application", platform);
        Assert.Same(
            typeof(PostgresExecutionBoundaryStore).Assembly,
            typeof(PostgresInspectionRecordStore).Assembly);
    }

    [Fact]
    public void ApiHost_ShouldComposeTheUnifiedInfrastructureAssembly()
    {
        var api = ReferencedAssemblies(typeof(Ingot.Platform.Api.Controllers.ProcessCurvesController).Assembly);

        Assert.Contains("Ingot.Platform.Infrastructure", api);
        Assert.DoesNotContain("Ingot.Platform.Inspections.Infrastructure", api);
    }

    [Fact]
    public void ApiControllers_ShouldDependOnApplicationUseCasesRatherThanStores()
    {
        var violations = typeof(Ingot.Platform.Api.Controllers.ProcessCurvesController).Assembly
            .GetTypes()
            .Where(static type => !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(static type => type.GetConstructors()
                .SelectMany(constructor => constructor.GetParameters()
                    .Where(static parameter => parameter.ParameterType.Name.EndsWith("Store", StringComparison.Ordinal))
                    .Select(parameter => $"{type.FullName}: {parameter.ParameterType.FullName}")))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(violations);
    }

    private static string[] ReferencedAssemblies(System.Reflection.Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(static reference => reference.Name ?? string.Empty)
            .Order(StringComparer.Ordinal)
            .ToArray();
}
