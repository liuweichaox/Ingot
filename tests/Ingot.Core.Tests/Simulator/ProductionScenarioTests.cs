using Ingot.Simulator;
using Xunit;

namespace Ingot.Core.Tests.Simulator;

public sealed class ProductionScenarioTests
{
    [Fact]
    public void FirstIteration_ShouldFollowRfcEventTimeline()
    {
        var baseline = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(1));
        var lotChanged = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(3));
        var toolingChanged = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(5));
        var cycle1 = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(7));
        var cycle2 = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(11));
        var cycle3 = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(15));
        var alarm = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(19));
        var recovered = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(21));

        Assert.Equal(string.Empty, baseline.MaterialLot);
        Assert.Equal("LOT-001", lotChanged.MaterialLot);
        Assert.Equal("TOOL-A", toolingChanged.Tooling);
        Assert.Equal(1, cycle1.CycleNumber);
        Assert.Equal(2, cycle2.CycleNumber);
        Assert.Equal(3, cycle3.CycleNumber);
        Assert.All([cycle1, cycle2, cycle3], snapshot => Assert.True(snapshot.IsProducing));
        Assert.True(alarm.AlarmActive);
        Assert.False(recovered.AlarmActive);
    }

    [Fact]
    public void NextIteration_ShouldPreserveOldContextUntilChangeSteps()
    {
        var beforeLotChange = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(23));
        var afterLotChange = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(25));
        var afterToolingChange = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(27));

        Assert.Equal(2, beforeLotChange.Iteration);
        Assert.Equal("LOT-001", beforeLotChange.MaterialLot);
        Assert.Equal("TOOL-A", beforeLotChange.Tooling);
        Assert.Equal("LOT-002", afterLotChange.MaterialLot);
        Assert.Equal("TOOL-A", afterLotChange.Tooling);
        Assert.Equal("TOOL-B", afterToolingChange.Tooling);
    }

    [Fact]
    public void SpeedMultiplier_ShouldScaleTimeline()
    {
        var normal = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(7));
        var accelerated = ProductionScenario.GetSnapshot(TimeSpan.FromSeconds(1.75), speed: 4);

        Assert.Equal(normal, accelerated);
    }
}
