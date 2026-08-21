using Ingot.Contracts.Events;
using Ingot.Domain.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class HttpPollingAcquisitionOptions
{
    public string? ConfigurationKind { get; init; }
    public string? ConfigurationId { get; init; }
    public int? ConfigurationVersion { get; init; }
    public bool Enabled { get; init; }

    public bool AllowLocalFallbackWhenPlatformAvailable { get; init; }

    public string DeploymentCachePath { get; init; } = "Data/acquisition-deployments.json";
    public string DeviceBaseUrl { get; init; } = string.Empty;
    public string SnapshotPath { get; init; } = "/api/v1/snapshot";
    public string Method { get; init; } = "get";
    public string? ContentType { get; init; }
    public string? RequestBody { get; init; }
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> HeaderSecretRefs { get; init; } = new Dictionary<string, string>();
    public int PollIntervalMs { get; init; } = 1000;
    public int TimeoutMs { get; init; } = 10000;
    public int ReconnectDelayMs { get; init; } = 5000;
    public int SourceIdentityStaleAfterMs { get; init; } = 60_000;
    public int MaximumFutureTimestampSkewMs { get; init; } = 300_000;

    public int StartupHealthTimeoutMs { get; init; } = 30000;
    public string Source { get; init; } = "connector/http-polling";
    public string SubjectType { get; init; } = "equipment";
    public string SubjectId { get; init; } = string.Empty;
    public string TimestampPath { get; init; } = "timestamp";
    public string TimestampMode { get; init; } = "source";
    public string TimestampEncoding { get; init; } = "iso-8601";
    public string? SequencePath { get; init; }
    public string SampleEventType { get; init; } = "process.sample";
    public IReadOnlyDictionary<string, string> StaticContext { get; init; }
        = new Dictionary<string, string>();
    public IReadOnlyList<ContextFieldMapping> ContextFields { get; init; } = [];
    public IReadOnlyList<ValueFieldMapping> Fields { get; init; } = [];
    public ProcessSpecificationFieldMapping? ProcessSpecification { get; init; }
    public LifecycleFieldMapping? Lifecycle { get; init; }

    public AppliedConfigurationRef? AppliedConfiguration
        => string.IsNullOrWhiteSpace(ConfigurationKind) ||
           string.IsNullOrWhiteSpace(ConfigurationId) ||
           ConfigurationVersion is not > 0
            ? null
            : new AppliedConfigurationRef(
                ConfigurationKind,
                ConfigurationId,
                ConfigurationVersion.Value);
}

public sealed class ValueFieldMapping
{
    public required string SourcePath { get; init; }
    public required string Code { get; init; }
    public string DataType { get; init; } = "double";
    public string Category { get; init; } = "process";
    public bool Required { get; init; } = true;
    public double Scale { get; init; } = 1;
    public double Offset { get; init; }
    public string? QualityPath { get; init; }
    public IReadOnlyList<string> AcceptedQualityValues { get; init; } = [];
    public double? Minimum { get; init; }
    public double? Maximum { get; init; }
    public string OutOfRangeBehavior { get; init; } = "reject";
    public string MissingValueBehavior { get; init; } = "inherit";
    public string? DefaultValue { get; init; }

    public string? Topic { get; init; }
}

public sealed class ContextFieldMapping
{
    public required string SourcePath { get; init; }
    public required string Key { get; init; }
    public bool Required { get; init; }

    public string? Topic { get; init; }
}

public sealed class ProcessSpecificationFieldMapping
{
    public string EventType { get; init; } = "process.specification.applied";
    public required string IdPath { get; init; }
    public required string VersionPath { get; init; }
    public string? NamePath { get; init; }
    public required string ParametersPath { get; init; }
    public IReadOnlyList<ValueFieldMapping> ParameterFields { get; init; } = [];
}

public sealed class LifecycleFieldMapping
{
    public string Mode { get; init; } = ProcessExecutionKinds.Discrete;
    public string? ActiveContextKey { get; init; }
    public string ActiveValue { get; init; } = "true";
    public string StartedEventType { get; init; } = "process.execution.started";
    public string CompletedEventType { get; init; } = "process.execution.completed";
    public string StepChangedEventType { get; init; } = "process.stage_changed";
}
