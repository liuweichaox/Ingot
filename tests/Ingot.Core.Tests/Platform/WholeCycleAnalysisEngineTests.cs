using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Domain.Events;
using Ingot.Platform.Infrastructure.Cycles;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class WholeCycleAnalysisEngineTests
{
    private static readonly DateTimeOffset Start = new(2026, 7, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Analyze_AllowsIrregularSuccessfulPollingIntervals()
    {
        var rows = new[]
        {
            Sample(1, 0, 0),
            Sample(2, 101, 1),
            Sample(3, 211, 2),
            Sample(4, 312, 3),
            Sample(5, 422, 4)
        };

        var result = new WholeCycleAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(422),
            Model(),
            Plan("mean", "integral", "slope"));

        Assert.Equal(ProcessDataStatuses.Available, result.Quality.Status);
        Assert.Equal(110, result.Quality.MaximumGapMs);
        var mean = result.Signals[0].Features.Single(item => item.Code == "mean");
        Assert.Equal(2.021, mean.Value!.Value, 3);
        Assert.Equal(1, mean.DefinitionVersion);
        Assert.Equal(64, mean.DefinitionHash.Length);
        Assert.Equal(64, mean.ComputationHash.Length);
        Assert.Equal(5, mean.InputPointCount);
        var repeated = new WholeCycleAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(422),
            Model(),
            Plan("mean", "integral", "slope"));
        Assert.Equal(mean.ComputationHash, repeated.Signals[0].Features.Single(item => item.Code == "mean").ComputationHash);
        Assert.Equal(0.853, result.Signals[0].Features.Single(item => item.Code == "integral").Value!.Value, 3);
        Assert.InRange(result.Signals[0].Features.Single(item => item.Code == "slope").Value!.Value, 9, 11);
    }

    [Fact]
    public void Analyze_DegradesOnIntermediateInterruptionAndDoesNotBridgeIt()
    {
        var rows = new[]
        {
            Sample(1, 0, 1),
            Sample(2, 100, 1),
            Sample(3, 200, 1),
            Sample(4, 1300, 10),
            Sample(5, 1400, 10)
        };

        var result = new WholeCycleAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(1400),
            Model(),
            Plan("integral"));

        Assert.Equal(ProcessDataStatuses.Degraded, result.Quality.Status);
        Assert.Contains(result.Quality.Issues, item => item.Contains("最大采样空窗", StringComparison.Ordinal));
        var feature = result.Signals[0].Features.Single();
        Assert.Equal(300, feature.ValidDurationMs);
        Assert.Equal(1.2, feature.Value!.Value, 3);
    }

    [Fact]
    public void Analyze_CompletedCycleWithoutSamplesIsUnavailable()
    {
        var result = new WholeCycleAnalysisEngine().Analyze(
            [],
            Start,
            Start.AddSeconds(10),
            Model(),
            Plan("mean"));

        Assert.Equal(ProcessDataStatuses.Unavailable, result.Quality.Status);
        Assert.Contains("周期内没有过程采样。", result.Quality.Issues);
    }

    [Fact]
    public void Analyze_UsesLastIngestedValueForDuplicateTimestamp()
    {
        var rows = new[]
        {
            Sample(1, 0, 1),
            Sample(2, 100, 2),
            Sample(3, 100, 8),
            Sample(4, 200, 3)
        };

        var result = new WholeCycleAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(200),
            Model(),
            Plan("max"));

        Assert.Equal(ProcessDataStatuses.Degraded, result.Quality.Status);
        Assert.Equal(1, result.Quality.DuplicateTimestampCount);
        Assert.Equal(8, result.Signals[0].Maximum);
    }

    [Fact]
    public void Analyze_ComputesFeaturesForConfiguredStageIntervals()
    {
        var rows = new[]
        {
            Sample(1, 0, 1, "10"),
            Sample(2, 100, 2, "10"),
            Sample(3, 200, 10, "20"),
            Sample(4, 300, 12, "20")
        };

        var result = new WholeCycleAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(400),
            ModelWithStages(),
            Plan("min", "max", "slope"));

        Assert.Equal(ProcessDataStatuses.Available, result.Quality.Status);
        Assert.Collection(
            result.Phases,
            phase =>
            {
                Assert.Equal("preheat", phase.Code);
                Assert.Equal(1, phase.Order);
                Assert.Equal("recipe_step", phase.Source);
                Assert.Equal(Start, phase.StartedAt);
                Assert.Equal(Start.AddMilliseconds(200), phase.EndedAt);
            },
            phase =>
            {
                Assert.Equal("press", phase.Code);
                Assert.Equal(2, phase.Order);
                Assert.True(phase.IsComplete);
            });
        var features = result.Signals[0].Features;
        Assert.Equal(1, features.Single(item =>
            item.Code == "min" && item.PhaseCode == "preheat" && item.PhaseOrder == 1).Value);
        Assert.Equal(12, features.Single(item =>
            item.Code == "max" && item.PhaseCode == "press" && item.PhaseOrder == 2).Value);
        Assert.All(features.Where(static item => item.PhaseCode is not null),
            feature => Assert.Equal("recipe_step", feature.PhaseSource));
    }

    [Fact]
    public void Analyze_DegradesWhenARequiredStageIsMissing()
    {
        var rows = new[]
        {
            Sample(1, 0, 1, "10"),
            Sample(2, 100, 2, "10"),
            Sample(3, 200, 3, "10")
        };

        var result = new WholeCycleAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(200),
            ModelWithStages(),
            Plan("mean"));

        Assert.Equal(ProcessDataStatuses.Degraded, result.Quality.Status);
        Assert.Contains("缺少必需工艺阶段 press。", result.Quality.Issues);
    }

    [Fact]
    public void Analyze_PreservesRepeatedStageOccurrences()
    {
        var rows = new[]
        {
            Sample(1, 0, 1, "10"),
            Sample(2, 100, 10, "20"),
            Sample(3, 200, 2, "10"),
            Sample(4, 300, 11, "20")
        };

        var result = new WholeCycleAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(300),
            ModelWithStages(),
            Plan("max"));

        Assert.Equal(4, result.Phases.Count);
        Assert.Equal([1, 3], result.Phases.Where(static phase => phase.Code == "preheat")
            .Select(static phase => phase.Order).ToArray());
        Assert.Equal(4, result.Signals[0].Features.Count(item => item.PhaseCode is not null));
    }

    [Fact]
    public void Analyze_rejects_unregistered_feature_semantics()
    {
        var rows = new[] { Sample(1, 0, 1), Sample(2, 100, 2) };

        var error = Assert.Throws<InvalidOperationException>(() =>
            new WholeCycleAnalysisEngine().Analyze(
                rows,
                Start,
                Start.AddMilliseconds(100),
                Model(),
                Plan("mystery-formula")));

        Assert.Contains("未注册的科研特征定义", error.Message, StringComparison.Ordinal);
    }

    private static PlatformProductionEvent Sample(long ingestId, int offsetMs, double value, string? recipeStep = null)
        => new()
        {
            IngestId = ingestId,
            EdgeId = "EDGE-1",
            IngestedAt = Start.AddMilliseconds(offsetMs + 5),
            Event = ProductionEvent.Create(
                "process.sample",
                Start.AddMilliseconds(offsetMs),
                "edge/EDGE-1/plc",
                new ObjectRef("equipment", "PLC-1"),
                "cycle-1",
                context: recipeStep is null
                    ? null
                    : new Dictionary<string, string> { ["recipe_step"] = recipeStep },
                data: new Dictionary<string, object?>
                {
                    ["values"] = new Dictionary<string, object?> { ["temperature"] = value }
                })
        };

    private static ProcessDataModel ModelWithStages()
        => Model() with
        {
            Acquisition = Model().Acquisition with { StepSourceKey = "recipe_step" },
            Stages =
            [
                new ProcessStageDefinition
                {
                    SourceStep = "10",
                    Code = "preheat",
                    Name = "预热"
                },
                new ProcessStageDefinition
                {
                    SourceStep = "20",
                    Code = "press",
                    Name = "压制"
                }
            ]
        };

    private static ProcessDataModel Model()
        => new()
        {
            ModelId = "model",
            Name = "Model",
            Acquisition = new AcquisitionModel
            {
                DataItems =
                [
                    new ProcessDataItemDefinition
                    {
                        Code = "temperature",
                        SourceField = "温度",
                        Nullable = false
                    }
                ]
            }
        };

    private static ProcessAnalysisPlan Plan(params string[] features)
        => new()
        {
            PlanId = "plan",
            Name = "Plan",
            DataModelId = "model",
            Signals =
            [
                new AnalysisSignalSelection
                {
                    DataItemCode = "temperature",
                    Features = features
                }
            ]
        };
}
