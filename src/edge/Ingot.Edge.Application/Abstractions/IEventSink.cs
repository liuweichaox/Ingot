using Ingot.Domain.Events;

namespace Ingot.Edge.Application.Abstractions;

public interface IEventSink
{

    ValueTask<ProductionEvent> EmitAsync(ProductionEvent evt, CancellationToken ct = default);

    ValueTask<IReadOnlyList<ProductionEvent>> EmitBatchAsync(
        IReadOnlyList<ProductionEvent> events,
        CancellationToken ct = default);
}
