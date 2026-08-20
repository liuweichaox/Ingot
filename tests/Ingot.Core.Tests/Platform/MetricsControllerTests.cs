// 验证平台组件 MetricsController 的成功、拒绝和安全边界。

using System.Net;
using Ingot.Platform.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MetricsControllerTests
{
    [Fact]
    public async Task GetMetricsJson_ShouldScrapeTheLocalListenerInsteadOfTheForwardedHost()
    {
        var handler = new RecordingHandler();
        var controller = new MetricsController(new SingleClientFactory(new HttpClient(handler)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Request =
                    {
                        Scheme = "http",
                        Host = new HostString("external.example", 443)
                    },
                    Connection = { LocalPort = 8000 }
                }
            }
        };

        var result = await controller.GetMetricsJson();

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("http://127.0.0.1:8000/metrics", handler.RequestUri?.AbsoluteUri);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("# TYPE event_ingest_total counter\nevent_ingest_total 1\n")
            });
        }
    }
}
