
namespace Ingot.Contracts.Events;

public sealed record TimeWindowSelection
{
    public required string WindowId { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public string? Label { get; init; }
}

public sealed record TimeWindowComparisonRequest
{
    public string AnalysisScope { get; init; } = "analysis-window";
    public required string BaselineWindowId { get; init; }
    public IReadOnlyList<TimeWindowSelection> Windows { get; init; } = [];
}

public sealed record TimeWindowComparisonResult
{
    public required string BaselineWindowId { get; init; }
    public required string AnalysisPlanId { get; init; }
    public int AnalysisPlanVersion { get; init; }
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; }
    public required string AnalysisScope { get; init; }
    public required string AlignmentMode { get; init; }
    public required TimeWindowComparisonRow Baseline { get; init; }
    public IReadOnlyList<TimeWindowComparisonRow> ComparisonWindows { get; init; } = [];
}

public sealed record TimeWindowComparisonRow
{
    public required string WindowId { get; init; }
    public string? Label { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public long EventCount { get; init; }
    public int SampleCount { get; init; }
    public ProcessDataQualitySummary ProcessDataQuality { get; init; } = new();
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<ProcessSignalStatistic> Signals { get; init; } = [];
    public TimeWindowQualitySummary Quality { get; init; } = new();
}

public sealed record TimeWindowQualitySummary
{
    public int ScopeCount { get; init; }
    public int InspectionCount { get; init; }
    public int PassCount { get; init; }
    public int FailCount { get; init; }
    public double? PassRate { get; init; }
    public IReadOnlyList<TimeWindowQualityCharacteristic> Characteristics { get; init; } = [];
}

public sealed record TimeWindowQualityCharacteristic
{
    public required string Code { get; init; }
    public int SampleCount { get; init; }
    public double? Average { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
}
