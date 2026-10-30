// 定义时序样本、查询和运行帧使用的应用层契约。
using Ingot.Domain.Events;

namespace Ingot.Platform.Application.TimeSeries;

public sealed record SignalSample
{
    public required string CollectionPointId { get; init; }
    public required string SignalCode { get; init; }
    public required string DataType { get; init; }
    public string? Unit { get; init; }
    public required string Category { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? IngestedAt { get; init; }
    public required string EventId { get; init; }
    public long IngestId { get; init; }
    public long? SourceSequence { get; init; }
    public required string SiteId { get; init; }
    public required string EdgeId { get; init; }
    public required string Source { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? ExecutionId { get; init; }
    public string? PhaseCode { get; init; }
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; }
    public AppliedConfigurationRef? AppliedConfiguration { get; init; }
    public string QualityCode { get; init; } = SignalQualityCodes.Good;
    public short QualityEvidenceVersion { get; init; }
    public double? NumericValue { get; init; }
    public long? IntegerValue { get; init; }
    public bool? BooleanValue { get; init; }
    public string? TextValue { get; init; }
}

public static class SignalQualityCodes
{
    public const short CurrentEvidenceVersion = 1;
    public const string Good = "good";
    public const string Uncertain = "uncertain";
    public const string Bad = "bad";
}

public sealed record TimeSeriesQuery
{
    public string? SiteId { get; init; }
    public string? CollectionPointId { get; init; }
    public string? SignalCode { get; init; }
    public string? ExecutionId { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public AppliedConfigurationRef? AppliedConfiguration { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public DateTimeOffset? AfterOccurredAt { get; init; }
    public long? AfterFrameId { get; init; }
    public int Limit { get; init; } = 10_000;
}

public sealed record ProcessSampleFrame
{
    public required string EventId { get; init; }
    public string? EdgeId { get; init; }
    public long IngestId { get; init; }
    public long? SourceSequence { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? IngestedAt { get; init; }
    public string? PhaseCode { get; init; }
    public AppliedConfigurationRef? AppliedConfiguration { get; init; }
    public IReadOnlyDictionary<string, double> NumericValues { get; init; } =
        new Dictionary<string, double>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, string> QualityCodes { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
