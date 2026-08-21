namespace Ingot.Edge.Application.Abstractions;

public interface IEventShipper
{
    Task RunAsync(CancellationToken ct = default);
}
