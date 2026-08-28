// 验证 OpenAI-compatible 客户端对 JSON object 响应的解析和安全拒绝路径。
using Ingot.Agent.Providers;
using Ingot.Contracts.Agents;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class ChatFrameworkOpenAiModelClientTests
{
    [Fact]
    public void DeserializeJsonResponse_ReadsCompleteJsonObject()
    {
        var plan = ChatFrameworkOpenAiModelClient.DeserializeJsonResponse<AnalysisPlan>(
            """
            {
              "intent": "list-data-objects",
              "summary": "列出当前可用运行对象",
              "toolCalls": [
                { "tool": "list_data_objects", "arguments": {} }
              ]
            }
            """);

        Assert.Equal("list-data-objects", plan.Intent);
        Assert.Single(plan.ToolCalls);
    }

    [Fact]
    public void DeserializeJsonResponse_RejectsTruncatedJsonWithSafeMessage()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ChatFrameworkOpenAiModelClient.DeserializeJsonResponse<AnalysisPlan>(
                "{\"intent\":\"list-data-objects\",\"toolCalls\":["));

        Assert.Equal("模型返回的结构化响应不完整或无效，请重试。", error.Message);
    }

    [Fact]
    public void DeserializeJsonResponse_RejectsEmptyResponseWithSafeMessage()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            ChatFrameworkOpenAiModelClient.DeserializeJsonResponse<AnalysisPlan>("  "));

        Assert.Equal("模型返回了空的结构化响应，请重试。", error.Message);
    }

    [Fact]
    public void BuildJsonPrompt_IncludesRequiredPlanProperties()
    {
        var prompt = ChatFrameworkOpenAiModelClient.BuildJsonPrompt<AnalysisPlan>("分析问题");

        Assert.Contains("JSON Schema", prompt, StringComparison.Ordinal);
        Assert.Contains("\"intent\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"summary\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"toolCalls\"", prompt, StringComparison.Ordinal);
    }
}
