using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition;
using Xunit;

namespace Ingot.Core.Tests.Edge;

/// <summary>
///     跨主题合并快照。以前 MQTT 采集器要求每条报文都是完整快照，
///     "温度在一个主题、压力在另一个主题" 的网关无法接入。
/// </summary>
public class MqttSnapshotAssemblerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

    private static AcquisitionProfile Profile(
        IEnumerable<AcquisitionValueMapping> values,
        IEnumerable<AcquisitionContextMapping>? contexts = null,
        AcquisitionRecipeMapping? recipe = null,
        string timestampMode = "edge-received")
        => new()
        {
            ProfileId = "gateway", Name = "网关接入", EdgeId = "EDGE-001",
            Protocol = AcquisitionProtocols.Mqtt, DataModelId = "model", Source = "connector/gateway",
            SubjectId = "PRESS-01", TimestampMode = timestampMode, SequencePath = null,
            ValueMappings = values.ToArray(),
            ContextMappings = (contexts ?? []).ToArray(),
            Recipe = recipe
        };

    private static AcquisitionValueMapping Value(string code, string path, string? topic, bool required = true)
        => new() { DataItemCode = code, SourcePath = path, Topic = topic, Required = required };

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void MergesValuesArrivingOnDifferentTopics()
    {
        var profile = Profile([
            Value("furnace.temperature", "sensors.temperature", "plant/press01/thermal"),
            Value("furnace.pressure", "sensors.pressure", "plant/press01/hydraulic")
        ]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);

        // 第一个主题到达时快照还不完整，不产生采样。
        Assert.True(assembler.Ingest("plant/press01/thermal", Json("""{"sensors":{"temperature":511.5}}"""), Now));
        Assert.False(assembler.TryBuildSnapshot(Now, out _, out var missing));
        Assert.Contains("sensors.pressure", missing);

        Assert.True(assembler.Ingest("plant/press01/hydraulic", Json("""{"sensors":{"pressure":12.5}}"""), Now));
        Assert.True(assembler.TryBuildSnapshot(Now, out var snapshot, out _));
        using (snapshot)
        {
            var sensors = snapshot!.RootElement.GetProperty("sensors");
            Assert.Equal(511.5, sensors.GetProperty("temperature").GetDouble());
            Assert.Equal(12.5, sensors.GetProperty("pressure").GetDouble());
        }
    }

    [Fact]
    public void IgnoresFieldsThatArriveOnATopicTheyAreNotBoundTo()
    {
        // 两个主题字段重名时，绑定关系必须决定取哪一个。
        var profile = Profile([Value("furnace.temperature", "value", "plant/press01/thermal")]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        Assert.False(assembler.Ingest("plant/press01/hydraulic", Json("""{"value":999}"""), Now));
        Assert.False(assembler.TryBuildSnapshot(Now, out _, out _));

        Assert.True(assembler.Ingest("plant/press01/thermal", Json("""{"value":511}"""), Now));
        Assert.True(assembler.TryBuildSnapshot(Now, out var snapshot, out _));
        using (snapshot)
            Assert.Equal(511, snapshot!.RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void SamePathOnDifferentTopicsKeepsBothTopicBoundValues()
    {
        var profile = Profile([
            Value("temperature", "value", "plant/thermal"),
            Value("pressure", "value", "plant/hydraulic")
        ]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        assembler.Ingest("plant/thermal", Json("""{"value":511}"""), Now);
        assembler.Ingest("plant/hydraulic", Json("""{"value":12}"""), Now);

        Assert.True(assembler.TryBuildSnapshot(Now, out var snapshot, out _));
        using (snapshot)
        {
            var options = new HttpPollingAcquisitionOptions
            {
                SubjectType = "equipment",
                SubjectId = profile.SubjectId,
                Source = profile.Source,
                TimestampMode = "edge-received",
                Fields =
                [
                    new ValueFieldMapping
                    {
                        Code = "temperature", SourcePath = "value", DataType = "integer",
                        Topic = "plant/thermal"
                    },
                    new ValueFieldMapping
                    {
                        Code = "pressure", SourcePath = "value", DataType = "integer",
                        Topic = "plant/hydraulic"
                    }
                ]
            };
            var mapped = HttpPollingSnapshotMapper.Map(
                snapshot!.RootElement,
                options,
                profile.Source,
                previousRecipeIdentity: null,
                assembler.BuildTopicSnapshots(Now));
            var values = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(mapped.Sample.Data["values"]);
            Assert.Equal(511L, values["temperature"]);
            Assert.Equal(12L, values["pressure"]);
        }
    }

    [Fact]
    public void UnboundFieldsAcceptAnyTopic()
    {
        var profile = Profile([Value("furnace.temperature", "value", null)]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        Assert.True(assembler.Ingest("anything/at/all", Json("""{"value":7}"""), Now));
        Assert.True(assembler.TryBuildSnapshot(Now, out var snapshot, out _));
        using (snapshot)
            Assert.Equal(7, snapshot!.RootElement.GetProperty("value").GetInt32());
    }

    [Fact]
    public void BindingHonoursWildcardSubscriptionFilters()
    {
        var profile = Profile([Value("furnace.temperature", "value", "plant/+/thermal")]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        Assert.True(assembler.Ingest("plant/press01/thermal", Json("""{"value":1}"""), Now));
        Assert.False(assembler.Ingest("plant/press01/line/thermal", Json("""{"value":2}"""), Now));
    }

    [Fact]
    public void ContextOnlyTopicsUpdateStateWithoutTriggeringSamples()
    {
        // 只带上下文的主题不应放大采样率。
        var profile = Profile(
            [Value("furnace.temperature", "sensors.temperature", "plant/press01/thermal")],
            [new AcquisitionContextMapping { ContextKey = "product_series", SourcePath = "product", Topic = "plant/press01/context" }]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        Assert.False(assembler.Ingest("plant/press01/context", Json("""{"product":"L-42"}"""), Now));
        Assert.True(assembler.Ingest("plant/press01/thermal", Json("""{"sensors":{"temperature":500}}"""), Now));
        Assert.True(assembler.TryBuildSnapshot(Now, out var snapshot, out _));
        using (snapshot)
            Assert.Equal("L-42", snapshot!.RootElement.GetProperty("product").GetString());
    }

    [Fact]
    public void StaleValuesAreTreatedAsMissingInsteadOfSilentlyReused()
    {
        // 某个主题停止发布后，合并快照不能一直沿用它最后一次的值。
        var profile = Profile([
            Value("a", "a", "topic/a"),
            Value("b", "b", "topic/b")
        ]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), snapshotMaxAgeSeconds: 30);
        assembler.Ingest("topic/a", Json("""{"a":1}"""), Now);
        assembler.Ingest("topic/b", Json("""{"b":2}"""), Now);
        Assert.True(assembler.TryBuildSnapshot(Now.AddSeconds(20), out _, out _));

        assembler.Ingest("topic/a", Json("""{"a":3}"""), Now.AddSeconds(40));
        Assert.False(assembler.TryBuildSnapshot(Now.AddSeconds(40), out _, out var missing));
        Assert.Contains("已超过 30 秒未更新", missing);
    }

    [Fact]
    public void ZeroMaxAgeKeepsTheOriginalBehaviourOfNeverExpiring()
    {
        var profile = Profile([Value("a", "a", null)]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        assembler.Ingest("topic/a", Json("""{"a":1}"""), Now);
        Assert.True(assembler.TryBuildSnapshot(Now.AddDays(7), out _, out _));
    }

    [Fact]
    public void PayloadEnvelopeIsStrippedBeforeMapping()
    {
        var subscriptions = new[]
        {
            new MqttTopicSubscription { Topic = "plant/+/telemetry", Qos = 0, PayloadRoot = "d" }
        };
        var subscription = MqttSnapshotAssembler.SubscriptionFor(subscriptions, "plant/press01/telemetry");
        Assert.NotNull(subscription);
        var unwrapped = MqttSnapshotAssembler.Unwrap(
            Json("""{"ts":1,"d":{"sensors":{"temperature":500}}}"""), subscription!.PayloadRoot);
        Assert.Equal(500, unwrapped.GetProperty("sensors").GetProperty("temperature").GetInt32());
    }

    [Fact]
    public void MissingEnvelopeIsReportedInsteadOfSilentlyProducingAnEmptySnapshot()
        => Assert.Throws<InvalidDataException>(() =>
            MqttSnapshotAssembler.Unwrap(Json("""{"other":1}"""), "d"));

    [Fact]
    public void OptionalFieldsDoNotBlockTheSnapshot()
    {
        var profile = Profile([
            Value("a", "a", null),
            Value("b", "b", null, required: false)
        ]);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        assembler.Ingest("t", Json("""{"a":1}"""), Now);
        Assert.True(assembler.TryBuildSnapshot(Now, out var snapshot, out _));
        using (snapshot)
            Assert.False(snapshot!.RootElement.TryGetProperty("b", out _));
    }

    [Fact]
    public void RecipeParametersKeepTheirRelativePathsInTheMergedSnapshot()
    {
        var recipe = new AcquisitionRecipeMapping
        {
            IdPath = "recipe.id",
            VersionPath = "recipe.version",
            ParametersPath = "recipe.parameters",
            ParameterMappings = [new AcquisitionValueMapping { DataItemCode = "temperature.target", SourcePath = "target" }]
        };
        var profile = Profile([Value("a", "a", null)], recipe: recipe);
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        assembler.Ingest("t", Json("""{"a":1,"recipe":{"id":"R-1","version":"3","parameters":{"target":520}}}"""), Now);
        Assert.True(assembler.TryBuildSnapshot(Now, out var snapshot, out _));
        using (snapshot)
        {
            var rebuilt = snapshot!.RootElement.GetProperty("recipe");
            Assert.Equal("R-1", rebuilt.GetProperty("id").GetString());
            Assert.Equal(520, rebuilt.GetProperty("parameters").GetProperty("target").GetInt32());
        }
    }

    [Fact]
    public void SourceTimestampIsRequiredWhenConfigured()
    {
        var profile = Profile([Value("a", "a", "topic/a")], timestampMode: "source") with { TimestampPath = "ts" };
        var assembler = new MqttSnapshotAssembler(MqttSnapshotAssembler.SlotsFor(profile), 0);
        assembler.Ingest("topic/a", Json("""{"a":1}"""), Now);
        Assert.False(assembler.TryBuildSnapshot(Now, out _, out var missing));
        Assert.Contains("ts", missing);
    }

    [Theory]
    [InlineData("plant/+/line", "plant/press01/line", true)]
    [InlineData("plant/+/line", "plant/press01/x/line", false)]
    [InlineData("plant/#", "plant/press01/line", true)]
    [InlineData("plant/#", "other/press01", false)]
    [InlineData("plant/press01", "plant/press01", true)]
    [InlineData("#", "$SYS/broker/uptime", false)]
    public void TopicFilterMatching(string filter, string topic, bool expected)
        => Assert.Equal(expected, MqttTopicFilter.Matches(filter, topic));
}
