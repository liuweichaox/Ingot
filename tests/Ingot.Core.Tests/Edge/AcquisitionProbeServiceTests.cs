// 验证边缘组件 AcquisitionProbeService 的协议、状态和失败边界。

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
            new SourceDiscoveryQuery(),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Contains(result.Points, item =>
            item.Path == "signals.temperature" && item.RawValue == "125");
        var preview = Assert.Single(result.Mappings);
        Assert.Equal("10.5", preview.ConvertedValue);
        Assert.Equal("°C", preview.TargetUnit);
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
            new SourceDiscoveryQuery(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(Assert.Single(result.Mappings).Found);
    }

    [Fact]
    public async Task HttpProbeBlocksPublishingWhenOptionalMappingWasNeverObserved()
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
                    Required = false
                }),
            new SourceDiscoveryQuery(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.MappingsValidated);
        Assert.Single(result.Warnings);
        Assert.Contains("missing", result.Warnings[0]);
    }

    [Fact]
    public async Task HttpProbeRequiresAnExplicitlyConfiguredSequencePathToExist()
    {
        var service = new AcquisitionProbeService(
            new SingleClientFactory(new HttpClient(new JsonHandler("""{"value":1}"""))),
            new NoSecrets());
        var deployment = Deployment(new AcquisitionValueMapping
        {
            DataItemCode = "mold.temperature",
            SourcePath = "value"
        });
        deployment = deployment with { Task = deployment.Task with { SequencePath = "sequence" } };

        var result = await service.ProbeAsync(
            deployment,
            new SourceDiscoveryQuery(),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains(result.Warnings, warning => warning.Contains("sequence", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DiscoverySupportsServerSideFilteringAndStableCursorPaging()
    {
        var service = new AcquisitionProbeService(
            new SingleClientFactory(new HttpClient(new JsonHandler(
                """{"signals":{"pressure":2,"temperature":1,"vacuum":3}}"""))),
            new NoSecrets());
        var deployment = Deployment(new AcquisitionValueMapping
        {
            DataItemCode = "mold.temperature",
            SourcePath = "signals.temperature"
        });

        var first = await service.ProbeAsync(
            deployment,
            new SourceDiscoveryQuery { RootPath = "signals", PageSize = 1 },
            CancellationToken.None);
        var second = await service.ProbeAsync(
            deployment,
            new SourceDiscoveryQuery
            {
                RootPath = "signals",
                PageSize = 1,
                Cursor = first.NextCursor
            },
            CancellationToken.None);

        Assert.Single(first.Points);
        Assert.NotNull(first.NextCursor);
        Assert.Single(second.Points);
        Assert.NotEqual(first.Points[0].Path, second.Points[0].Path);
    }

    [Fact]
    public async Task DiscoveryRejectsInvalidCursor()
    {
        var service = new AcquisitionProbeService(
            new SingleClientFactory(new HttpClient(new JsonHandler("""{"value":1}"""))),
            new NoSecrets());

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ProbeAsync(
            Deployment(new AcquisitionValueMapping
            {
                DataItemCode = "mold.temperature",
                SourcePath = "value"
            }),
            new SourceDiscoveryQuery { Cursor = "%%%" },
            CancellationToken.None));
    }

    [Fact]
    public async Task HttpProbeUsesConfiguredMethodBodyAndSecretHeaders()
    {
        var handler = new CapturingHandler();
        var service = new AcquisitionProbeService(
            new SingleClientFactory(new HttpClient(handler)),
            new FixedSecrets("token-secret", "Bearer test-token"));
        var deployment = Deployment(new AcquisitionValueMapping
        {
            DataItemCode = "mold.temperature",
            SourcePath = "value"
        });
        deployment = deployment with
        {
            Task = deployment.Task with
            {
                HttpPolling = deployment.Task.HttpPolling with
                {
                    Method = "post",
                    ContentType = "application/json",
                    RequestBody = "{\"read\":true}",
                    HeaderSecretRefs = new Dictionary<string, string>
                    {
                        ["Authorization"] = "token-secret"
                    }
                }
            }
        };

        var result = await service.ProbeAsync(deployment, new SourceDiscoveryQuery(), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("Bearer test-token", handler.Authorization);
        Assert.Equal("{\"read\":true}", handler.Body);
    }

    private static AcquisitionDeployment Deployment(AcquisitionValueMapping mapping)
        => new()
        {
            Task = new IngestionTask
            {
                TaskId = "probe",
                Name = "Probe",
                EdgeId = "edge",
                DataModelId = "model",
                Source = "connector/http/device",
                SubjectId = "device",
                Protocol = AcquisitionProtocols.HttpPolling,
                TimestampMode = "edge-received",
                HttpPolling = new HttpPollingConnection
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

    private sealed class FixedSecrets(string reference, string value) : IAcquisitionSecretResolver
    {
        public string? Resolve(string? requested) => requested == reference ? value : null;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Authorization = request.Headers.GetValues("Authorization").Single();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"value\":1}", Encoding.UTF8, "application/json")
            };
        }
    }
}
