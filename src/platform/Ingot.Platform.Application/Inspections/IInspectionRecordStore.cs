using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

public interface IInspectionRecordStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<StoreInspectionRecordResult> CreateAsync(
        CreateInspectionRecordRequest request,
        bool submitterVerified,
        CancellationToken ct = default);

    Task<InspectionRecord?> GetAsync(Guid recordId, CancellationToken ct = default);

    Task<InspectionRecord?> GetCorrectionForAsync(Guid recordId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionScope>> ListScopesAsync(CancellationToken ct = default);

    Task<InspectionScope?> GetScopeAsync(string scopeId, CancellationToken ct = default);

    Task<InspectionScope> UpsertScopeAsync(InspectionScope scope, CancellationToken ct = default);

    Task<bool> DeleteScopeAsync(string scopeId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionRecord>> QueryAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default);

    Task<InspectionRecordPage> QueryPageAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default);

    Task<IReadOnlyList<InspectionRecord>> QueryAllByExecutionIdsAsync(
        IReadOnlyCollection<string> executionIds,
        CancellationToken ct = default);
}

public sealed record StoreInspectionRecordResult
{
    public required InspectionRecord Record { get; init; }

    public required bool Created { get; init; }

    public required bool PayloadConflict { get; init; }
}
