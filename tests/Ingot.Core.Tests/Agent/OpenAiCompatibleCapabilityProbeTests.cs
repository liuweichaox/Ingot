using Ingot.Agent.Providers;
using Xunit;

namespace Ingot.Core.Tests.Agent;

public sealed class OpenAiCompatibleCapabilityProbeTests
{
    [Fact]
    public void BuildModelsUri_RequiresVersionedApiRoot()
    {
        Assert.Equal(
            "http://model-host:8000/v1/models",
            OpenAiCompatibleCapabilityProbe.BuildModelsUri("http://model-host:8000/v1").ToString());
        Assert.Throws<InvalidOperationException>(() =>
            OpenAiCompatibleCapabilityProbe.BuildModelsUri("http://model-host:8000"));
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
