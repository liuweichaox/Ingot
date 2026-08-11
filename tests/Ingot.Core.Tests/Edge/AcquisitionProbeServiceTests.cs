using System.Net;
using System.Text;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionProbeServiceTests
{
    [Fact]
    public async Task HttpProbeDiscoversFieldsAndPreviewsScaledValue()
    {
        var client = new HttpClient(new JsonHandler(
            """{"signals":{"temperature":125},"stageNumber":3}"""));
        var service = new AcquisitionProbeService(
            new SingleClientFactory(client),
            new NoSecrets());
        var result = await service.ProbeAsync(
            Deployment(
                new AcquisitionValueMapping
                {
                    DataItemCode = "mold.temperature",
                    SourcePath = "signals.temperature",
                    Scale = 0.1,
                    Offset = -2,
                    Required = true
                }),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Points, item =>
            item.Path == "signals.temperature" && item.RawValue == "125");
        var preview = Assert.Single(result.Mappings);
        Assert.Equal("10.5", preview.ConvertedValue);
        Assert.Equal("°C", preview.Unit);
    }

    [Fact]
    public async Task HttpProbeRejectsMissingRequiredMapping()
    {
        var service = new AcquisitionProbeService(
            new SingleClientFactory(new HttpClient(new JsonHandler("""{"value":1}"""))),
            new NoSecrets());
        var result = await service.ProbeAsync(
            Deployment(
                new AcquisitionValueMapping
                {
                    DataItemCode = "mold.temperature",
                    SourcePath = "missing",
                    Required = true
                }),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(Assert.Single(result.Mappings).Found);
    }

    private static AcquisitionDeployment Deployment(AcquisitionValueMapping mapping)
        => new()
        {
            Profile = new AcquisitionProfile
            {
                ProfileId = "probe",
                Name = "Probe",
                EdgeId = "edge",
                DataModelId = "model",
                Source = "connector/http/device",
                SubjectId = "device",
                Protocol = AcquisitionProtocols.HttpPolling,
                TimestampMode = "edge-received",
                Connection = new HttpPollingConnection
                {
                    BaseUrl = "http://device.local",
                    SnapshotPath = "/snapshot"
                },
                Execution = new AcquisitionExecutionOptions { TimeoutMs = 1000 },
                ValueMappings = [mapping]
            },
            DataModel = new ProcessDataModel
            {
                ModelId = "model",
                Name = "Model",
                Acquisition = new AcquisitionModel
                {
                    DataItems =
                    [
                        new ProcessDataItemDefinition
                        {
                            Code = "mold.temperature",
                            DisplayName = "模具温度",
                            Unit = "°C"
                        }
                    ]
                }
            }
        };

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class NoSecrets : IAcquisitionSecretResolver
    {
        public string? Resolve(string? reference) => null;
    }
}
