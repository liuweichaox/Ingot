// 定义 OpenAI-compatible 协议名称及通用供应商判定，不包含供应商分支。
namespace Ingot.Agent.Providers;

internal static class OpenAiCompatibleModelConfiguration
{
    public const string ResponsesProtocol = "Responses";
    public const string ChatCompletionsProtocol = "ChatCompletions";

    public static bool UsesExternalModel(string? provider)
        => !string.IsNullOrWhiteSpace(provider) &&
           !string.Equals(provider, "Deterministic", StringComparison.OrdinalIgnoreCase);

    public static bool IsSupportedProtocol(string? protocol)
        => string.Equals(protocol, ResponsesProtocol, StringComparison.OrdinalIgnoreCase) ||
           string.Equals(protocol, ChatCompletionsProtocol, StringComparison.OrdinalIgnoreCase);

}
