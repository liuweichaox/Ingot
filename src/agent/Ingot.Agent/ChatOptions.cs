// 定义 Chat 模型、并发、时限和组合分析的受控配置。
namespace Ingot.Agent;

/// <summary>定义 Chat 运行、模型路由和组合分析的安全配置。</summary>
public sealed class ChatOptions
{
    public bool Enabled { get; set; }

    public string Provider { get; set; } = "Deterministic";

    public string Protocol { get; set; } = "Responses";

    public string FastModel { get; set; } = "deterministic-v1";

    public string ReasoningModel { get; set; } = "deterministic-v1";

    public string? BaseUrl { get; set; }

    public bool ProbeOnStartup { get; set; } = true;

    public int ProbeTimeoutSeconds { get; set; } = 10;

    public int MaxToolCalls { get; set; } = 8;

    public int MaxRunSeconds { get; set; } = 60;

    public int MaxConcurrentRuns { get; set; } = 8;

    public int MaxConcurrentRunsPerUser { get; set; } = 2;

    public int MaxEventRowsPerTool { get; set; } = 50_000;

    public int MaxProcessExecutionsPerTool { get; set; } = 200;

    public int MaxTimeSeriesFramesPerTool { get; set; } = 100_000;

    public bool EnableCombinedAnalysis { get; set; }

    public int MaxDiscussionRounds { get; set; } = 3;

    public int MaxDiscussionTurns { get; set; } = 9;

    public Dictionary<string, ModelPricingOptions> ModelPricing { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>定义单个模型的可选成本估算参数。</summary>
public sealed class ModelPricingOptions
{
    public decimal InputPerMillionTokens { get; set; }

    public decimal OutputPerMillionTokens { get; set; }

    public string Currency { get; set; } = "USD";
}
