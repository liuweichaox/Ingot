using Ingot.Contracts.Insight;

namespace Ingot.Platform.Infrastructure.Insight;

/// <summary>定级门槛阈值。默认值来自系统设计 §6.4；可经配置节 CaseLeveling 覆盖，门本身不可拆。</summary>
public sealed class CaseLevelThresholds
{
    public int WindowDays { get; set; } = 14;

    // L0 生产记录可用
    public double MinPairingRate { get; set; } = 0.95;
    public double MaxContextMissingRate { get; set; } = 0.05;

    // L1 生产过程看得清
    public int MinFeaturedCycles { get; set; } = 1;
    public double MinPhaseCoverage { get; set; } = 0.90;

    // L2 参数关系找得到
    public int MinComparableCycles { get; set; } = 30;
}

/// <summary>定级探针的原始度量（由 CaseLevelEvaluator 从数据库读取）。</summary>
public sealed record CaseLevelMetrics
{
    public long ScopeEvents { get; init; }
    public long Cycles { get; init; }
    public long PairedCycles { get; init; }
    public long MissingContext { get; init; }
    public long FutureTimestamps { get; init; }
    public long UnitConflicts { get; init; }
    public long ScopeCycles { get; init; }
    public long FeaturedCycles { get; init; }
    public double? AverageCoverage { get; init; }
    public long ComparableReadyCycles { get; init; }

    public double PairingRate => Cycles == 0 ? 0d : (double)PairedCycles / Cycles;
    public double ContextMissingRate => ScopeEvents == 0 ? 1d : (double)MissingContext / ScopeEvents;
}

/// <summary>
///     纯函数定级：给定度量、阈值、人工核定标志 → 达到的最高等级 + 逐门槛证据。
///     证据不足自动停在低等级（诚实降级）。与数据库无关，可完整单元测试。
/// </summary>
public static class CaseLevelGrading
{
    public static (string Level, IReadOnlyList<LevelGate> Gates) Determine(
        CaseLevelMetrics metrics,
        CaseLevelThresholds thresholds,
        bool featureSetRatified)
    {
        var gates = new List<LevelGate>();

        if (metrics.ScopeEvents == 0)
        {
            gates.Add(new LevelGate
            {
                Name = "生产记录存在",
                Tier = CaseLevels.L0,
                Measured = 0,
                Threshold = 1,
                Comparator = ">=",
                Passed = false,
                Detail = "范围内没有生产记录，无法评定任何等级。"
            });
            return (CaseLevels.L0Pending, gates);
        }

        // ---- L0 生产记录可用 ----
        gates.Add(Gate("周期配对率", CaseLevels.L0, metrics.PairingRate, thresholds.MinPairingRate, ">=",
            metrics.PairingRate >= thresholds.MinPairingRate,
            $"{metrics.PairedCycles}/{metrics.Cycles} 周期同时有开始与结束事件。"));
        gates.Add(Gate("生产信息缺失率", CaseLevels.L0, metrics.ContextMissingRate, thresholds.MaxContextMissingRate, "<",
            metrics.ContextMissingRate < thresholds.MaxContextMissingRate,
            $"{metrics.MissingContext}/{metrics.ScopeEvents} 条记录 context 为空。"));
        gates.Add(Gate("时钟异常记录", CaseLevels.L0, metrics.FutureTimestamps, 0, "==",
            metrics.FutureTimestamps == 0,
            $"{metrics.FutureTimestamps} 条记录时间戳晚于当前时间。"));
        gates.Add(Gate("单位冲突信号", CaseLevels.L0, metrics.UnitConflicts, 0, "==",
            metrics.UnitConflicts == 0,
            $"{metrics.UnitConflicts} 个信号在范围内出现多种单位。"));

        if (!AllPassed(gates, CaseLevels.L0))
            return (CaseLevels.L0Pending, gates);

        // ---- L1 生产过程看得清 ----
        gates.Add(Gate("已物化周期数", CaseLevels.L1, metrics.FeaturedCycles, thresholds.MinFeaturedCycles, ">=",
            metrics.FeaturedCycles >= thresholds.MinFeaturedCycles,
            $"{metrics.FeaturedCycles}/{metrics.ScopeCycles} 周期已产出阶段特征。"));
        var coverage = metrics.AverageCoverage ?? 0d;
        gates.Add(Gate("阶段归属覆盖", CaseLevels.L1, coverage, thresholds.MinPhaseCoverage, ">=",
            metrics.AverageCoverage is { } value && value >= thresholds.MinPhaseCoverage,
            metrics.AverageCoverage is null ? "尚无阶段特征可评估覆盖度。" : $"平均覆盖度 {coverage:P1}。"));

        if (!AllPassed(gates, CaseLevels.L1))
            return (CaseLevels.L0, gates);

        // ---- L2 参数关系找得到 ----
        gates.Add(Gate("同类可比周期数", CaseLevels.L2, metrics.ComparableReadyCycles, thresholds.MinComparableCycles, ">=",
            metrics.ComparableReadyCycles >= thresholds.MinComparableCycles,
            $"{metrics.ComparableReadyCycles} 个 ready 物化周期可用于同类比较。"));
        gates.Add(Gate("特征集已核定", CaseLevels.L2, featureSetRatified ? 1 : 0, 1, "==",
            featureSetRatified,
            featureSetRatified ? "工艺工程师已核定特征集。" : "需工艺工程师核定特征集后方可进入 L2。"));

        if (!AllPassed(gates, CaseLevels.L2))
            return (CaseLevels.L1, gates);

        return (CaseLevels.L2, gates);
    }

    private static bool AllPassed(IEnumerable<LevelGate> gates, string tier)
        => gates.Where(gate => gate.Tier == tier).All(static gate => gate.Passed);

    private static LevelGate Gate(
        string name, string tier, double measured, double threshold,
        string comparator, bool passed, string detail)
        => new()
        {
            Name = name,
            Tier = tier,
            Measured = measured,
            Threshold = threshold,
            Comparator = comparator,
            Passed = passed,
            Detail = detail
        };
}
