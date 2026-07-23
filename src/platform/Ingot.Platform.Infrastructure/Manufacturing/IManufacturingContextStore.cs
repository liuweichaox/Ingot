using Ingot.Contracts.Manufacturing;

namespace Ingot.Platform.Infrastructure.Manufacturing;

public interface IManufacturingContextStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<ToolingComponentTypeDefinition> UpsertComponentTypeAsync(ToolingComponentTypeDefinition value, CancellationToken ct = default);
    Task<IReadOnlyList<ToolingComponentTypeDefinition>> ListComponentTypesAsync(CancellationToken ct = default);
    Task<bool> DeleteComponentTypeAsync(string componentTypeCode, CancellationToken ct = default);

    Task<ToolingTypeDefinition> CreateToolingTypeAsync(ToolingTypeDefinition value, CancellationToken ct = default);
    Task<IReadOnlyList<ToolingTypeDefinition>> ListToolingTypesAsync(CancellationToken ct = default);
    Task<bool> DeleteToolingTypeAsync(string toolingTypeCode, int version, CancellationToken ct = default);

    Task<ToolingComponent> UpsertComponentAsync(ToolingComponent value, CancellationToken ct = default);
    Task<IReadOnlyList<ToolingComponent>> ListComponentsAsync(string? componentTypeCode = null, CancellationToken ct = default);
    Task<bool> DeleteComponentAsync(string componentId, CancellationToken ct = default);

    Task<ToolingAssembly> UpsertAssemblyAsync(ToolingAssembly value, CancellationToken ct = default);
    Task<IReadOnlyList<ToolingAssembly>> ListAssembliesAsync(CancellationToken ct = default);
    Task<bool> DeleteAssemblyAsync(string moldId, CancellationToken ct = default);

    Task<ToolingAssemblyRevision> CreateAssemblyRevisionAsync(ToolingAssemblyRevision value, CancellationToken ct = default);
    Task<IReadOnlyList<ToolingAssemblyRevision>> ListAssemblyRevisionsAsync(string? moldId = null, CancellationToken ct = default);
    Task<bool> DeleteAssemblyRevisionAsync(Guid assemblyRevisionId, CancellationToken ct = default);

    Task<ToolingInstallation> CreateInstallationAsync(ToolingInstallation value, CancellationToken ct = default);
    Task<ToolingInstallation?> RemoveInstallationAsync(Guid installationId, DateTimeOffset removedAt, string? actor, CancellationToken ct = default);
    Task<IReadOnlyList<ToolingInstallation>> ListInstallationsAsync(string? machineId = null, bool activeOnly = false, CancellationToken ct = default);
    Task<bool> DeleteInstallationAsync(Guid installationId, CancellationToken ct = default);

    Task<ProductionContext> StartProductionContextAsync(ProductionContext value, CancellationToken ct = default);
    Task<ProductionContext?> CloseProductionContextAsync(Guid contextId, DateTimeOffset validTo, string? actor, CancellationToken ct = default);
    Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(string? machineId = null, bool activeOnly = false, CancellationToken ct = default);
    Task<bool> DeleteProductionContextAsync(Guid contextId, CancellationToken ct = default);
    Task<ResolvedProductionContext?> ResolveAsync(string machineId, DateTimeOffset at, CancellationToken ct = default);
}
