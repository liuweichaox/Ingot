using Ingot.Contracts.Events;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

public interface IProcessExecutionService
{
    Task<ProcessExecutionQueryResult> QueryAsync(
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? productFamilyCode,
        string? productCode,
        string? processSpecificationId,
        string? equipmentId,
        string? outputItemId,
        string? executionId,
        string? status,
        int limit,
        int offset = 0,
        string? search = null,
        CancellationToken ct = default,
        string? edgeId = null,
        string? externalBatchRef = null);
}
