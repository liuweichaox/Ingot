using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

/// <summary>
///     把多个 MQTT 主题上收到的报文合并成一份等价的设备快照。
///
///     以前 MQTT 采集器对所有订阅主题走同一套映射，也就是说**每条报文都必须是包含
///     全部必填字段的完整快照**：网关把温度发在一个主题、把压力发在另一个主题时无法配置，
///     而界面却允许添加多个主题，误配之后没有任何提示。
///
///     现在每个点位可以绑定来源主题（留空表示任意主题）。收到报文时只取属于该主题的字段，
///     存进按路径索引的槽位；全部必需槽位都有值之后，再拼装出一份与单主题快照结构完全一致的
///     JSON 文档，交给 <see cref="HttpPollingSnapshotMapper"/> 走原有的换算与校验逻辑——
///     映射实现仍然只有一份。
///
///     本类刻意不引用 MQTTnet，只处理"报文内容 → 合并快照"这一层，因此可以被完整测试。
/// </summary>
public sealed class MqttSnapshotAssembler
{
    /// <summary>一个需要从设备报文里取到的路径。</summary>
    /// <param name="Path">相对于（去掉信封之后的）报文根的点号路径。</param>
    /// <param name="Topic">只接受来自该订阅过滤器的报文；null 表示任意主题。</param>
    /// <param name="Required">缺失时不产生采样。</param>
    /// <param name="IsValue">是否是工艺变量点位——只有工艺变量到达才触发采样。</param>
    public sealed record Slot(string Path, string? Topic, bool Required, bool IsValue);

    private readonly Slot[] _slots;
    private readonly TimeSpan _maxAge;
    private readonly Dictionary<Slot, (string Json, DateTimeOffset At)> _values = [];

    public MqttSnapshotAssembler(IReadOnlyList<Slot> slots, int snapshotMaxAgeSeconds)
    {
        _slots = slots.ToArray();
        _maxAge = snapshotMaxAgeSeconds > 0 ? TimeSpan.FromSeconds(snapshotMaxAgeSeconds) : TimeSpan.Zero;
    }

    /// <summary>从采集配置推导槽位。路径与 <see cref="HttpPollingSnapshotMapper"/> 解析的路径严格一致。</summary>
    public static IReadOnlyList<Slot> SlotsFor(AcquisitionProfile profile)
    {
        var slots = new List<Slot>();
        void Add(string? path, string? topic, bool required, bool isValue)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var normalized = path.Trim();
            var normalizedTopic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
            var existingIndex = slots.FindIndex(item =>
                string.Equals(item.Path, normalized, StringComparison.Ordinal) &&
                string.Equals(item.Topic, normalizedTopic, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                var existing = slots[existingIndex];
                slots[existingIndex] = existing with
                {
                    Required = existing.Required || required,
                    IsValue = existing.IsValue || isValue
                };
                return;
            }
            slots.Add(new Slot(normalized, normalizedTopic, required, isValue));
        }

        foreach (var mapping in profile.ValueMappings)
            Add(mapping.SourcePath, mapping.Topic, mapping.Required, isValue: true);
        foreach (var mapping in profile.ContextMappings)
            Add(mapping.SourcePath, mapping.Topic, mapping.Required, isValue: false);
        if (profile.TimestampMode == "source")
            Add(profile.TimestampPath, null, required: true, isValue: false);
        Add(profile.SequencePath, null, required: false, isValue: false);
        if (profile.Recipe is { } recipe)
        {
            Add(recipe.IdPath, null, required: true, isValue: false);
            Add(recipe.VersionPath, null, required: true, isValue: false);
            Add(recipe.NamePath, null, required: false, isValue: false);
            foreach (var parameter in recipe.ParameterMappings)
                Add(
                    Combine(recipe.ParametersPath, parameter.SourcePath),
                    parameter.Topic,
                    parameter.Required,
                    isValue: false);
            // 配方参数集合本身必须是对象。参数逐个取值后按同样的路径重建，
            // 因此这里不再单独保留整个对象，避免与逐参数槽位互相覆盖。
        }

        return slots;
    }

    /// <summary>把配方参数集合路径与参数自身路径拼成一条绝对路径；"." 表示报文根。</summary>
    public static string Combine(string? parametersPath, string fieldPath)
    {
        var root = parametersPath?.Trim();
        return string.IsNullOrEmpty(root) || root == "." ? fieldPath.Trim() : $"{root}.{fieldPath.Trim()}";
    }

    /// <summary>
    ///     摄入一条报文。返回该报文是否带来了至少一个工艺变量——
    ///     只携带上下文的主题会更新状态但不触发采样，避免采样率被无关主题放大。
    /// </summary>
    public bool Ingest(string topic, JsonElement payload, DateTimeOffset receivedAt)
    {
        var carriedValue = false;
        foreach (var slot in _slots)
        {
            if (slot.Topic is not null && !MqttTopicFilter.Matches(slot.Topic, topic)) continue;
            if (!TryResolve(payload, slot.Path, out var value)) continue;
            if (value.ValueKind is JsonValueKind.Undefined) continue;
            _values[slot] = (value.GetRawText(), receivedAt);
            if (slot.IsValue) carriedValue = true;
        }

        return carriedValue;
    }

