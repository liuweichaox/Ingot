namespace Ingot.Contracts.ProcessImprovement;

public static class MechanismModelStatuses
{
    public const string Draft = "draft";
    public const string Validated = "validated";
    public const string Active = "active";
    public const string Retired = "retired";

    public static bool IsValid(string? value)
        => value is Draft or Validated or Active or Retired;
}

public static class MechanismFusionModes
{
    public const string Calibration = "calibration";
    public const string PostProcessing = "post-processing";
    public const string MechanismAsFeature = "mechanism-as-feature";
    public const string Ensemble = "ensemble";

    public static bool IsValid(string? value)
        => value is Calibration or PostProcessing or MechanismAsFeature or Ensemble;
}

public sealed record MechanismVariableDefinition
{
    public required string Code { get; init; }
    public required string Unit { get; init; }
    public double? ValidMinimum { get; init; }
    public double? ValidMaximum { get; init; }
}

/// <summary>
/// Versioned, executable mechanism model. The first implementation deliberately supports
/// an auditable affine equation rather than arbitrary uploaded code.
/// </summary>
public sealed record MechanismModelVersion
{
    public required string ModelId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string Status { get; init; } = MechanismModelStatuses.Draft;
    public string EquationKind { get; init; } = "affine";
    public IReadOnlyList<MechanismVariableDefinition> Inputs { get; init; } = [];
    public required MechanismVariableDefinition Output { get; init; }
    public double Intercept { get; init; }
    public IReadOnlyDictionary<string, double> Coefficients { get; init; }
        = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, string> ApplicabilityContext { get; init; }
        = new Dictionary<string, string>();
    public required string ScientificBasis { get; init; }
    public string? SourceReference { get; init; }
    public string ContentHash { get; init; } = "";
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public string? ValidatedBy { get; init; }
    public DateTimeOffset? ValidatedAt { get; init; }
}

public sealed record MechanismFusionDefinition
{
    public required string FusionId { get; init; }
    public int Version { get; init; } = 1;
    public required string Name { get; init; }
    public string Status { get; init; } = MechanismModelStatuses.Draft;
    public required string Mode { get; init; }
    public required string MechanismModelId { get; init; }
    public int MechanismModelVersion { get; init; } = 1;
    public string? DataModelId { get; init; }
    public int? DataModelVersion { get; init; }
    public double CalibrationScale { get; init; } = 1;
    public double CalibrationOffset { get; init; }
    public double PostProcessingGain { get; init; } = 1;
    public double MechanismReference { get; init; }
    public double MechanismWeight { get; init; } = 0.5;
    public string MechanismFeatureCode { get; init; } = "mechanism.output";
    public required string OutputCode { get; init; }
    public IReadOnlyDictionary<string, string> ApplicabilityContext { get; init; }
        = new Dictionary<string, string>();
    public string ContentHash { get; init; } = "";
    public string CreatedBy { get; init; } = "";
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed record MechanismFusionExecutionRequest
{
    public required string FusionId { get; init; }
    public int FusionVersion { get; init; } = 1;
    public IReadOnlyDictionary<string, double> MechanismInputs { get; init; }
        = new Dictionary<string, double>();
    public double? DataPrediction { get; init; }
    public IReadOnlyDictionary<string, string> OperatingContext { get; init; }
        = new Dictionary<string, string>();
}

public sealed record MechanismFusionExecutionResult
{
    public required string FusionId { get; init; }
    public int FusionVersion { get; init; }
    public required string Mode { get; init; }
    public double MechanismPrediction { get; init; }
    public double? DataPrediction { get; init; }
    public double? FusedPrediction { get; init; }
    public IReadOnlyDictionary<string, double> AugmentedFeatures { get; init; }
        = new Dictionary<string, double>();
    public required string OutputCode { get; init; }
    public required string OutputUnit { get; init; }
    public required string MechanismModelHash { get; init; }
    public required string FusionDefinitionHash { get; init; }
    public required string ExecutionHash { get; init; }
}
