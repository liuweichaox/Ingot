using Ingot.Contracts.Insight;
using Ingot.Platform.Infrastructure.Insight;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class CaseLevelGradingTests
{
    private static readonly CaseLevelThresholds Thresholds = new();

    private static CaseLevelMetrics Healthy() => new()
    {
        ScopeEvents = 1000,
        Cycles = 100,
        PairedCycles = 100,
        MissingContext = 0,
        FutureTimestamps = 0,
        UnitConflicts = 0,
        ScopeCycles = 100,
        FeaturedCycles = 100,
        AverageCoverage = 0.95,
        ComparableReadyCycles = 40
    };

    [Fact]
    public void NoData_StaysL0Pending()
    {
        var (level, gates) = CaseLevelGrading.Determine(new CaseLevelMetrics(), Thresholds, featureSetRatified: true);
        Assert.Equal(CaseLevels.L0Pending, level);
        Assert.Single(gates);
        Assert.False(gates[0].Passed);
    }

    [Fact]
    public void PairingBelowThreshold_StaysL0Pending()
    {
        var metrics = Healthy() with { PairedCycles = 92, Cycles = 100 }; // 0.92 < 0.95
        var (level, gates) = CaseLevelGrading.Determine(metrics, Thresholds, featureSetRatified: true);
        Assert.Equal(CaseLevels.L0Pending, level);
        var pairing = Assert.Single(gates, g => g.Name == "周期配对率");
        Assert.False(pairing.Passed);
        // 未评定 L1/L2（短路）
        Assert.DoesNotContain(gates, g => g.Tier == CaseLevels.L1);
    }

    [Fact]
    public void ContextMissingTooHigh_StaysL0Pending()
    {
        var metrics = Healthy() with { MissingContext = 100, ScopeEvents = 1000 }; // 0.10 >= 0.05
        var (level, _) = CaseLevelGrading.Determine(metrics, Thresholds, featureSetRatified: true);
        Assert.Equal(CaseLevels.L0Pending, level);
    }

    [Fact]
    public void FutureTimestampOrUnitConflict_StaysL0Pending()
    {
        Assert.Equal(CaseLevels.L0Pending,
            CaseLevelGrading.Determine(Healthy() with { FutureTimestamps = 1 }, Thresholds, true).Level);
        Assert.Equal(CaseLevels.L0Pending,
            CaseLevelGrading.Determine(Healthy() with { UnitConflicts = 1 }, Thresholds, true).Level);
    }

    [Fact]
    public void L0Passes_ButLowCoverage_StopsAtL0()
    {
        var metrics = Healthy() with { AverageCoverage = 0.80 }; // < 0.90
        var (level, gates) = CaseLevelGrading.Determine(metrics, Thresholds, featureSetRatified: true);
        Assert.Equal(CaseLevels.L0, level);
        Assert.Contains(gates, g => g.Tier == CaseLevels.L1 && !g.Passed);
        Assert.DoesNotContain(gates, g => g.Tier == CaseLevels.L2);
    }

    [Fact]
    public void L0L1Pass_ButNotRatified_StopsAtL1()
    {
        var (level, gates) = CaseLevelGrading.Determine(Healthy(), Thresholds, featureSetRatified: false);
        Assert.Equal(CaseLevels.L1, level);
        var ratify = Assert.Single(gates, g => g.Name == "特征集已核定");
        Assert.False(ratify.Passed);
    }

    [Fact]
    public void L0L1Pass_ButTooFewComparable_StopsAtL1()
    {
        var metrics = Healthy() with { ComparableReadyCycles = 10 }; // < 30
        var (level, _) = CaseLevelGrading.Determine(metrics, Thresholds, featureSetRatified: true);
        Assert.Equal(CaseLevels.L1, level);
    }

    [Fact]
    public void AllGatesPass_ReachesL2()
    {
        var (level, gates) = CaseLevelGrading.Determine(Healthy(), Thresholds, featureSetRatified: true);
        Assert.Equal(CaseLevels.L2, level);
        Assert.All(gates, g => Assert.True(g.Passed));
    }

    [Fact]
    public void Downgrade_IsAutomatic_WhenPairingRegresses()
    {
        // 曾达 L2 的档案，配对率回落 → 自动回到 L0-pending
        var regressed = Healthy() with { PairedCycles = 80, Cycles = 100 };
        var (level, _) = CaseLevelGrading.Determine(regressed, Thresholds, featureSetRatified: true);
        Assert.Equal(CaseLevels.L0Pending, level);
    }
}
