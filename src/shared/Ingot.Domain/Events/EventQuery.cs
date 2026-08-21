namespace Ingot.Domain.Events;

public sealed record EventQuery : EventFilter
{
    public long? AfterSeq { get; init; }
}
