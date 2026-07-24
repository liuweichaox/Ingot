namespace Ingot.Platform.Infrastructure.TimeSeries;

/// <summary>
/// A canonical, typed observation produced by one physical or logical collection point.
/// This contract deliberately contains no database-specific concepts so the same semantics
/// is independent from SQL details so deterministic offline scientific runners can reuse it.
/// </summary>
public sealed record SignalSample
{
    public required string CollectionPointId { get; init; }
    public required string SignalCode { get; init; }
    public required string DataType { get; init; }
    public string? Unit { get; init; }
    public required string Category { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public required string EventId { get; init; }
    public long IngestId { get; init; }
    public required string EdgeId { get; init; }
    public required string Source { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? CorrelationId { get; init; }
    public string? PhaseCode { get; init; }
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; }
    public string QualityCode { get; init; } = SignalQualityCodes.Good;
    public double? NumericValue { get; init; }
    public long? IntegerValue { get; init; }
    public bool? BooleanValue { get; init; }
    public string? TextValue { get; init; }
    public IReadOnlyDictionary<string, string> RunContext { get; init; }
        = new Dictionary<string, string>();
}

public static class SignalQualityCodes
{
    public const string Good = "good";
    public const string Uncertain = "uncertain";
    public const string Bad = "bad";
}

public sealed record TimeSeriesQuery
{
    public string? CollectionPointId { get; init; }
    public string? SignalCode { get; init; }
    public string? CorrelationId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 10_000;
}
