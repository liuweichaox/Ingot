namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class HttpPollingAcquisitionOptions
{
    public bool Enabled { get; init; }
    public string DeviceBaseUrl { get; init; } = string.Empty;
    public string SnapshotPath { get; init; } = "/api/v1/snapshot";
    public int PollIntervalMs { get; init; } = 1000;
    public int SamplePeriodMs { get; init; } = 1000;
    public string Source { get; init; } = "connector/http-polling";
    public string SubjectType { get; init; } = "equipment";
    public string SubjectId { get; init; } = string.Empty;
    public string TimestampPath { get; init; } = "timestamp";
    public string TimestampMode { get; init; } = "source";
    public string? SequencePath { get; init; } = "sequence";
    public string SampleEventType { get; init; } = "process.sample";
    public IReadOnlyDictionary<string, string> StaticContext { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<ContextFieldMapping> ContextFields { get; init; } = [];
    public IReadOnlyList<ValueFieldMapping> Fields { get; init; } = [];
    public RecipeFieldMapping? Recipe { get; init; }
    public LifecycleFieldMapping? Lifecycle { get; init; }
}

public sealed class ValueFieldMapping
{
    public required string SourcePath { get; init; }
    public required string Code { get; init; }
    public string DataType { get; init; } = "double";
    public bool Required { get; init; } = true;
    public double Scale { get; init; } = 1;
    public double Offset { get; init; }
}

public sealed class ContextFieldMapping
{
    public required string SourcePath { get; init; }
    public required string Key { get; init; }
    public bool Required { get; init; }
}

public sealed class RecipeFieldMapping
{
    public string EventType { get; init; } = "recipe.applied";
    public required string IdPath { get; init; }
    public required string VersionPath { get; init; }
    public string? NamePath { get; init; }
    public required string ParametersPath { get; init; }
    public IReadOnlyList<ValueFieldMapping> ParameterFields { get; init; } = [];
}

public sealed class LifecycleFieldMapping
{
    public string Mode { get; init; } = "discrete-cycle";
    public string CorrelationIdContextKey { get; init; } = "correlation_id";
    public string? StepContextKey { get; init; } = "recipe_step";
    public string? StepNameContextKey { get; init; } = "recipe_step_name";
    public string StartedEventType { get; init; } = "cycle.started";
    public string CompletedEventType { get; init; } = "cycle.completed";
    public string StepChangedEventType { get; init; } = "recipe.step_changed";
    public int? ExpectedDurationMs { get; init; }
}
