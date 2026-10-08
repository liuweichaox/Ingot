// 验证平台组件 ProcessOptimizerClientContract 的成功、拒绝和安全边界。

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessOptimizerClientContractTests
{
    [Fact]
    public async Task DiagnoseAsync_ShouldSendSharedPythonRequestContract()
    {
        var fixture = await File.ReadAllTextAsync(Path.Combine(
            AppContext.BaseDirectory, "contract-fixtures", "optimizer-diagnosis-request.json"));
        using var httpClient = new HttpClient(new DiagnosisContractHandler(fixture))
        {
            BaseAddress = new Uri("http://optimizer.test/")
        };
        var client = new ProcessOptimizerClient(
            httpClient, Options.Create(new ProcessOptimizerOptions { Enabled = true }));

        var response = await client.DiagnoseAsync(new ProcessDiagnosisCall
        {
            Features =
            [
                new ProcessDiagnosticFeatureInput
                {
                    DataSource = "control-parameter:x",
                    SourceKind = "control-parameter",
                    Actionability = "controllable"
                }
            ],
            Observations = Enumerable.Range(0, 4).Select(index => new ProcessDiagnosticObservationInput
            {
                ExecutionKey = $"run-{index}",
                Outcome = index % 2,
                Values = new Dictionary<string, double> { ["control-parameter:x"] = index },
                Context = new Dictionary<string, string> { ["equipment_id"] = "PRESS-A" },
                OccurredAt = index
            }).ToArray(),
            Seed = 17
        });

        Assert.Equal("adaptive-context-diagnosis-v1", response.AlgorithmVersion);
    }

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

    private sealed class DiagnosisContractHandler(string fixture) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1/diagnosis", request.RequestUri!.AbsolutePath);
            var actual = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(fixture), JsonNode.Parse(actual)),
                $"诊断请求与 Python 共享契约不一致：{actual}");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"algorithm_version":"adaptive-context-diagnosis-v1",
                     "model_family":"robust-screening-only","adjustment_method":"none"}
                    """, Encoding.UTF8, "application/json")
            };
        }
    }

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
