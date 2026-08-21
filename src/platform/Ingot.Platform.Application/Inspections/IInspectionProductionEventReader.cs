using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.Inspections;

public interface IInspectionProductionEventReader
{
    Task<IReadOnlyList<PlatformProductionEvent>> QueryCompletedAsync(
        string? executionId,
        CancellationToken ct = default);
}
