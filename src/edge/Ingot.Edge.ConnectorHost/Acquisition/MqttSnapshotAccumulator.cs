using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
///     保留 MQTT 各订阅主题的最近一份报文，并生成一个合并快照。
///     主题级快照用于支持点位的显式 Topic 绑定；合并快照用于未绑定主题的点位。
/// </summary>
internal sealed class MqttSnapshotAccumulator
{
    private readonly IReadOnlyList<MqttTopicSubscription> _subscriptions;
    private readonly Dictionary<string, JsonObject> _topicSnapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> _topicUpdatedAt = new(StringComparer.Ordinal);

    public MqttSnapshotAccumulator(IReadOnlyList<MqttTopicSubscription> subscriptions)
        => _subscriptions = subscriptions;

    public MqttSnapshotSet Add(
        string actualTopic,
        ReadOnlyMemory<byte> payload,
        DateTimeOffset? receivedAt = null,
        int maxAgeSeconds = 30,
        int maxSkewSeconds = 5)
        => Add(
            actualTopic,
            new ReadOnlySequence<byte>(payload),
            receivedAt,
            maxAgeSeconds,
            maxSkewSeconds);

    public MqttSnapshotSet Add(
        string actualTopic,
        ReadOnlySequence<byte> payload,
        DateTimeOffset? receivedAt = null,
        int maxAgeSeconds = 30,
        int maxSkewSeconds = 5)
    {
        var observedAt = receivedAt ?? DateTimeOffset.UtcNow;
        using var document = JsonDocument.Parse(payload);
        foreach (var subscription in _subscriptions)
        {
            if (!MatchesTopicFilter(subscription.Topic, actualTopic))
                continue;

            if (!TryResolve(document.RootElement, subscription.PayloadRoot, out var payloadRoot) ||
                payloadRoot.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException(
                    $"MQTT 主题 {actualTopic} 的报文根路径不是对象：{subscription.PayloadRoot ?? "(报文根)"}。");
            }

            var incoming = JsonNode.Parse(payloadRoot.GetRawText()) as JsonObject
                ?? throw new InvalidDataException($"MQTT 主题 {actualTopic} 的报文根不是 JSON 对象。");
            if (!_topicSnapshots.TryGetValue(subscription.Topic, out var current))
            {
                current = new JsonObject();
                _topicSnapshots[subscription.Topic] = current;
            }

            Merge(current, incoming);
            _topicUpdatedAt[subscription.Topic] = observedAt;
        }

        return BuildSnapshot(observedAt, maxAgeSeconds, maxSkewSeconds);
    }

    internal static bool MatchesTopicFilter(string filter, string topic)
    {
        var filterLevels = filter.Split('/', StringSplitOptions.None);
        var topicLevels = topic.Split('/', StringSplitOptions.None);
        for (var index = 0; index < filterLevels.Length; index++)
        {
            var filterLevel = filterLevels[index];
            if (filterLevel == "#")
                return index == filterLevels.Length - 1;
            if (index >= topicLevels.Length)
                return false;
            if (filterLevel != "+" && !string.Equals(filterLevel, topicLevels[index], StringComparison.Ordinal))
                return false;
        }

        return filterLevels.Length == topicLevels.Length;
    }

    private MqttSnapshotSet BuildSnapshot(
        DateTimeOffset observedAt,
        int maxAgeSeconds,
        int maxSkewSeconds)
    {
        var aggregate = new JsonObject();
        foreach (var subscription in _subscriptions)
        {
            if (_topicSnapshots.TryGetValue(subscription.Topic, out var topicSnapshot))
                Merge(aggregate, topicSnapshot);
        }

        var aggregateDocument = JsonDocument.Parse(aggregate.ToJsonString());
        var topicDocuments = _topicSnapshots.ToDictionary(
            item => item.Key,
            item => JsonDocument.Parse(item.Value.ToJsonString()),
            StringComparer.Ordinal);
        var timestamps = _subscriptions
            .Select(item => _topicUpdatedAt.GetValueOrDefault(item.Topic))
            .Where(static item => item != default)
            .ToArray();
        var complete = _subscriptions.All(item => _topicUpdatedAt.ContainsKey(item.Topic));
        var newest = timestamps.Length == 0 ? observedAt : timestamps.Max();
        var oldest = timestamps.Length == 0 ? observedAt : timestamps.Min();
        var coherent = complete &&
                       newest >= oldest &&
                       newest - oldest <= TimeSpan.FromSeconds(Math.Max(0, maxSkewSeconds)) &&
                       observedAt - oldest <= TimeSpan.FromSeconds(Math.Max(1, maxAgeSeconds));
        var fingerprint = Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(aggregate.ToJsonString())));
        return new MqttSnapshotSet(
            aggregateDocument,
            topicDocuments,
            complete,
            coherent,
            fingerprint,
            newest,
            oldest);
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var property in source)
        {
            if (property.Value is JsonObject sourceObject &&
                target[property.Key] is JsonObject targetObject)
            {
                Merge(targetObject, sourceObject);
            }
            else
            {
                target[property.Key] = property.Value?.DeepClone();
            }
        }
    }

    private static bool TryResolve(JsonElement root, string? path, out JsonElement value)
    {
        value = root;
        if (string.IsNullOrWhiteSpace(path) || path.Trim() == ".")
            return true;

        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                return false;
        }

        return true;
    }
}

internal sealed class MqttSnapshotSet : IDisposable
{
    public MqttSnapshotSet(
        JsonDocument aggregate,
        IReadOnlyDictionary<string, JsonDocument> topicDocuments,
        bool complete,
        bool coherent,
        string fingerprint,
        DateTimeOffset newestTopicAt,
        DateTimeOffset oldestTopicAt)
    {
        Aggregate = aggregate;
        TopicDocuments = topicDocuments;
        IsComplete = complete;
        IsCoherent = coherent;
        Fingerprint = fingerprint;
        NewestTopicAt = newestTopicAt;
        OldestTopicAt = oldestTopicAt;
    }

    public JsonDocument Aggregate { get; }
    public bool IsComplete { get; }
    public bool IsCoherent { get; }
    public string Fingerprint { get; }
    public DateTimeOffset NewestTopicAt { get; }
    public DateTimeOffset OldestTopicAt { get; }

    public IReadOnlyDictionary<string, JsonElement> TopicSnapshots
        => TopicDocuments.ToDictionary(item => item.Key, item => item.Value.RootElement, StringComparer.Ordinal);

    private IReadOnlyDictionary<string, JsonDocument> TopicDocuments { get; }

    public void Dispose()
    {
        Aggregate.Dispose();
        foreach (var document in TopicDocuments.Values)
            document.Dispose();
    }
}
