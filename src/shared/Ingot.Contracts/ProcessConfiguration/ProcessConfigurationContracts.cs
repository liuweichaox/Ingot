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
    public IReadOnlyList<ControlParameterDefinition> ControlParameters { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record AcquisitionModel
{
    public IReadOnlyList<ProcessDataItemDefinition> DataItems { get; init; } = [];
}

public sealed record ProcessDataItemDefinition
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }
    public string DataType { get; init; } = "double";
    public string? Unit { get; init; }
    public string Category { get; init; } = "process";
    public bool Nullable { get; init; } = true;
}

public sealed record ControlParameterDefinition
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }
    public string DataType { get; init; } = "double";
    public string? Unit { get; init; }
    public bool Nullable { get; init; } = true;
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public double? Step { get; init; }
    public bool ChangeAllowed { get; init; } = true;
}

public sealed record ProcessSpecificationEvidenceReference
{
    public required string Kind { get; init; }
    public required string ReferenceId { get; init; }
}

/// <summary>
/// The only command for deriving a new process-specification version from an
/// already published baseline. Identity, version and model are inherited by
/// the server and are deliberately absent from this request.
/// </summary>
public sealed record CreateProcessSpecificationDraftRequest
{
    public required string ChangeReason { get; init; }
    public string? MechanismNotes { get; init; }
    public IReadOnlyList<ProcessSpecificationEvidenceReference> EvidenceReferences { get; init; } = [];
    public IReadOnlyList<ControlParameterValue> ParameterOverrides { get; init; } = [];
}

public sealed record ProcessSpecificationDraftCreationResult
{
    public ProcessSpecification? Draft { get; init; }
    public string? Conflict { get; init; }

    public bool Succeeded => Draft is not null;
}

public sealed record ProcessSpecification
{
    public required string ProcessSpecificationId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public int? BasedOnVersion { get; init; }
    public required string DataModelId { get; init; }
    public int DataModelVersion { get; init; } = 1;
    public string Status { get; init; } = ConfigurationStatuses.Draft;
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<ControlParameterValue> Values { get; init; } = [];
    public string? ChangeReason { get; init; }
    public string? MechanismNotes { get; init; }
    public IReadOnlyList<ProcessSpecificationEvidenceReference> EvidenceReferences { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record ControlParameterValue
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
    public string AnalysisScope { get; init; } = "production-execution";
    public string AlignmentMode { get; init; } = "stage-relative";
    public string? CohortDimension { get; init; }

    public IReadOnlyList<string> ComparisonKeys { get; init; } = ["product_family_code"];
    public IReadOnlyDictionary<string, string> ContextSelector { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<KnownUnmeasuredConfounderDefinition> KnownUnmeasuredConfounders { get; init; } = [];
    public IReadOnlyList<AnalysisSignalSelection> Signals { get; init; } = [];
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record KnownUnmeasuredConfounderDefinition
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
}

public sealed record AnalysisSignalSelection
{
    public required string DataItemCode { get; init; }
    public bool IncludeTrace { get; init; } = true;
    public IReadOnlyList<string> Features { get; init; } = [];
}