    /// <summary>
    ///     拼装合并快照。全部必需槽位都有值且未超过陈旧上限时返回 true。
    ///     <paramref name="missing"/> 说明缺哪一条，便于把"还在等哪个主题"直接显示给工程师。
    /// </summary>
    public bool TryBuildSnapshot(DateTimeOffset now, out JsonDocument? snapshot, out string? missing)
    {
        snapshot = null;
        missing = null;
        foreach (var slot in _slots.Where(static item => item.Required))
        {
            if (!_values.TryGetValue(slot, out var entry))
            {
                missing = slot.Topic is null
                    ? $"尚未收到必填字段 {slot.Path}"
                    : $"尚未收到主题 {slot.Topic} 上的必填字段 {slot.Path}";
                return false;
            }

            if (_maxAge > TimeSpan.Zero && now - entry.At > _maxAge)
            {
                missing = $"字段 {slot.Path} 的值已超过 {_maxAge.TotalSeconds:0} 秒未更新";
                return false;
            }
        }

        var tree = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var slot in _slots)
        {
            if (!_values.TryGetValue(slot, out var entry)) continue;
            if (_maxAge > TimeSpan.Zero && now - entry.At > _maxAge) continue;
            Insert(tree, slot.Path, entry.Json);
        }

        snapshot = JsonDocument.Parse(Render(tree));
        return true;
    }

    /// <summary>
    ///     为显式绑定主题的映射生成各自的快照。即使两个主题使用相同字段名，映射器也能
    ///     按配置的主题过滤器取到正确值，而不会被聚合快照中的同名字段覆盖。
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement> BuildTopicSnapshots(DateTimeOffset now)
    {
        var snapshots = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var group in _slots
                     .Where(static slot => slot.Topic is not null)
                     .GroupBy(static slot => slot.Topic!, StringComparer.Ordinal))
        {
            var tree = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var slot in group)
            {
                if (!_values.TryGetValue(slot, out var entry)) continue;
                if (_maxAge > TimeSpan.Zero && now - entry.At > _maxAge) continue;
                Insert(tree, slot.Path, entry.Json);
            }

            using var document = JsonDocument.Parse(Render(tree));
            snapshots[group.Key] = document.RootElement.Clone();
        }

        return snapshots;
    }

    /// <summary>对完整的主题隔离快照生成稳定指纹，用于抑制设备重复投递的同一份状态。</summary>
    public static string Fingerprint(
        JsonDocument aggregate,
        IReadOnlyDictionary<string, JsonElement> topicSnapshots)
    {
        var builder = new StringBuilder(aggregate.RootElement.GetRawText());
        foreach (var item in topicSnapshots.OrderBy(static item => item.Key, StringComparer.Ordinal))
            builder.Append('\n').Append(item.Key).Append('\n').Append(item.Value.GetRawText());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>把点号路径写进嵌套字典。前缀冲突（同时配置 a 与 a.b）会明确报错而不是静默丢值。</summary>
    private static void Insert(Dictionary<string, object?> tree, string path, string rawJson)
    {
        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0) return;
        var current = tree;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!current.TryGetValue(segments[index], out var next) || next is null)
            {
                var child = new Dictionary<string, object?>(StringComparer.Ordinal);
                current[segments[index]] = child;
                current = child;
                continue;
            }

            if (next is not Dictionary<string, object?> existing)
                throw new InvalidOperationException(
                    $"合并快照时路径冲突：{path} 与另一个已配置字段互为前缀，请调整点位路径。");
            current = existing;
        }

        current[segments[^1]] = rawJson;
    }

    /// <summary>把嵌套字典渲染成 JSON。叶子已经是原始 JSON 文本，直接写入以保留类型。</summary>
    private static string Render(Dictionary<string, object?> tree)
    {
        var builder = new StringBuilder();
        Write(builder, tree);
        return builder.ToString();
    }

    private static void Write(StringBuilder builder, Dictionary<string, object?> tree)
    {
        builder.Append('{');
        var first = true;
        foreach (var (key, value) in tree)
        {
            if (!first) builder.Append(',');
            first = false;
            builder.Append(JsonSerializer.Serialize(key)).Append(':');
            if (value is Dictionary<string, object?> child) Write(builder, child);
            else builder.Append((string?)value ?? "null");
        }

        builder.Append('}');
    }

    private static bool TryResolve(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value))
                return false;
        }

        return true;
    }

    /// <summary>按订阅配置剥掉报文信封，返回真正承载数据的对象。</summary>
    public static JsonElement Unwrap(JsonElement payload, string? payloadRoot)
    {
        if (string.IsNullOrWhiteSpace(payloadRoot) || payloadRoot.Trim() == ".") return payload;
        return TryResolve(payload, payloadRoot, out var value)
            ? value
            : throw new InvalidDataException($"报文中找不到配置的根路径：{payloadRoot}。");
    }

    /// <summary>找出与具体主题匹配的订阅项，用于确定该报文的信封路径。</summary>
    public static MqttTopicSubscription? SubscriptionFor(
        IReadOnlyList<MqttTopicSubscription> subscriptions,
        string topic)
        => subscriptions.FirstOrDefault(item => MqttTopicFilter.Matches(item.Topic, topic));
}
