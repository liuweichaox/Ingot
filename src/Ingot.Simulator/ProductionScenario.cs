namespace Ingot.Simulator;

/// <summary>
///     RFC Phase 2 的确定性剧本：换批次 → 换工装 → 三个周期 → 报警并恢复。
///     每 22 秒进入下一批次，便于重复演示和端到端测试。
/// </summary>
public static class ProductionScenario
{
    public const double DurationSeconds = 22;

    public static ScenarioSnapshot GetSnapshot(TimeSpan elapsed, double speed = 1)
    {
        if (speed <= 0)
            throw new ArgumentOutOfRangeException(nameof(speed), "剧本时间倍率必须大于 0。");

        var totalSeconds = Math.Max(0, elapsed.TotalSeconds * speed);
        var iteration = (int)Math.Floor(totalSeconds / DurationSeconds);
        var local = totalSeconds % DurationSeconds;
        var currentLot = $"LOT-{iteration + 1:000}";
        var previousLot = iteration == 0 ? string.Empty : $"LOT-{iteration:000}";
        var currentTooling = $"TOOL-{(char)('A' + iteration % 26)}";
        var previousTooling = iteration == 0
            ? string.Empty
            : $"TOOL-{(char)('A' + (iteration - 1) % 26)}";

        var materialLot = local < 2 ? previousLot : currentLot;
        var tooling = local < 4 ? previousTooling : currentTooling;
        var cycleInIteration = local switch
        {
            >= 6 and < 8 => 1,
            >= 10 and < 12 => 2,
            >= 14 and < 16 => 3,
            _ => 0
        };
        var isProducing = cycleInIteration > 0;
        var alarmActive = local is >= 18 and < 20;
        var cycleNumber = cycleInIteration == 0 ? 0 : iteration * 3 + cycleInIteration;
        var completedCycleInIteration = local switch
        {
            >= 16 => 3,
            >= 12 => 2,
            >= 8 => 1,
            _ => 0
        };
        var completedCycleNumber = completedCycleInIteration == 0
            ? 0
            : iteration * 3 + completedCycleInIteration;

        var phase = local switch
        {
            < 2 => "等待换批",
            < 4 => "批次已切换",
            < 6 => "工装已切换",
            < 8 => "周期 1",
            < 10 => "周期 1 完成",
            < 12 => "周期 2",
            < 14 => "周期 2 完成",
            < 16 => "周期 3",
            < 18 => "周期 3 完成",
            < 20 => "主轴过载报警",
            _ => "报警已恢复"
        };

        return new ScenarioSnapshot(
            iteration + 1,
            phase,
            materialLot,
            tooling,
            isProducing,
            alarmActive,
            cycleNumber,
            completedCycleNumber);
    }
}

public sealed record ScenarioSnapshot(
    int Iteration,
    string Phase,
    string MaterialLot,
    string Tooling,
    bool IsProducing,
    bool AlarmActive,
    int CycleNumber,
    int CompletedCycleNumber);
