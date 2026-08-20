using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Application.Acquisition;

/// <summary>协调设备探查任务的领取、进度、完成和失败状态。</summary>
public interface IAcquisitionProbeTaskStore
{
    Task EnqueueAsync(AcquisitionProbeTask task, CancellationToken ct = default);
    Task<AcquisitionProbeTask?> ClaimNextAsync(string edgeId, CancellationToken ct = default);
    Task<bool> CompleteAsync(AcquisitionProbeTaskCompletion completion, CancellationToken ct = default);
    Task<AcquisitionProbeResult?> GetResultAsync(string taskId, CancellationToken ct = default);
    Task DeleteAsync(string taskId, CancellationToken ct = default);
}

/// <summary>
///     Coordinates read-only device probes through a persistent store. The waiting HTTP request may be served by
///     a different API replica from Edge polling and completion; PostgreSQL is the coordination authority.
/// </summary>
public sealed class AcquisitionProbeTaskCoordinator(IAcquisitionProbeTaskStore store)
{
    private static readonly TimeSpan ResultPollInterval = TimeSpan.FromMilliseconds(100);

    public async Task<AcquisitionProbeResult> QueueAndWaitAsync(
        AcquisitionDeployment deployment,
        TimeSpan timeout,
        SourceDiscoveryQuery? discovery = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var now = DateTimeOffset.UtcNow;
        var task = new AcquisitionProbeTask
        {
            TaskId = Guid.CreateVersion7().ToString(),
            EdgeId = deployment.Task.EdgeId,
            Deployment = deployment,
            Discovery = discovery ?? new SourceDiscoveryQuery(),
            CreatedAt = now,
            ExpiresAt = now.Add(timeout)
        };
        await store.EnqueueAsync(task, ct).ConfigureAwait(false);

        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutSource.Token);
        try
        {
            while (true)
            {
                var result = await store.GetResultAsync(task.TaskId, linked.Token).ConfigureAwait(false);
                if (result is not null)
                    return result;
                await Task.Delay(ResultPollInterval, linked.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            throw new TimeoutException("The acquisition probe task expired.");
        }
        finally
        {
            await store.DeleteAsync(task.TaskId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public Task<AcquisitionProbeTask?> ClaimNextAsync(string edgeId, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(edgeId)
            ? Task.FromResult<AcquisitionProbeTask?>(null)
            : store.ClaimNextAsync(edgeId.Trim(), ct);

    public Task<bool> CompleteAsync(AcquisitionProbeTaskCompletion completion, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(completion);
        return completion.Result is null
            ? Task.FromResult(false)
            : store.CompleteAsync(completion, ct);
    }
}
