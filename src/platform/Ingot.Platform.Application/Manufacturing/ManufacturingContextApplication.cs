using Ingot.Contracts.Manufacturing;

namespace Ingot.Platform.Application.Manufacturing;

public sealed class ManufacturingContextApplication(IManufacturingContextStore contexts)
{
    public Task<ToolingComponentTypeDefinition> UpsertComponentTypeAsync(
        ToolingComponentTypeDefinition value, CancellationToken ct = default)
        => contexts.UpsertComponentTypeAsync(value, ct);
    public Task<IReadOnlyList<ToolingComponentTypeDefinition>> ListComponentTypesAsync(CancellationToken ct = default)
        => contexts.ListComponentTypesAsync(ct);
    public Task<bool> DeleteComponentTypeAsync(string code, CancellationToken ct = default)
        => contexts.DeleteComponentTypeAsync(code, ct);

    public Task<ToolingTypeDefinition> CreateToolingTypeAsync(ToolingTypeDefinition value, CancellationToken ct = default)
        => contexts.CreateToolingTypeAsync(value, ct);
    public Task<IReadOnlyList<ToolingTypeDefinition>> ListToolingTypesAsync(CancellationToken ct = default)
        => contexts.ListToolingTypesAsync(ct);
    public Task<bool> DeleteToolingTypeAsync(string code, int version, CancellationToken ct = default)
        => contexts.DeleteToolingTypeAsync(code, version, ct);

    public Task<ToolingComponent> UpsertComponentAsync(ToolingComponent value, CancellationToken ct = default)
        => contexts.UpsertComponentAsync(value, ct);
    public Task<IReadOnlyList<ToolingComponent>> ListComponentsAsync(
        string? componentTypeCode = null, CancellationToken ct = default)
        => contexts.ListComponentsAsync(componentTypeCode, ct);
    public Task<bool> DeleteComponentAsync(string id, CancellationToken ct = default)
        => contexts.DeleteComponentAsync(id, ct);

    public Task<ToolingAssembly> UpsertAssemblyAsync(ToolingAssembly value, CancellationToken ct = default)
        => contexts.UpsertAssemblyAsync(value, ct);
    public Task<IReadOnlyList<ToolingAssembly>> ListAssembliesAsync(CancellationToken ct = default)
        => contexts.ListAssembliesAsync(ct);
    public Task<bool> DeleteAssemblyAsync(string id, CancellationToken ct = default)
        => contexts.DeleteAssemblyAsync(id, ct);

    public Task<ToolingAssemblyRevision> CreateAssemblyRevisionAsync(
        ToolingAssemblyRevision value, CancellationToken ct = default)
        => contexts.CreateAssemblyRevisionAsync(value, ct);
    public Task<IReadOnlyList<ToolingAssemblyRevision>> ListAssemblyRevisionsAsync(
        string? assemblyId = null, CancellationToken ct = default)
        => contexts.ListAssemblyRevisionsAsync(assemblyId, ct);
    public Task<bool> DeleteAssemblyRevisionAsync(Guid id, CancellationToken ct = default)
        => contexts.DeleteAssemblyRevisionAsync(id, ct);

    public Task<ToolingInstallation> CreateInstallationAsync(
        ToolingInstallation value, CancellationToken ct = default)
        => contexts.CreateInstallationAsync(value, ct);
    public Task<ToolingInstallation> ReplaceInstallationAsync(
        ToolingInstallation value, CancellationToken ct = default)
        => contexts.ReplaceInstallationAsync(value, ct);
    public Task<ToolingInstallation?> RemoveInstallationAsync(
        string siteId, Guid id, DateTimeOffset removedAt, string? actor, CancellationToken ct = default)
        => contexts.RemoveInstallationAsync(siteId, id, removedAt, actor, ct);
    public Task<IReadOnlyList<ToolingInstallation>> ListInstallationsAsync(
        string? siteId = null, string? equipmentId = null, bool activeOnly = false, CancellationToken ct = default)
        => contexts.ListInstallationsAsync(siteId, equipmentId, activeOnly, ct);
    public Task<bool> DeleteInstallationAsync(string siteId, Guid id, CancellationToken ct = default)
        => contexts.DeleteInstallationAsync(siteId, id, ct);

    public Task<ProductionContext> StartProductionContextAsync(
        ProductionContext value, CancellationToken ct = default)
        => contexts.StartProductionContextAsync(value, ct);
    public Task<ProductionContext> ReplaceProductionContextAsync(
        ProductionContext value, CancellationToken ct = default)
        => contexts.ReplaceProductionContextAsync(value, ct);
    public Task<ProductionContext?> CloseProductionContextAsync(
        string siteId, Guid id, DateTimeOffset validTo, string? actor, CancellationToken ct = default)
        => contexts.CloseProductionContextAsync(siteId, id, validTo, actor, ct);
    public Task<IReadOnlyList<ProductionContext>> ListProductionContextsAsync(
        string? siteId = null, string? equipmentId = null, bool activeOnly = false, CancellationToken ct = default)
        => contexts.ListProductionContextsAsync(siteId, equipmentId, activeOnly, ct);
    public Task<bool> DeleteProductionContextAsync(string siteId, Guid id, CancellationToken ct = default)
        => contexts.DeleteProductionContextAsync(siteId, id, ct);
    public Task<ResolvedProductionContext?> ResolveAsync(
        string siteId, string equipmentId, DateTimeOffset at, CancellationToken ct = default)
        => contexts.ResolveAsync(siteId, equipmentId, at, ct);
}
