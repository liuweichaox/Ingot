// 验证平台组件 ProcessExecutionAnalysisEngine 的成功、拒绝和安全边界。

using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.TimeSeries;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessExecutionAnalysisEngineTests
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

        var result = new ProcessExecutionAnalysisEngine().Analyze(
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
        var repeated = new ProcessExecutionAnalysisEngine().Analyze(
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

        var result = new ProcessExecutionAnalysisEngine().Analyze(
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
    public void Analyze_CompletedProcessExecutionWithoutSamplesIsUnavailable()
    {
        var result = new ProcessExecutionAnalysisEngine().Analyze(
            [],
            Start,
            Start.AddSeconds(10),
            Model(),
            Plan("mean"));

        Assert.Equal(ProcessDataStatuses.Unavailable, result.Quality.Status);
        Assert.Contains("过程执行内没有过程采样。", result.Quality.Issues);
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

        var result = new ProcessExecutionAnalysisEngine().Analyze(
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
    public void Analyze_ReportsSourceClockOffsetAndPlatformIngestLatency()
    {
        var rows = new[]
        {
            Sample(1, 0, 1, recordedDelayMs: 100, ingestDelayMs: 50),
            Sample(2, 100, 2, recordedDelayMs: 200, ingestDelayMs: 100)
        };

        var result = new ProcessExecutionAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(100),
            Model(),
            Plan("mean"));

        Assert.Equal(150, result.Quality.MedianSourceClockOffsetMs);
        Assert.Equal(200, result.Quality.MaximumAbsoluteSourceClockOffsetMs);
        Assert.Equal(75, result.Quality.MedianPlatformIngestLatencyMs);
        Assert.Equal(97.5, result.Quality.P95PlatformIngestLatencyMs);
        Assert.Equal(100, result.Quality.MaximumPlatformIngestLatencyMs);
        Assert.Equal(0, result.Quality.NegativePlatformIngestLatencyCount);
    }

    [Fact]
    public void Analyze_CountsMaterialNegativeIngestLatencyAsClockAnomaly()
    {
        var result = new ProcessExecutionAnalysisEngine().Analyze(
            [Sample(1, 0, 1, recordedDelayMs: 0, ingestDelayMs: -1501)],
            Start,
            Start.AddMilliseconds(1),
            Model(),
            Plan("mean"));

        Assert.Equal(1, result.Quality.NegativePlatformIngestLatencyCount);
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

        var result = new ProcessExecutionAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(400),
            ModelWithStageNumber(),
            Plan("min", "max", "slope"));

        Assert.Equal(ProcessDataStatuses.Available, result.Quality.Status);
        Assert.Collection(
            result.Phases,
            phase =>
            {
                Assert.Equal("10", phase.Code);
                Assert.Equal(1, phase.Order);
                Assert.Equal("stage_number", phase.Source);
                Assert.Equal(Start, phase.StartedAt);
                Assert.Equal(Start.AddMilliseconds(200), phase.EndedAt);
            },
            phase =>
            {
                Assert.Equal("20", phase.Code);
                Assert.Equal(2, phase.Order);
                Assert.Equal(Start.AddMilliseconds(400), phase.EndedAt);
            });
        var features = result.Signals[0].Features;
        Assert.Equal(1, features.Single(item =>
            item.Code == "min" && item.PhaseCode == "10" && item.PhaseOrder == 1).Value);
        Assert.Equal(12, features.Single(item =>
            item.Code == "max" && item.PhaseCode == "20" && item.PhaseOrder == 2).Value);
        Assert.All(features.Where(static item => item.PhaseCode is not null),
            feature => Assert.Equal("stage_number", feature.PhaseSource));
    }

    [Fact]
    public void Analyze_UsesOneSampleDomainForPhaseMeanAndExtrema()
    {
        var rows = new[]
        {
            Sample(1, 0, 0, "10"),
            Sample(2, 100, 0, "10"),
            Sample(3, 200, 100, "20"),
            Sample(4, 300, 100, "20")
        };

        var result = new ProcessExecutionAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(400),
            ModelWithStageNumber(),
            Plan("min", "mean", "max"));

        var phaseFeatures = result.Signals[0].Features
            .Where(item => item.PhaseCode == "10")
            .ToDictionary(item => item.Code, item => item.Value!.Value);

        Assert.True(phaseFeatures["min"] <= phaseFeatures["mean"]);
        Assert.True(phaseFeatures["mean"] <= phaseFeatures["max"]);
        Assert.Equal(100, phaseFeatures["max"]);
    }

    [Fact]
    public void Analyze_DoesNotUseStageNumberCoverageAsProcessExecutionCompleteness()
    {
        var rows = new[]
        {
            Sample(1, 0, 1, "10"),
            Sample(2, 100, 2),
            Sample(3, 200, 3, "10")
        };

        var result = new ProcessExecutionAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(200),
            ModelWithStageNumber(),
            Plan("mean"));

        Assert.Equal(ProcessDataStatuses.Available, result.Quality.Status);
        Assert.DoesNotContain(result.Quality.Issues, issue => issue.Contains("阶段", StringComparison.Ordinal));
        Assert.Contains(result.Phases, phase => phase.Code == "unknown");
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

        var result = new ProcessExecutionAnalysisEngine().Analyze(
            rows,
            Start,
            Start.AddMilliseconds(300),
            ModelWithStageNumber(),
            Plan("max"));

        Assert.Equal(4, result.Phases.Count);
        Assert.Equal([1, 3], result.Phases.Where(static phase => phase.Code == "10")
            .Select(static phase => phase.Order).ToArray());
        Assert.Equal(4, result.Signals[0].Features.Count(item => item.PhaseCode is not null));
    }

    [Fact]
    public void Analyze_rejects_unregistered_feature_semantics()
    {
        var rows = new[] { Sample(1, 0, 1), Sample(2, 100, 2) };

        var error = Assert.Throws<InvalidOperationException>(() =>
            new ProcessExecutionAnalysisEngine().Analyze(
                rows,
                Start,
                Start.AddMilliseconds(100),
                Model(),
                Plan("mystery-formula")));

        Assert.Contains("未注册的科研特征定义", error.Message, StringComparison.Ordinal);
    }

    private static ProcessSampleFrame Sample(
        long ingestId,
        int offsetMs,
        double value,
        string? stageNumber = null,
        int recordedDelayMs = 2,
        int ingestDelayMs = 3)
    {
        var occurredAt = Start.AddMilliseconds(offsetMs);
        var recordedAt = occurredAt.AddMilliseconds(recordedDelayMs);
        return new ProcessSampleFrame
        {
            EventId = $"event-{ingestId}",
            IngestId = ingestId,
            OccurredAt = occurredAt,
            RecordedAt = recordedAt,
            IngestedAt = recordedAt.AddMilliseconds(ingestDelayMs),
            PhaseCode = stageNumber,
            NumericValues = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["temperature"] = value
            }
        };
    }

    private static ProcessDataModel ModelWithStageNumber()
        => Model() with
        {
            Acquisition = Model().Acquisition with
            {
                DataItems =
                [
                    Model().Acquisition.DataItems[0],
                    new ProcessDataItemDefinition
                    {
                        Code = "process.stage_number",
                        DisplayName = "阶段号",
                        DataType = "integer",
                        Category = "stage",
                        Nullable = false
                    }
                ]
            }
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
                        DisplayName = "温度",
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
