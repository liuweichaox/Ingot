namespace Ingot.Contracts.Events;

public sealed record ProcessAnalysisWindowSelection
{
    public required string WindowId { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public string? Label { get; init; }
}

public sealed record ProcessWindowComparisonRequest
{
    public string AnalysisScope { get; init; } = "analysis-window";
    public required string BaselineWindowId { get; init; }
    public IReadOnlyList<ProcessAnalysisWindowSelection> Windows { get; init; } = [];
}

public sealed record ProcessWindowComparisonResult
{
    public required string BaselineWindowId { get; init; }
    public required string AnalysisPlanId { get; init; }
    public int AnalysisPlanVersion { get; init; }
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; }
    public required string AnalysisScope { get; init; }
    public required string AlignmentMode { get; init; }
    public required ProcessWindowComparisonRow Baseline { get; init; }
    public IReadOnlyList<ProcessWindowComparisonRow> ComparisonWindows { get; init; } = [];
}

public sealed record ProcessWindowComparisonRow
{
    public required string WindowId { get; init; }
    public string? Label { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public long EventCount { get; init; }
    public int SampleCount { get; init; }
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<CycleSignalStatistic> Signals { get; init; } = [];
    public ProcessWindowQualitySummary Quality { get; init; } = new();
}

public sealed record ProcessWindowQualitySummary
{
    public int ScopeCount { get; init; }
    public int InspectionCount { get; init; }
    public int PassCount { get; init; }
    public int FailCount { get; init; }
    public double? PassRate { get; init; }
    public IReadOnlyList<ProcessWindowQualityCharacteristic> Characteristics { get; init; } = [];
}

public sealed record ProcessWindowQualityCharacteristic
{
    public required string Code { get; init; }
    public int SampleCount { get; init; }
    public double? Average { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
}
