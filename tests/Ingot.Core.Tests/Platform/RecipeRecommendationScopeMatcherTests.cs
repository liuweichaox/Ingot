using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class RecipeRecommendationScopeMatcherTests
{
    [Fact]
    public void Matches_ExplicitMissingScope_DoesNotMatchRecordedValues()
    {
        var context = new Dictionary<string, string>
        {
            ["missing_scope_fields"] = "product_family_code",
            ["product_code"] = "product"
        };

        Assert.True(RecipeRecommendationScopeMatcher.Matches(context, "product_family_code", null));
        Assert.True(RecipeRecommendationScopeMatcher.Matches(context, "product_family_code", " "));
        Assert.False(RecipeRecommendationScopeMatcher.Matches(context, "product_family_code", "family"));
        Assert.True(RecipeRecommendationScopeMatcher.Matches(context, "product_code", "product"));
        Assert.False(RecipeRecommendationScopeMatcher.Matches(context, "product_code", "other"));
    }

    [Fact]
    public void Matches_OldRequestWithoutScopeField_StillMatchesAnyValue()
    {
        var context = new Dictionary<string, string>();

        Assert.True(RecipeRecommendationScopeMatcher.Matches(context, "product_family_code", null));
        Assert.True(RecipeRecommendationScopeMatcher.Matches(context, "product_family_code", "family"));
    }
}
