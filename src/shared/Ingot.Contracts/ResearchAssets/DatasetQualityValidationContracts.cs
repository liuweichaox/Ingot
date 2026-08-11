namespace Ingot.Contracts.ResearchAssets;

public static class DatasetQualityValidationStatuses
{
    public const string Passed = "passed";
    public const string Rejected = "rejected";
}

public sealed record DatasetQualityValidationDatasetManifest
{
    public required string DatasetId { get; init; }
    public int Version { get; init; } = 1;
    public required string Industry { get; init; }
    public required string Process { get; init; }
    public required string DataKind { get; init; }
    public bool IsMeasuredData { get; init; }
    public required string SourceUri { get; init; }
    public string? RetrievalUri { get; init; }
    public string? ArchiveMemberPath { get; init; }
    public required string License { get; init; }
    public required string Citation { get; init; }
    public string? Doi { get; init; }
    public string? ExpectedSha256 { get; init; }
    public string? SheetName { get; init; }
    public int HeaderRowCount { get; init; } = 1;
    public string? MatVariableName { get; init; }
    public string? ProcessExecutionColumn { get; init; }
    public string? TimestampColumn { get; init; }
    public string? PhaseColumn { get; init; }
    public IReadOnlyList<string> SignalColumns { get; init; } = [];
    public IReadOnlyList<string> OutcomeColumns { get; init; } = [];
    public double MinimumSignalNumericCoverage { get; init; } = 0.8;
    public double MinimumOutcomeNumericCoverage { get; init; } = 0.3;
    public IReadOnlyDictionary<string, string> Units { get; init; } =
        new Dictionary<string, string>();
    public IReadOnlyDictionary<string, ScientificNumericRange> ValidSignalRanges { get; init; } =
        new Dictionary<string, ScientificNumericRange>();
}

public sealed record ScientificNumericRange
{
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public required string Basis { get; init; }
}

public sealed record ScientificColumnProfile
{
    public required string Column { get; init; }
    public long PresentCount { get; init; }
    public long NumericCount { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? Mean { get; init; }
}

public sealed record DatasetQualityValidationReport
{
    public Guid ReportId { get; init; }
    public required string DatasetId { get; init; }
    public int DatasetVersion { get; init; }
    public required string Industry { get; init; }
    public required string Process { get; init; }
    public required string Status { get; init; }
    public bool ResearchClaimsAllowed { get; init; }
    public required string SourceSha256 { get; init; }
    public required string ManifestSha256 { get; init; }
    public long RowCount { get; init; }
    public long ProcessExecutionCount { get; init; }
    public long ChronologyViolationCount { get; init; }
    public double StreamBatchMaximumDifference { get; init; }
    public IReadOnlyList<ScientificColumnProfile> SignalProfiles { get; init; } = [];
    public IReadOnlyList<ScientificColumnProfile> OutcomeProfiles { get; init; } = [];
    public IReadOnlyDictionary<string, long> ExcludedSampleCounts { get; init; } =
        new Dictionary<string, long>();
    public IReadOnlyList<string> DataQualityNotes { get; init; } = [];
    public IReadOnlyList<string> Issues { get; init; } = [];
    public required string RunnerVersion { get; init; }
    public string RunBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
}
