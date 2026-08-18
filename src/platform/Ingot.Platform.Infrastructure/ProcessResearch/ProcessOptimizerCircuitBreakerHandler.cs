using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

/// <summary>
///     Fast-fails after consecutive transport/5xx failures. It intentionally does not retry:
///     optimizer POST requests can be expensive and are not assumed idempotent.
/// </summary>
public sealed class ProcessOptimizerCircuitBreakerHandler(
    IOptions<ProcessOptimizerOptions> options) : DelegatingHandler
{
    private readonly int _failureThreshold = Math.Clamp(
        options.Value.CircuitFailureThreshold, 1, 20);
    private readonly TimeSpan _breakDuration = TimeSpan.FromSeconds(Math.Clamp(
        options.Value.CircuitBreakSeconds, 1, 300));
    private int _consecutiveFailures;
    private long _blockedUntilUtcTicks;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow.UtcTicks < Interlocked.Read(ref _blockedUntilUtcTicks))
            throw new ProcessOptimizerUnavailableException(
                "优化服务熔断器已打开，请稍后重试。");

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if ((int)response.StatusCode >= 500)
                RecordFailure();
            else
                Reset();
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            RecordFailure();
            throw;
        }
    }

    private void RecordFailure()
    {
        if (Interlocked.Increment(ref _consecutiveFailures) < _failureThreshold)
            return;
        Interlocked.Exchange(
            ref _blockedUntilUtcTicks,
            DateTimeOffset.UtcNow.Add(_breakDuration).UtcTicks);
    }

    private void Reset()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _blockedUntilUtcTicks, 0);
    }
}
