namespace Ingot.Agent;

public sealed class ChatOptions
{
    public bool Enabled { get; set; }

    public string Provider { get; set; } = "Deterministic";

    public string FastModel { get; set; } = "deterministic-v1";

    public string ReasoningModel { get; set; } = "deterministic-v1";

    public int MaxToolCalls { get; set; } = 8;

    public int MaxRunSeconds { get; set; } = 60;

    public bool EnableCombinedAnalysis { get; set; }

    public int MaxDiscussionRounds { get; set; } = 3;

    public int MaxDiscussionTurns { get; set; } = 9;

    public bool RequireToken { get; set; } = true;

    public Dictionary<string, string> UserTokens { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, ModelPricingOptions> ModelPricing { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ModelPricingOptions
{
    public decimal InputPerMillionTokens { get; set; }

    public decimal OutputPerMillionTokens { get; set; }

    public string Currency { get; set; } = "USD";
}
