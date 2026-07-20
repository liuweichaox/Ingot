using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

public interface IInspectionMasterDataStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<InspectionDefinition> UpsertInspectionDefinitionAsync(InspectionDefinition definition, CancellationToken ct = default);
    Task<IReadOnlyList<InspectionDefinition>> ListInspectionDefinitionsAsync(CancellationToken ct = default);
    Task<InspectionDefinition?> GetInspectionDefinitionAsync(string code, int version, CancellationToken ct = default);
    Task<bool> DeleteInspectionDefinitionAsync(string code, int version, CancellationToken ct = default);

    Task<PhaseDefinition> UpsertPhaseDefinitionAsync(PhaseDefinition definition, CancellationToken ct = default);
    Task<IReadOnlyList<PhaseDefinition>> ListPhaseDefinitionsAsync(CancellationToken ct = default);
    Task<PhaseDefinition?> GetPhaseDefinitionAsync(string code, CancellationToken ct = default);
    Task<bool> DeletePhaseDefinitionAsync(string code, CancellationToken ct = default);

    Task<PhaseMapping> UpsertPhaseMappingAsync(PhaseMapping mapping, CancellationToken ct = default);
    Task<IReadOnlyList<PhaseMapping>> ListPhaseMappingsAsync(CancellationToken ct = default);
    Task<PhaseMapping?> GetPhaseMappingAsync(string mappingId, CancellationToken ct = default);
    Task<bool> DeletePhaseMappingAsync(string mappingId, CancellationToken ct = default);

    Task<FeatureDefinition> UpsertFeatureDefinitionAsync(FeatureDefinition definition, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureDefinition>> ListFeatureDefinitionsAsync(CancellationToken ct = default);
    Task<FeatureDefinition?> GetFeatureDefinitionAsync(string code, CancellationToken ct = default);
    Task<bool> DeleteFeatureDefinitionAsync(string code, CancellationToken ct = default);
}

