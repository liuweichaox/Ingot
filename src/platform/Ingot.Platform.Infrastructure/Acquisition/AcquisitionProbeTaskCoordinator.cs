using System.Collections.Concurrent;
using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Infrastructure.Acquisition;

/// <summary>
///     Coordinates short-lived probe requests without requiring Platform to open a connection into OT.
///     Probe tasks are deliberately ephemeral: the caller is waiting for the result, and a Platform restart
///     terminates that request instead of replaying an obsolete device probe.
/// </summary>
public sealed class AcquisitionProbeTaskCoordinator
{
    private readonly ConcurrentDictionary<string, PendingProbe> _pending = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _queues = new(StringComparer.Ordinal);

    public async Task<AcquisitionProbeResult> QueueAndWaitAsync(
        AcquisitionDeployment deployment,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var now = DateTimeOffset.UtcNow;
        var task = new AcquisitionProbeTask
        {
            TaskId = Guid.CreateVersion7().ToString(),
            EdgeId = deployment.Profile.EdgeId,
            Deployment = deployment,
            CreatedAt = now,
            ExpiresAt = now.Add(timeout)
        };
        var pending = new PendingProbe(task);
        if (!_pending.TryAdd(task.TaskId, pending))
            throw new InvalidOperationException("Failed to allocate a unique acquisition probe task.");
        _queues.GetOrAdd(task.EdgeId, static _ => new ConcurrentQueue<string>()).Enqueue(task.TaskId);

        try
        {
            return await pending.Completion.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(task.TaskId, out _);
        }
    }

    public AcquisitionProbeTask? ClaimNext(string edgeId)
    {
        if (string.IsNullOrWhiteSpace(edgeId) || !_queues.TryGetValue(edgeId, out var queue))
            return null;
        while (queue.TryDequeue(out var taskId))
        {
            if (!_pending.TryGetValue(taskId, out var pending))
                continue;
            if (pending.Task.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                pending.Completion.TrySetException(new TimeoutException("The acquisition probe task expired."));
                _pending.TryRemove(taskId, out _);
                continue;
            }
            if (Interlocked.CompareExchange(ref pending.Claimed, 1, 0) == 0)
                return pending.Task;
        }
        return null;
    }

    public bool Complete(AcquisitionProbeTaskCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (!_pending.TryGetValue(completion.TaskId, out var pending) ||
            !string.Equals(pending.Task.EdgeId, completion.EdgeId, StringComparison.Ordinal))
            return false;
        return pending.Completion.TrySetResult(completion.Result);
    }

    private sealed class PendingProbe(AcquisitionProbeTask task)
    {
        public AcquisitionProbeTask Task { get; } = task;
        public TaskCompletionSource<AcquisitionProbeResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Claimed;
    }
}
