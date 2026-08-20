using System.Net;
using System.Text;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessOptimizerClientContractTests
{
    [Fact]
    public async Task SuggestAsync_ShouldRejectMismatchedFeatureSetContract()
    {
        const string response = """
            {
              "model_version": "fixture-v1",
              "observation_count": 0,
              "suggestions": [{
                "recommended_params": {"x": 0.5},
                "objective_predictions": {},
                "constraint_predictions": {},
                "predicted_distance_to_spec": null,
                "feasibility_probability": null,
                "acquisition_value": null,
                "cold_start": true,
                "model_version": "fixture-v1",
                "rationale": "fixture"
              }],
              "feature_set_id": "unexpected",
              "feature_set_version": 1,
              "derived_feature_count": 0,
              "state_persisted": false
            }
            """;
        using var httpClient = new HttpClient(new JsonResponseHandler(response))
        {
            BaseAddress = new Uri("http://optimizer.test/")
        };
        var client = new ProcessOptimizerClient(
            httpClient,
            Options.Create(new ProcessOptimizerOptions { Enabled = true }));

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            client.SuggestAsync(CreateCall("expected")));

        Assert.Contains("特征集契约", error.Message, StringComparison.Ordinal);
    }

    private static OptimizerSuggestionCall CreateCall(string featureSetId) => new()
    {
        Campaign = new OptimizerCampaignInput
        {
            Name = "contract-test",
            FeatureSetId = featureSetId,
            Variables = [new OptimizerVariableInput("x", 0, 1, "")],
            Objectives =
            [
                new OptimizerObjectiveInput
                {
                    Name = "loss",
                    Kind = "le",
                    Threshold = 0.1
                }
            ]
        }
    };

    private sealed class JsonResponseHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        });
    }
}
