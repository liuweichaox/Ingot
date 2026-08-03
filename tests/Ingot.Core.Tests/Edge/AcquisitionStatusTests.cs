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
        status.RecordSuccess("furnace-a@1", now, "recipe-a@1");
        status.RecordFailure("furnace-b@2", "connection refused");

        var snapshot = status.Get();

        Assert.Equal("degraded", snapshot.State);
        Assert.Equal(2, snapshot.Tasks.Count);
        Assert.Equal("running", snapshot.Tasks.Single(item => item.ConfigurationKey == "furnace-a@1").State);
        Assert.Equal(1, snapshot.SamplesCollected);
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
        status.RecordSuccess("furnace-a@2", appliedAt, "recipe-a@1");

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
        status.RecordSuccess("furnace-a@1", DateTimeOffset.UtcNow, null);

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
    public void ActiveCycle_ShouldBlockConfigurationReplacement()
    {
        var status = new AcquisitionStatus();
        var deployment = Deployment(version: 1);
        status.SetDesiredDeployments([deployment], AcquisitionConfigurationSources.Platform);
        status.RegisterTask("furnace-a@1", deployment);

        status.RecordCycleState("furnace-a@1", true);
        Assert.False(status.IsSafeToReplace("furnace-a@1"));

        status.RecordCycleState("furnace-a@1", false);
        Assert.True(status.IsSafeToReplace("furnace-a@1"));
    }

    private static AcquisitionDeployment Deployment(int version)
        => new()
        {
            Profile = new AcquisitionProfile
            {
                ProfileId = "furnace-a",
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
