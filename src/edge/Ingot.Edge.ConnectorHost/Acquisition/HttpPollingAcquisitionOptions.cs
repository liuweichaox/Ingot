using Ingot.Contracts.Events;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class HttpPollingAcquisitionOptions
{
    public bool Enabled { get; init; }
    /// <summary>
    ///     平台已接管采集配置时，是否允许继续使用本地回退配置。默认关闭，避免未经版本化和语义校验的
    ///     本地采集任务持续向平台写入无法用于追因的数据。仅离线调试或明确隔离的模拟环境可开启。
    /// </summary>
    public bool AllowLocalFallbackWhenPlatformAvailable { get; init; }
    /// <summary>
    ///     平台最后一次成功下发配置的本地缓存。相对路径以 ConnectorHost 程序目录为基准。
    ///     平台暂时不可用或 Edge 重启时优先恢复此版本，不会静默切换到另一套本地采集定义。
    /// </summary>
    public string DeploymentCachePath { get; init; } = "Data/acquisition-deployments.json";
    public string DeviceBaseUrl { get; init; } = string.Empty;
    public string SnapshotPath { get; init; } = "/api/v1/snapshot";
    public int PollIntervalMs { get; init; } = 1000;
    public int TimeoutMs { get; init; } = 10000;
    /// <summary>
    ///     已有配置升级时，候选工作器必须在该时间内产生首个成功采样；否则停止候选并恢复旧版本。
    ///     这是 Edge 本地的切换保护参数，不属于设备采集配置版本。
    /// </summary>
    public int StartupHealthTimeoutMs { get; init; } = 30000;
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
    public ProcessSpecificationFieldMapping? ProcessSpecification { get; init; }
    public LifecycleFieldMapping? Lifecycle { get; init; }
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
    /// <summary>MQTT 多主题订阅时，该字段来自哪个订阅主题；留空表示合并快照。</summary>
    public string? Topic { get; init; }
}

public sealed class ContextFieldMapping
{
    public required string SourcePath { get; init; }
    public required string Key { get; init; }
    public bool Required { get; init; }
    /// <summary>MQTT 多主题订阅时，该上下文来自哪个订阅主题；留空表示合并快照。</summary>
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
