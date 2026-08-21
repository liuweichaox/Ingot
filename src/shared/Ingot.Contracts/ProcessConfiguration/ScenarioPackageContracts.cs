namespace Ingot.Contracts.ProcessConfiguration;

public static class ScenarioContextModes
{
    public const string RequiredForAnalysis = "required-for-analysis";
    public const string RecordWhenAvailable = "record-when-available";
    public const string ValidatedForModeling = "validated-for-modeling";

    public static bool IsValid(string? value)
        => value is RequiredForAnalysis or RecordWhenAvailable or ValidatedForModeling;
}

public sealed record ScenarioPackage
{
    public required string PackageId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; } = 1;
    public required string AnalysisPlanId { get; init; }
    public int AnalysisPlanVersion { get; init; } = 1;
    public IReadOnlyList<VersionedConfigurationReference> IngestionTasks { get; init; } = [];
    public VersionedConfigurationReference? QualityPlan { get; init; }
    public IReadOnlyList<ScenarioContextFieldPolicy> ContextFields { get; init; } = [];
    public IReadOnlyList<ScenarioConstraintDefinition> Constraints { get; init; } = [];
    public IReadOnlyList<VersionedConfigurationReference> KnowledgeAssets { get; init; } = [];
    public IReadOnlyDictionary<string, string> Terminology { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record VersionedConfigurationReference
{
    public required string Id { get; init; }
    public int Version { get; init; } = 1;
}

public sealed record ScenarioContextFieldPolicy
{
    public required string FieldCode { get; init; }
    public required string Name { get; init; }
    public string Mode { get; init; } = ScenarioContextModes.RecordWhenAvailable;
    public double? MinimumCoverage { get; init; }
    public double? MinimumFactorOverlap { get; init; }
}

public sealed record ScenarioConstraintDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string Severity { get; init; } = "hard";
    public string? Unit { get; init; }
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
}
