using Ingot.Edge.ConnectorHost.Acquisition;
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
}
