namespace Ingot.Application.Abstractions;

/// <summary>
///     按边缘序号将本地 outbox 复制到中心，并在收到确认后推进断点。
/// </summary>
public interface IEventShipper
{
    Task RunAsync(CancellationToken ct = default);
}
