namespace Ingot.Simulator;

/// <summary>PLC 模拟器运行选项。</summary>
public sealed class SimulatorOptions
{
    public int Port { get; set; } = 502;

    /// <summary>Continuous 保持连续波形；Scenario 运行 RFC 光学闭环剧本。</summary>
    public string Mode { get; set; } = "Continuous";

    /// <summary>剧本时间倍率。2 表示用一半真实时间完成剧本。</summary>
    public double ScenarioSpeed { get; set; } = 1;

    public int UpdateIntervalMs { get; set; } = 100;

    public int ConsoleIntervalMs { get; set; } = 1_000;

    public bool IsScenario =>
        string.Equals(Mode, "Scenario", StringComparison.OrdinalIgnoreCase);
}
