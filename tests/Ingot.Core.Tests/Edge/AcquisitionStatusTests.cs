using Ingot.Edge.ConnectorHost.Acquisition;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class AcquisitionStatusTests
{
    [Fact]
    public void Get_ShouldReportEachConfigurationIndependently()
    {
        var status = new AcquisitionStatus();
        status.SetEnabled(true);
        status.RegisterTask("furnace-a@1");
        status.RegisterTask("furnace-b@2");

        var now = DateTimeOffset.UtcNow;
        status.RecordAttempt("furnace-a@1", now);
        status.RecordReadSuccess("furnace-a@1", now, 12);
        status.RecordValidSnapshot("furnace-a@1", now, "processSpecification-a@1", now);
        status.RecordEmissionOutcome("furnace-a@1", 2, inactive: false);
        status.RecordFailure("furnace-b@2", "connection refused");

        var snapshot = status.Get();

        Assert.Equal("degraded", snapshot.State);
        Assert.Equal(2, snapshot.Tasks.Count);
        Assert.Equal("running", snapshot.Tasks.Single(item => item.ConfigurationKey == "furnace-a@1").State);
        Assert.Equal(1, snapshot.ReadSuccessCount);
        Assert.Equal(1, snapshot.ValidSnapshotCount);
        Assert.Equal(2, snapshot.EmittedEventCount);
        Assert.Equal("connection refused", snapshot.Tasks.Single(item => item.ConfigurationKey == "furnace-b@2").LastError);
    }

    [Fact]
    public void RemoveTask_ShouldRemoveDeploymentAcknowledgement()
    {
        var status = new AcquisitionStatus();
        status.SetEnabled(true);
        status.RegisterTask("furnace-a@1");

        status.RemoveTask("furnace-a@1");

        var snapshot = status.Get();
        Assert.Empty(snapshot.Tasks);
        Assert.Equal("starting", snapshot.State);
    }

    [Fact]
    public void ConfigurationError_ShouldBeVisibleWhenNoDeploymentCanRun()
    {
        var status = new AcquisitionStatus();
        status.SetEnabled(true);

        status.SetConfigurationError("没有已发布配置。");

        var snapshot = status.Get();
        Assert.Equal("degraded", snapshot.State);
        Assert.Equal("没有已发布配置。", snapshot.LastError);
        Assert.Empty(snapshot.Tasks);
    }

    [Fact]
    public void DesiredAndAppliedVersions_ShouldConvergeOnlyAfterSuccessfulSample()
    {
        var status = new AcquisitionStatus();
        var deployment = Deployment(version: 2);
        status.SetEnabled(true);
        status.SetDesiredDeployments([deployment], AcquisitionConfigurationSources.Platform);
        status.RegisterTask("furnace-a@2", deployment);

        var pending = Assert.Single(status.Get().Deployments);
        Assert.Equal(AcquisitionApplicationStates.Pending, pending.State);
        Assert.Null(pending.AppliedVersion);
        Assert.False(status.AreDesiredDeploymentsApplied());

        var appliedAt = DateTimeOffset.UtcNow;
        status.RecordValidSnapshot("furnace-a@2", appliedAt, "processSpecification-a@1");

        var applied = Assert.Single(status.Get().Deployments);
        Assert.Equal(AcquisitionApplicationStates.Applied, applied.State);
        Assert.Equal(2, applied.AppliedVersion);
        Assert.Equal(applied.DesiredConfigurationHash, applied.AppliedConfigurationHash);
        Assert.Equal(appliedAt, applied.AppliedAt);
        Assert.True(status.AreDesiredDeploymentsApplied());
    }

    [Fact]
    public void FailedNewVersion_ShouldKeepPreviousVersionAsRollbackState()
    {
        var status = new AcquisitionStatus();
        var first = Deployment(version: 1);
        var second = Deployment(version: 2);
        status.SetEnabled(true);
        status.SetDesiredDeployments([first], AcquisitionConfigurationSources.Platform);
        status.RegisterTask("furnace-a@1", first);
        status.RecordValidSnapshot("furnace-a@1", DateTimeOffset.UtcNow, null);

        status.SetDesiredDeployments([second], AcquisitionConfigurationSources.Platform);
        status.RecordApplicationState(
            "furnace-a",
            AcquisitionApplicationStates.Rollback,
            "probe failed");

        var snapshot = Assert.Single(status.Get().Deployments);
        Assert.Equal(2, snapshot.DesiredVersion);
        Assert.Equal(1, snapshot.AppliedVersion);
        Assert.Equal(AcquisitionApplicationStates.Rollback, snapshot.State);
        Assert.Equal("probe failed", snapshot.LastError);
        Assert.False(status.AreDesiredDeploymentsApplied());
    }

    [Fact]
    public void ActiveProcessExecution_ShouldBlockConfigurationReplacement()
    {
        var status = new AcquisitionStatus();
        var deployment = Deployment(version: 1);
        status.SetDesiredDeployments([deployment], AcquisitionConfigurationSources.Platform);
        status.RegisterTask("furnace-a@1", deployment);

        status.RecordProcessExecutionState("furnace-a@1", true);
        Assert.False(status.IsSafeToReplace("furnace-a@1"));

        status.RecordProcessExecutionState("furnace-a@1", false);
        Assert.True(status.IsSafeToReplace("furnace-a@1"));
    }

    [Fact]
    public void StaleSnapshotRejections_ShouldRemainVisiblePerTaskAndInAggregate()
    {
        var status = new AcquisitionStatus();
        status.SetEnabled(true);
        status.RegisterTask("furnace-a@1");

        status.RecordStaleSnapshotRejection("furnace-a@1", 2, "snapshot stale");
        status.RecordStaleSnapshotRejection("furnace-a@1", 1, "snapshot stale");

        var snapshot = status.Get();
        var task = Assert.Single(snapshot.Tasks);
        Assert.Equal("degraded", task.State);
        Assert.Equal(2, task.StaleSnapshotRejectionCount);
        Assert.Equal(3, task.StaleValueRejectionCount);
        Assert.Equal(2, snapshot.StaleSnapshotRejectionCount);
        Assert.Equal(3, snapshot.StaleValueRejectionCount);
    }

    [Fact]
    public async Task ReadSuccessWithoutValidSnapshot_ShouldNotPassStartupHealth()
    {
        var status = new AcquisitionStatus();
        status.RegisterTask("furnace-a@1");
        status.RecordReadSuccess("furnace-a@1", DateTimeOffset.UtcNow);

        Assert.False(await status.WaitForFirstSuccessAsync(
            "furnace-a@1",
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None));
        Assert.Null(Assert.Single(status.Get().Tasks).LastValidSnapshotAt);
    }

    [Fact]
    public void StalledSourceIdentity_ShouldDegradeWithoutAdvancingValidSnapshot()
    {
        var status = new AcquisitionStatus();
        status.SetEnabled(true);
        status.RegisterTask("furnace-a@1");
        status.RecordValidSnapshot("furnace-a@1", DateTimeOffset.UtcNow.AddMinutes(-2), null);

        status.RecordDuplicateSnapshot("furnace-a@1", stalled: true, "source clock stalled");

        var task = Assert.Single(status.Get().Tasks);
        Assert.Equal("degraded", task.State);
        Assert.Equal(1, task.DuplicateSuppressionCount);
        Assert.Equal(1, task.SourceIdentityStallCount);
        Assert.Equal("source clock stalled", task.LastError);
    }

    private static AcquisitionDeployment Deployment(int version)
        => new()
        {
            Task = new IngestionTask
            {
                TaskId = "furnace-a",
                Version = version,
                Name = "Furnace A",
                Status = ConfigurationStatuses.Published,
                EdgeId = "EDGE-001",
                DataModelId = "model-a",
                Source = "connector/http/furnace-a",
                SubjectId = "FURNACE-01"
            },
            DataModel = new ProcessDataModel
            {
                ModelId = "model-a",
                Name = "Model A",
                Status = ConfigurationStatuses.Published
            }
        };
}
