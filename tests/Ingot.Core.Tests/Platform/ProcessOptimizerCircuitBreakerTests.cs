// 验证平台组件 ProcessOptimizerCircuitBreaker 的成功、拒绝和安全边界。

using System.Net;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessOptimizerCircuitBreakerTests
{
    [Fact]
    public async Task ConsecutiveServerFailures_ShouldOpenWithoutRetryingRequests()
    {
        var inner = new ServerFailureHandler();
        var breaker = new ProcessOptimizerCircuitBreakerHandler(
            Options.Create(new ProcessOptimizerOptions
            {
                CircuitFailureThreshold = 3,
                CircuitBreakSeconds = 30
            }))
        {
            InnerHandler = inner
        };
        using var client = new HttpClient(breaker);

        for (var index = 0; index < 3; index++)
            using (await client.GetAsync("http://optimizer.test/v1/suggestions")) { }

        await Assert.ThrowsAsync<ProcessOptimizerUnavailableException>(() =>
            client.GetAsync("http://optimizer.test/v1/suggestions"));
        Assert.Equal(3, inner.CallCount);
    }

    private sealed class ServerFailureHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }
    }
}
