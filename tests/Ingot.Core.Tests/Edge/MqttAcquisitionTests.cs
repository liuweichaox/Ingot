using System.Text;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

public sealed class MqttAcquisitionTests
{
    [Theory]
    [InlineData("plant/+/press", "plant/line1/press", true)]
    [InlineData("plant/#", "plant/line1/press", true)]
    [InlineData("plant/line1/#", "plant/line1", true)]
    [InlineData("plant/line1/press", "plant/line2/press", false)]
    public void TopicFiltersMatchMqttTopics(string filter, string topic, bool expected)
        => Assert.Equal(expected, MqttSnapshotAccumulator.MatchesTopicFilter(filter, topic));

    [Theory]
    [InlineData("plant/+/telemetry", "plant/press01/#", true)]
    [InlineData("plant/a/#", "plant/b/+", false)]
    [InlineData("plant/a", "plant/a/#", true)]
    public void TopicFilterOverlapIsDetected(string first, string second, bool expected)
        => Assert.Equal(expected, MqttTopicFilter.Intersects(first, second));

    [Fact]
    public void PayloadRootsAreUnwrappedAndTopicSnapshotsAreMerged()
    {
        var accumulator = new MqttSnapshotAccumulator(
        [
            new MqttTopicSubscription { Topic = "line/temperature", PayloadRoot = "payload" },
            new MqttTopicSubscription { Topic = "line/pressure", PayloadRoot = "payload" }
        ]);

        using var first = accumulator.Add(
            "line/temperature",
            Encoding.UTF8.GetBytes("{\"payload\":{\"value\":11}}"));
        using var second = accumulator.Add(
            "line/pressure",
            Encoding.UTF8.GetBytes("{\"payload\":{\"value\":22}}"));

        Assert.Equal(22, second.Aggregate.RootElement.GetProperty("value").GetInt32());
        Assert.Equal(11, second.TopicSnapshots["line/temperature"].GetProperty("value").GetInt32());
        Assert.Equal(22, second.TopicSnapshots["line/pressure"].GetProperty("value").GetInt32());
    }

    [Fact]
    public void TopicBoundMappingsReadFromTheirOwnTopicInsteadOfMergedLastValue()
    {
        var accumulator = new MqttSnapshotAccumulator(
        [
            new MqttTopicSubscription { Topic = "line/temperature" },
            new MqttTopicSubscription { Topic = "line/pressure" }
        ]);
        using var first = accumulator.Add("line/temperature", Encoding.UTF8.GetBytes("{\"value\":11}"));
        using var second = accumulator.Add("line/pressure", Encoding.UTF8.GetBytes("{\"value\":22}"));

        var options = new HttpPollingAcquisitionOptions
        {
            SubjectType = "equipment",
            SubjectId = "press-01",
            Source = "mqtt/press-01",
            TimestampMode = "edge-received",
            Fields =
            [
                new ValueFieldMapping
                {
                    Code = "temperature",
                    SourcePath = "value",
                    DataType = "integer",
                    Topic = "line/temperature"
                },
                new ValueFieldMapping
                {
                    Code = "pressure",
                    SourcePath = "value",
                    DataType = "integer",
                    Topic = "line/pressure"
                }
            ]
        };

        var mapped = HttpPollingSnapshotMapper.Map(
            second.Aggregate.RootElement,
            options,
            options.Source,
            previousProcessSpecificationIdentity: null,
            second.TopicSnapshots);
        var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            mapped.Sample.Data["values"]);

        Assert.Equal(11L, values["temperature"]);
        Assert.Equal(22L, values["pressure"]);
    }

    [Fact]
    public void CrossTopicSnapshotsRejectStaleData()
    {
        var accumulator = new MqttSnapshotAccumulator(
        [
            new MqttTopicSubscription { Topic = "line/temperature" },
            new MqttTopicSubscription { Topic = "line/pressure" }
        ]);
        var now = DateTimeOffset.UtcNow;

        using var first = accumulator.Add(
            "line/temperature",
            Encoding.UTF8.GetBytes("{\"value\":11}"),
            now.AddSeconds(-10),
            maxAgeSeconds: 30,
            maxSkewSeconds: 5);
        using var second = accumulator.Add(
            "line/pressure",
            Encoding.UTF8.GetBytes("{\"value\":22}"),
            now,
            maxAgeSeconds: 30,
            maxSkewSeconds: 5);

        Assert.True(second.IsComplete);
        Assert.False(second.IsCoherent);
    }
}
