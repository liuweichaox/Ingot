using System.Text.Json;

namespace Ingot.Contracts.ProcessConfiguration;

public static class ConfigurationStatuses
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Retired = "retired";

    public static bool IsValid(string? value) => value is Draft or Published or Retired;
}

public sealed record ProcessDataModel
{
    public required string ModelId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public AcquisitionModel Acquisition { get; init; } = new();
    public IReadOnlyList<RecipeParameterDefinition> RecipeParameters { get; init; } = [];
    public IReadOnlyList<ProcessStageDefinition> Stages { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AcquisitionModel
{
    public int SamplePeriodMs { get; init; } = 1000;
    public string? StepSourceKey { get; init; }
    public IReadOnlyList<ProcessDataItemDefinition> DataItems { get; init; } = [];
}

public sealed record ProcessDataItemDefinition
{
    public required string Code { get; init; }
    public required string SourceField { get; init; }
    public string DataType { get; init; } = "double";
    public string? Unit { get; init; }
    public string Category { get; init; } = "process";
    public bool Nullable { get; init; } = true;
}

public sealed record RecipeParameterDefinition
{
    public required string Code { get; init; }
    public required string SourceField { get; init; }
    public string DataType { get; init; } = "double";
    public string? Unit { get; init; }
    public bool Nullable { get; init; } = true;
}

public sealed record ProcessStageDefinition
{
    public required string SourceStep { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
    public int ExpectedDurationSeconds { get; init; }
    public bool Required { get; init; } = true;
}

public sealed record RecipeVersion
{
    public required string RecipeId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public int? BasedOnVersion { get; init; }
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; } = 1;
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<RecipeParameterValue> Values { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record RecipeParameterValue
{
    public required string Code { get; init; }
    public JsonElement Value { get; init; }
}

public sealed record ProcessAnalysisPlan
{
    public required string PlanId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; } = 1;
    public string AnalysisScope { get; init; } = "production-cycle";
    public string AlignmentMode { get; init; } = "stage-relative";
    public string? CohortDimension { get; init; }
    /// <summary>决定哪些运行记录属于同一可比组；键来自不可变运行上下文。</summary>
    public IReadOnlyList<string> ComparisonKeys { get; init; } = ["product_series"];
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<AnalysisSignalSelection> Signals { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AnalysisSignalSelection
{
    public required string DataItemCode { get; init; }
    public bool IncludeTrace { get; init; } = true;
    public IReadOnlyList<string> Features { get; init; } = [];
}
