using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

/// <summary>
///     「黄金过程执行」端到端回归：用一条经过工艺确认的光学模压过程执行喂进产品实际使用的
///     <see cref="ProcessExecutionAnalysisEngine"/>，断言它对真实数据说出
///     <b>物理上为真</b>的话，且结果确定可复算。
///
///     为什么存在：本项目此前所有测试都在验证「工程」（纯函数/映射/校验），从未验证「产品对真实工艺数据
///     产出的洞察是否正确」。这条测试把领域真值（升温曲线、压制阶段高压力）写进回归门禁——它一旦变红，
///     说明分析核心对真实模压数据的理解退化了，比第 200 个单测更有价值。
///
///     断言策略：max/min/采样数/空窗/确定性哈希是<b>与加权算法无关</b>的不变量，精确断言；
///     time-weighted「mean」随算法演进允许微调，用范围断言以免脆化。
/// </summary>
public sealed class GoldenProcessExecutionAnalysisTests
{
    // 过程执行起点（process.execution.started 的 OccurredAt）。真实数据为 2026-06-01 08:00:00。
    private static readonly DateTimeOffset ProcessExecutionStart = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);
    // 过程执行终点（process.execution.completed 的 OccurredAt）= 起点 +95s（08:01:35）。
    private static readonly DateTimeOffset ProcessExecutionEnd = ProcessExecutionStart.AddSeconds(95);

    // CYC-0001 的 5 条 process.sample（process.execution.started/completed 不是采样，引擎会自动过滤）。
    // 列：距起点秒数, 上模温 t_upper(℃), 合模力 force(kN)。取自真实 CSV，未做任何修改。
    private static readonly (int OffsetSeconds, double TUpper, double Force)[] RealSamples =
    [
        (20, 300.3, 0.0),
        (35, 341.5, 0.0),
        (50, 378.8, 523.5),
        (65, 419.0, 526.0),
        (80, 462.4, 513.4)
    ];

    [Fact]
    public void RealMoldingProcessExecution_ProducesPhysicallyTrueAndDeterministicAnalysis()
    {
        var rows = BuildRealProcessExecution();

        var result = new ProcessExecutionAnalysisEngine().Analyze(
            rows,
            ProcessExecutionStart,
            ProcessExecutionEnd,
            MoldingModel(),
            MoldingPlan("mean", "max", "min"));

        // —— 数据质量：规则 15s 采样、有始有终 → 应为「可用」，最大空窗 15s ——
        Assert.Equal(ProcessDataStatuses.Available, result.Quality.Status);
        Assert.Equal(15_000, result.Quality.MaximumGapMs);

        // —— 两路信号都在 ——
        Assert.Equal(2, result.Signals.Count);
        var temp = result.Signals.Single(s => s.Code == "temperature");
        var force = result.Signals.Single(s => s.Code == "force");
        Assert.Equal(5, temp.SampleCount);
        Assert.Equal(5, force.SampleCount);

        // —— 物理真值（与加权无关，精确断言）——
        // 升温：峰值 462.4℃、谷值 300.3℃，来自真实曲线。
        Assert.Equal(462.4, temp.Maximum!.Value, 3);
        Assert.Equal(300.3, temp.Minimum!.Value, 3);
        // 合模力：压制/退火阶段峰值 526.0kN，预热/保温阶段 0kN。这正是「压制真的发生了」的证据。
        Assert.Equal(526.0, force.Maximum!.Value, 3);
        Assert.Equal(0.0, force.Minimum!.Value, 3);

        // —— 特征值同样精确（max/min 与加权无关）——
        Assert.Equal(462.4, TempFeature(result, "max").Value!.Value, 3);
        Assert.Equal(300.3, TempFeature(result, "min").Value!.Value, 3);
        Assert.Equal(526.0, ForceFeature(result, "max").Value!.Value, 3);

        // —— time-weighted mean：算术均值 温 380.4 / 力 312.6ｈ 时间加权应落在其附近区间 ——
        Assert.InRange(TempFeature(result, "mean").Value!.Value, 360.0, 400.0);
        Assert.InRange(ForceFeature(result, "mean").Value!.Value, 280.0, 345.0);

        // —— 每个特征都基于 5 个真实采样点 ——
        Assert.Equal(5, TempFeature(result, "mean").InputPointCount);

        // —— 确定性：同输入复算，计算哈希逐字节一致（可复算核对用）——
        var repeated = new ProcessExecutionAnalysisEngine().Analyze(
            BuildRealProcessExecution(), ProcessExecutionStart, ProcessExecutionEnd, MoldingModel(), MoldingPlan("mean", "max", "min"));
        var hash = TempFeature(result, "mean").ComputationHash;
        Assert.Equal(64, hash.Length); // SHA-256 十六进制
        Assert.Equal(hash, TempFeature(repeated, "mean").ComputationHash);
    }

    private static IReadOnlyList<PlatformProductionEvent> BuildRealProcessExecution()
    {
        var events = new List<PlatformProductionEvent>();
        long ingestId = 1;
        foreach (var (offsetSeconds, tUpper, force) in RealSamples)
        {
            var at = ProcessExecutionStart.AddSeconds(offsetSeconds);
            events.Add(new PlatformProductionEvent
            {
                IngestId = ingestId++,
                EdgeId = "EDGE-1",
                IngestedAt = at.AddMilliseconds(5),
                Event = ProductionEvent.Create(
                    "process.sample",
                    at,
                    "edge/EDGE-1/fx3u",
                    new ObjectRef("equipment", "PRESS-01"),
                    "CYC-0001",
                    context: null,
                    data: new Dictionary<string, object?>
                    {
                        ["values"] = new Dictionary<string, object?>
                        {
                            ["temperature"] = tUpper,
                            ["force"] = force
                        }
                    })
            });
        }

        return events;
    }

    private static ProcessSignalFeature TempFeature(WholeProcessExecutionAnalysisResult result, string code)
        => result.Signals.Single(s => s.Code == "temperature").Features.Single(f => f.Code == code);

    private static ProcessSignalFeature ForceFeature(WholeProcessExecutionAnalysisResult result, string code)
        => result.Signals.Single(s => s.Code == "force").Features.Single(f => f.Code == code);

    private static ProcessDataModel MoldingModel()
        => new()
        {
            ModelId = "optical-molding",
            Name = "光学模压",
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition { Code = "temperature", DisplayName = "上模温", Nullable = false },
                    new ProcessDataItemDefinition { Code = "force", DisplayName = "合模力", Nullable = false }
                ]
            }
        };

    private static ProcessAnalysisPlan MoldingPlan(params string[] features)
        => new()
        {
            PlanId = "optical-molding-plan",
            Name = "光学模压分析计划",
            DataModelId = "optical-molding",
            Signals =
            [
                new AnalysisSignalSelection { DataItemCode = "temperature", Features = features },
                new AnalysisSignalSelection { DataItemCode = "force", Features = features }
            ]
        };
}
