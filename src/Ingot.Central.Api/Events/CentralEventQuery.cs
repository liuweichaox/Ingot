namespace Ingot.Central.Api.Events;

public sealed record CentralEventQuery
{
    public string? EdgeId { get; init; }
    public string? EventType { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public long? AfterIngestId { get; init; }
    public int Limit { get; init; } = 100;
    public IReadOnlyDictionary<string, string> Context { get; init; }
        = new Dictionary<string, string>();
}
