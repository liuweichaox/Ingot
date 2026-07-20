using Ingot.Domain.Events;

namespace Ingot.Edge.Application.Abstractions;

/// <summary>
///     按资产维护的边缘业务上下文。
/// </summary>
public interface IEdgeContextStore
{
    string? Get(ObjectRef asset, string key);

    Task SetAsync(ObjectRef asset, string key, string value, CancellationToken ct = default);

    IReadOnlyDictionary<string, string> Snapshot(ObjectRef asset, IReadOnlyList<string> keys);
}
