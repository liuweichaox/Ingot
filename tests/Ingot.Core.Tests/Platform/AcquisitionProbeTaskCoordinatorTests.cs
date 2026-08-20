// 验证平台组件 AcquisitionProbeTaskCoordinator 的成功、拒绝和安全边界。

using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class AcquisitionProbeTaskCoordinatorTests
{
    [Fact]
    public async Task Edge_ShouldClaimOnlyItsOwnTaskAndCompleteWaitingRequest()
    {
        var coordinator = new AcquisitionProbeTaskCoordinator(new MemoryProbeTaskStore());
        var waiting = coordinator.QueueAndWaitAsync(
            Deployment("EDGE-001"),
            TimeSpan.FromSeconds(5));

        Assert.Null(await coordinator.ClaimNextAsync("EDGE-002"));
        var task = await coordinator.ClaimNextAsync("EDGE-001");
        Assert.NotNull(task);
        Assert.Equal("EDGE-001", task.EdgeId);

        var result = new AcquisitionProbeResult
        {
            Success = true,
            MappingsValidated = true,
            Protocol = AcquisitionProtocols.HttpPolling,
            Message = "ok",
            TestedAt = DateTimeOffset.UtcNow
        };
        Assert.True(await coordinator.CompleteAsync(new AcquisitionProbeTaskCompletion
        {
            TaskId = task.TaskId,
            EdgeId = "EDGE-001",
            Result = result
        }));

        Assert.Same(result, await waiting);
    }

    [Fact]
    public async Task WrongEdge_ShouldNotCompleteClaimedTask()
    {
        var coordinator = new AcquisitionProbeTaskCoordinator(new MemoryProbeTaskStore());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = coordinator.QueueAndWaitAsync(
            Deployment("EDGE-001"),
            TimeSpan.FromSeconds(5),
            new SourceDiscoveryQuery(),
            cancellation.Token);
        var task = (await coordinator.ClaimNextAsync("EDGE-001"))!;
        var result = new AcquisitionProbeResult
        {
            Success = false,
            MappingsValidated = false,
            Protocol = AcquisitionProtocols.HttpPolling,
            Message = "failed",
            TestedAt = DateTimeOffset.UtcNow
        };

        Assert.False(await coordinator.CompleteAsync(new AcquisitionProbeTaskCompletion
        {
            TaskId = task.TaskId,
            EdgeId = "EDGE-002",
            Result = result
        }));
        Assert.True(await coordinator.CompleteAsync(new AcquisitionProbeTaskCompletion
        {
            TaskId = task.TaskId,
            EdgeId = "EDGE-001",
            Result = result
        }));
        Assert.False((await waiting).Success);
    }

    [Fact]
    public async Task CompletionMustBelongToAClaimedTaskAndTheExpectedProtocol()
    {
        var coordinator = new AcquisitionProbeTaskCoordinator(new MemoryProbeTaskStore());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = coordinator.QueueAndWaitAsync(
            Deployment("EDGE-001"),
            TimeSpan.FromSeconds(5),
            ct: cancellation.Token);
        var queued = (await coordinator.ClaimNextAsync("EDGE-001"))!;

        Assert.False(await coordinator.CompleteAsync(new AcquisitionProbeTaskCompletion
        {
            TaskId = queued.TaskId,
            EdgeId = queued.EdgeId,
            Result = new AcquisitionProbeResult
            {
                Success = true,
                MappingsValidated = true,
                Protocol = AcquisitionProtocols.Mqtt,
                Message = "wrong protocol",
                TestedAt = DateTimeOffset.UtcNow
            }
        }));

        var expected = new AcquisitionProbeResult
        {
            Success = true,
            MappingsValidated = true,
            Protocol = AcquisitionProtocols.HttpPolling,
            Message = "ok",
            TestedAt = DateTimeOffset.UtcNow
        };
        Assert.True(await coordinator.CompleteAsync(new AcquisitionProbeTaskCompletion
        {
            TaskId = queued.TaskId,
            EdgeId = queued.EdgeId,
            Result = expected
        }));
        Assert.Same(expected, await waiting);
    }

    private static AcquisitionDeployment Deployment(string edgeId)
        => new()
        {
            Task = new IngestionTask
            {
                TaskId = "profile-a",
                Name = "Profile A",
                Status = ConfigurationStatuses.Draft,
                EdgeId = edgeId,
                DataModelId = "model-a",
                Source = "connector/http/profile-a",
                SubjectId = "MACHINE-01"
            },
            DataModel = new ProcessDataModel
            {
                ModelId = "model-a",
                Name = "Model A",
                Status = ConfigurationStatuses.Published
            }
        };

    private sealed class MemoryProbeTaskStore : IAcquisitionProbeTaskStore
    {
        private readonly Dictionary<string, AcquisitionProbeTask> _tasks = new(StringComparer.Ordinal);
        private readonly Dictionary<string, AcquisitionProbeResult> _results = new(StringComparer.Ordinal);
        private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

        public Task EnqueueAsync(AcquisitionProbeTask task, CancellationToken ct = default)
        {
            _tasks.Add(task.TaskId, task);
            return Task.CompletedTask;
        }

        public Task<AcquisitionProbeTask?> ClaimNextAsync(string edgeId, CancellationToken ct = default)
        {
            var task = _tasks.Values.FirstOrDefault(item =>
                item.EdgeId == edgeId && item.ExpiresAt > DateTimeOffset.UtcNow && !_claimed.Contains(item.TaskId));
            if (task is not null) _claimed.Add(task.TaskId);
            return Task.FromResult(task);
        }

        public Task<bool> CompleteAsync(AcquisitionProbeTaskCompletion completion, CancellationToken ct = default)
        {
            var valid = _tasks.TryGetValue(completion.TaskId, out var task) &&
                        _claimed.Contains(completion.TaskId) &&
                        task.EdgeId == completion.EdgeId &&
                        task.Deployment.Task.Protocol == completion.Result.Protocol;
            if (valid) _results[completion.TaskId] = completion.Result;
            return Task.FromResult(valid);
        }

        public Task<AcquisitionProbeResult?> GetResultAsync(string taskId, CancellationToken ct = default)
            => Task.FromResult(_results.GetValueOrDefault(taskId));

        public Task DeleteAsync(string taskId, CancellationToken ct = default)
        {
            _tasks.Remove(taskId);
            _results.Remove(taskId);
            _claimed.Remove(taskId);
            return Task.CompletedTask;
        }
    }
}
