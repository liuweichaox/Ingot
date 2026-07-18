using Ingot.Contracts.Inspections;

namespace Ingot.Central.Infrastructure.Inspections;

public interface IInspectionRecordStore
{
    Task InitializeAsync(CancellationToken ct = default);

    Task<StoreInspectionRecordResult> CreateAsync(
        CreateInspectionRecordRequest request,
        bool submitterVerified,
        CancellationToken ct = default);

    Task<InspectionRecord?> GetAsync(Guid recordId, CancellationToken ct = default);

    Task<IReadOnlyList<InspectionRecord>> QueryAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default);
}

public sealed record StoreInspectionRecordResult
{
    public required InspectionRecord Record { get; init; }

    public required bool Created { get; init; }

    public required bool PayloadConflict { get; init; }
}

