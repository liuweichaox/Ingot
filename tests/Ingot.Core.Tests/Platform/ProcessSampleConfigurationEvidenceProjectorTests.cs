// 验证过程采样帧的配置来源与已观测值质量汇总边界。

using Ingot.Contracts.Events;
using Ingot.Domain.Events;
using Ingot.Platform.Application.TimeSeries;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessSampleConfigurationEvidenceProjectorTests
{
    [Fact]
    public void Project_groups_frames_by_configuration_and_counts_quality_rows()
    {
        var start = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        var configurationV3 = new AppliedConfigurationRef("ingestion-task", "task-01", 3);
        var configurationV4 = new AppliedConfigurationRef("ingestion-task", "task-01", 4);
        var firstFrame = Frame(1, start, configurationV3,
            new Dictionary<string, string>
            {
                ["temperature"] = SignalQualityCodes.Good,
                ["mode"] = SignalQualityCodes.Uncertain
            },
            new Dictionary<string, double> { ["temperature"] = 22.5 });
        var frames = new[]
        {
            firstFrame,
            Frame(2, start.AddSeconds(2), null,
                new Dictionary<string, string> { ["pressure"] = SignalQualityCodes.Good }),
            Frame(3, start.AddSeconds(4), configurationV4,
                new Dictionary<string, string>
                {
                    ["temperature"] = SignalQualityCodes.Bad,
                    ["mode"] = "unsupported"
                }),
            Frame(4, start.AddSeconds(5), configurationV3,
                new Dictionary<string, string> { ["temperature"] = SignalQualityCodes.Good })
        };

        var evidence = ProcessSampleConfigurationEvidenceProjector.Project(frames);

        Assert.Equal(3, evidence.Count);
        Assert.All(evidence, item => Assert.Equal(
            ProcessSampleConfigurationReferenceStatuses.NotEvaluated,
            item.ReferenceVerificationStatus));
        var v3 = Assert.Single(evidence, item => item.AppliedConfigurationVersion == 3);
        Assert.Equal("ingestion-task", v3.AppliedConfigurationKind);
        Assert.Equal("task-01", v3.AppliedConfigurationId);
        Assert.Equal(2, v3.SampleFrameCount);
        Assert.Equal(start, v3.FirstOccurredAt);
        Assert.Equal(start.AddSeconds(5), v3.LastOccurredAt);
        Assert.Equal(3, v3.ObservedValueCount);
        Assert.Equal(2, v3.GoodValueCount);
        Assert.Equal(1, v3.UncertainValueCount);
        Assert.Equal(0, v3.BadValueCount);
        Assert.Equal(0, v3.UnclassifiedQualityValueCount);

        var unknown = Assert.Single(evidence, item => item.AppliedConfigurationKind is null);
        Assert.Null(unknown.AppliedConfigurationId);
        Assert.Null(unknown.AppliedConfigurationVersion);
        Assert.Equal(1, unknown.SampleFrameCount);
        Assert.Equal(1, unknown.GoodValueCount);

        var v4 = Assert.Single(evidence, item => item.AppliedConfigurationVersion == 4);
        Assert.Equal(start.AddSeconds(4), v4.FirstOccurredAt);
        Assert.Equal(2, v4.ObservedValueCount);
        Assert.Equal(1, v4.BadValueCount);
        Assert.Equal(1, v4.UnclassifiedQualityValueCount);
    }

    [Fact]
    public void Project_returns_empty_evidence_when_no_frames_were_returned()
    {
        Assert.Empty(ProcessSampleConfigurationEvidenceProjector.Project([]));
    }

    [Fact]
    public void Project_counts_same_frame_id_at_distinct_timestamps_separately()
    {
        var start = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        var configuration = new AppliedConfigurationRef("ingestion-task", "task-01", 3);
        var frames = new[]
        {
            Frame(1, start, configuration,
                new Dictionary<string, string> { ["temperature"] = SignalQualityCodes.Good }),
            Frame(1, start.AddSeconds(1), configuration,
                new Dictionary<string, string> { ["temperature"] = SignalQualityCodes.Bad })
        };

        var evidence = Assert.Single(ProcessSampleConfigurationEvidenceProjector.Project(frames));

        Assert.Equal(2, evidence.SampleFrameCount);
        Assert.Equal(2, evidence.ObservedValueCount);
        Assert.Equal(1, evidence.GoodValueCount);
        Assert.Equal(1, evidence.BadValueCount);
        Assert.Equal(start, evidence.FirstOccurredAt);
        Assert.Equal(start.AddSeconds(1), evidence.LastOccurredAt);
    }

    [Fact]
    public void Project_keeps_same_configuration_separate_for_each_source_edge()
    {
        var start = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        var configuration = new AppliedConfigurationRef("ingestion-task", "task-01", 3);
        var frames = new[]
        {
            Frame(1, start, configuration,
                new Dictionary<string, string> { ["temperature"] = SignalQualityCodes.Good }, edgeId: "EDGE-A"),
            Frame(2, start.AddSeconds(1), configuration,
                new Dictionary<string, string> { ["temperature"] = SignalQualityCodes.Bad }, edgeId: "EDGE-B"),
            Frame(3, start.AddSeconds(2), configuration,
                new Dictionary<string, string> { ["temperature"] = SignalQualityCodes.Good }, edgeId: "EDGE-A")
        };

        var evidence = ProcessSampleConfigurationEvidenceProjector.Project(frames);

        Assert.Equal(2, evidence.Count);
        var edgeA = Assert.Single(evidence, item => item.SourceEdgeId == "EDGE-A");
        Assert.Equal(2, edgeA.SampleFrameCount);
        var edgeB = Assert.Single(evidence, item => item.SourceEdgeId == "EDGE-B");
        Assert.Equal(1, edgeB.SampleFrameCount);
        Assert.Equal("ingestion-task", edgeB.AppliedConfigurationKind);
    }

    [Fact]
    public void Project_summarizes_configuration_groups_after_the_detail_limit()
    {
        var start = new DateTimeOffset(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        var frames = Enumerable.Range(1, 25)
            .SelectMany(version =>
            {
                var configuration = new AppliedConfigurationRef("ingestion-task", $"task-{version:D2}", version);
                var primaryQuality = version switch
                {
                    21 => SignalQualityCodes.Good,
                    22 => SignalQualityCodes.Uncertain,
                    _ => SignalQualityCodes.Bad
                };
                var primaryFrame = Frame(version, start.AddSeconds(version), configuration,
                    new Dictionary<string, string> { ["temperature"] = primaryQuality });
                return version == 22
                    ? new[]
                    {
                        primaryFrame,
                        Frame(100 + version, start.AddSeconds(version + 1), configuration,
                            new Dictionary<string, string> { ["pressure"] = SignalQualityCodes.Bad })
                    }
                    : new[] { primaryFrame };
            })
            .ToArray();

        var evidence = ProcessSampleConfigurationEvidenceProjector.Project(frames);

        Assert.Equal(21, evidence.Count);
        Assert.Equal(20, evidence.Count(item => item.SummarizedConfigurationGroupCount == 0));
        var summary = Assert.Single(evidence, item => item.SummarizedConfigurationGroupCount > 0);
        Assert.Null(summary.AppliedConfigurationKind);
        Assert.Null(summary.AppliedConfigurationId);
        Assert.Null(summary.AppliedConfigurationVersion);
        Assert.Null(summary.SourceEdgeId);
        Assert.Equal(5, summary.SummarizedConfigurationGroupCount);
        Assert.Equal(6, summary.SampleFrameCount);
        Assert.Equal(start.AddSeconds(21), summary.FirstOccurredAt);
        Assert.Equal(start.AddSeconds(25), summary.LastOccurredAt);
        Assert.Equal(6, summary.ObservedValueCount);
        Assert.Equal(1, summary.GoodValueCount);
        Assert.Equal(1, summary.UncertainValueCount);
        Assert.Equal(4, summary.BadValueCount);
        Assert.Equal(0, summary.UnclassifiedQualityValueCount);
    }

    private static ProcessSampleFrame Frame(
        long ingestId,
        DateTimeOffset occurredAt,
        AppliedConfigurationRef? configuration,
        IReadOnlyDictionary<string, string> qualityCodes,
        IReadOnlyDictionary<string, double>? numericValues = null,
        string? edgeId = null)
        => new()
        {
            EventId = $"event-{ingestId}",
            IngestId = ingestId,
            EdgeId = edgeId,
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
            AppliedConfiguration = configuration,
            QualityCodes = qualityCodes,
            NumericValues = numericValues ?? new Dictionary<string, double>()
        };
}
