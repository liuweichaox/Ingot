// 验证 Agent 的 OpenAiCompatibleCapabilityProbe 能力、只读边界和拒绝路径。

using Ingot.Agent.Providers;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class OpenAiCompatibleCapabilityProbeTests
{
    [Fact]
    public void BuildModelsUri_AppendsToConfiguredApiRoot()
    {
        Assert.Equal(
            "http://model-host:8000/v1/models",
            OpenAiCompatibleCapabilityProbe.BuildModelsUri("http://model-host:8000/v1").ToString());
        Assert.Equal(
            "https://api.deepseek.com/models",
            OpenAiCompatibleCapabilityProbe.BuildModelsUri("https://api.deepseek.com").ToString());
    }

    [Theory]
    [InlineData("https://user:secret@models.example.com/v1")]
    [InlineData("https://models.example.com/v1?tenant=unsafe")]
    [InlineData("models.example.com/v1")]
    public void BuildModelsUri_RejectsUnsafeApiRoots(string baseUrl)
    {
        Assert.Throws<InvalidOperationException>(() =>
            OpenAiCompatibleCapabilityProbe.BuildModelsUri(baseUrl));
    }

    [Fact]
    public void ReadModelIds_UsesExactReportedIdentifiers()
    {
        var models = OpenAiCompatibleCapabilityProbe.ReadModelIds(
            """{"data":[{"id":"qwen-fast"},{"id":"deepseek-reasoning"}]}""");

        Assert.Contains("qwen-fast", models);
        Assert.Contains("deepseek-reasoning", models);
        Assert.DoesNotContain("qwen", models);
    }
}
