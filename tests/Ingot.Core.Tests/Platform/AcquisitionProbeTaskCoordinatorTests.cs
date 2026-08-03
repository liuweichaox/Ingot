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
        var coordinator = new AcquisitionProbeTaskCoordinator();
        var waiting = coordinator.QueueAndWaitAsync(
            Deployment("EDGE-001"),
            TimeSpan.FromSeconds(5));

        Assert.Null(coordinator.ClaimNext("EDGE-002"));
        var task = coordinator.ClaimNext("EDGE-001");
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
        Assert.True(coordinator.Complete(new AcquisitionProbeTaskCompletion
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
        var coordinator = new AcquisitionProbeTaskCoordinator();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waiting = coordinator.QueueAndWaitAsync(
            Deployment("EDGE-001"),
            TimeSpan.FromSeconds(5),
            cancellation.Token);
        var task = coordinator.ClaimNext("EDGE-001")!;
        var result = new AcquisitionProbeResult
        {
            Success = false,
            MappingsValidated = false,
            Protocol = AcquisitionProtocols.HttpPolling,
            Message = "failed",
            TestedAt = DateTimeOffset.UtcNow
        };

        Assert.False(coordinator.Complete(new AcquisitionProbeTaskCompletion
        {
            TaskId = task.TaskId,
            EdgeId = "EDGE-002",
            Result = result
        }));
        Assert.True(coordinator.Complete(new AcquisitionProbeTaskCompletion
        {
            TaskId = task.TaskId,
            EdgeId = "EDGE-001",
            Result = result
        }));
        Assert.False((await waiting).Success);
    }

    private static AcquisitionDeployment Deployment(string edgeId)
        => new()
        {
            Profile = new AcquisitionProfile
            {
                ProfileId = "profile-a",
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
}
