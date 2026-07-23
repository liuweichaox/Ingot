namespace Ingot.Contracts.Analytics;

/// <summary>
///     事件库中可被分析的运行对象。对象类型由数据源提供，不限定为设备。
/// </summary>
public sealed record DataObjectSummary
{
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public string? EdgeId { get; init; }
    public long EventCount { get; init; }
    public long SampleCount { get; init; }
    public long OperationCount { get; init; }
    public DateTimeOffset? FirstObservedAt { get; init; }
    public DateTimeOffset? LastObservedAt { get; init; }
    public DateTimeOffset? LastSampleAt { get; init; }
    public double? MaximumSampleGapSeconds { get; init; }
    public string? LatestEventType { get; init; }
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record DataObjectQuery
{
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 100;
    public int Offset { get; init; }
}

public sealed record DataObjectPage
{
    public IReadOnlyList<DataObjectSummary> Data { get; init; } = [];
    public int Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
}

/// <summary>
///     一条有效质量结果及其分析上下文。既可关联生产周期，也可关联运行段或时间窗口。
/// </summary>
public sealed record QualityAnalysisRecord
{
    public required Guid RecordId { get; init; }
    public required string AnalysisScopeId { get; init; }
    public required string AnalysisScopeType { get; init; }
    public required string SubjectType { get; init; }
    public required string SubjectId { get; init; }
    public required string QualityObjectId { get; init; }
    public string? ProductSeries { get; init; }
    public string? ProductCode { get; init; }
    public string? RecipeId { get; init; }
    public string? RecipeVersion { get; init; }
    public required string DefinitionCode { get; init; }
    public int DefinitionVersion { get; init; }
    public DateTimeOffset? ScopeFrom { get; init; }
    public DateTimeOffset? ScopeTo { get; init; }
    public required DateTimeOffset MeasuredAt { get; init; }
    public required string Outcome { get; init; }
    public int MeasurementCount { get; init; }
    public int AttachmentCount { get; init; }
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record QualityAnalysisQuery
{
    public string? ProductSeries { get; init; }
    public string? SubjectType { get; init; }
    public string? SubjectId { get; init; }
    public string? Outcome { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 100;
    public int Offset { get; init; }
}

public sealed record QualityAnalysisPage
{
    public IReadOnlyList<QualityAnalysisRecord> Data { get; init; } = [];
    public int Total { get; init; }
    public int Limit { get; init; }
    public int Offset { get; init; }
}
