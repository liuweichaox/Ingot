using System.Text.Json;
using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class OptimizerContractTests
{
    [Fact]
    public async Task SharedSuggestionResponseFixture_ShouldDeserializeWithoutLosingContractFields()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "contract-fixtures",
            "optimizer-suggestion-response.json");
        var json = await File.ReadAllTextAsync(path);

        var response = JsonSerializer.Deserialize<OptimizerSuggestionResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(response);
        Assert.Equal("contract-fixture-v1", response.ModelVersion);
        Assert.Equal(3, response.ObservationCount);
        Assert.Equal("molding-v2", response.FeatureSetId);
        Assert.Equal(2, response.FeatureSetVersion);
        Assert.Equal(4, response.DerivedFeatureCount);
        Assert.False(response.StatePersisted);
        var suggestion = Assert.Single(response.Suggestions);
        Assert.Equal(512.5, suggestion.RecommendedParameters["temperature"]);
        Assert.Equal(0.012, suggestion.Predictions["defect_rate"].Mean);
    }
}
