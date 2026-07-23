using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.Cycles;

public interface ICycleRecordService
{
    Task<CycleRecordQueryResult> QueryAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? productSeries,
        string? productCode,
        string? recipeId,
        string? machineId,
        string? workpieceId,
        string? correlationId,
        string? status,
        int limit,
        int offset = 0,
        CancellationToken ct = default);
}
