// Parses bounded MTConnect XML without allowing DTDs, external entities, or unbounded document trees.
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition.Probers;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal sealed record MtConnectDataItem(
    string Id,
    string Name,
    string Category,
    string Type,
    string? SubType,
    string? Units,
    string? ComponentId,
    string ComponentPath)
{
    public bool IsSupportedScalar
        => Category is "SAMPLE" or "EVENT" &&
           !MtConnectXmlReader.CompositeTypePattern.IsMatch(Type);
}

internal sealed record MtConnectObservation(
    object? Value,
    string? Quality,
    DateTimeOffset? Timestamp,
    string? Sequence);

internal sealed record MtConnectProbeDocument(
    string? AgentVersion,
    string? DeviceName,
    string? DeviceUuid,
    IReadOnlyDictionary<string, MtConnectDataItem> DataItems);

internal static class MtConnectXmlReader
{
    internal const int MaximumPayloadBytes = 16 * 1024 * 1024;
    private const int MaximumXmlDepth = 64;
    private const int MaximumXmlNodes = 100_000;
    private const int MaximumDataItems = AcquisitionProbeSupport.MaximumPoints;

    // Reject explicitly aggregate/structured models even if an agent serializes them as flat text.
    internal static readonly System.Text.RegularExpressions.Regex CompositeTypePattern = new(
        "(?:VECTOR|MATRIX|ARRAY|DATA[_ ]?SET|STRUCT|COMPLEX|SIX[_ ]?DOF)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.CultureInvariant |
        System.Text.RegularExpressions.RegexOptions.Compiled);

    public static async Task<MtConnectProbeDocument> ReadProbeAsync(
        HttpContent content,
        string deviceNameOrUuid,
        int maximumPayloadBytes,
        CancellationToken ct)
    {
        var document = await ReadDocumentAsync(content, maximumPayloadBytes, ct).ConfigureAwait(false);
        var root = document.Root;
        if (root is null || root.Name.LocalName != "MTConnectDevices")
            throw new InvalidDataException("MTConnect /probe 响应根节点必须是 MTConnectDevices。");

        var device = root.DescendantsAndSelf()
            .FirstOrDefault(element => element.Name.LocalName == "Device" &&
                (string.Equals((string?)element.Attribute("name"), deviceNameOrUuid, StringComparison.Ordinal) ||
                 string.Equals((string?)element.Attribute("uuid"), deviceNameOrUuid, StringComparison.Ordinal)));
        if (device is null)
            throw new InvalidDataException($"MTConnect /probe 响应中找不到所选设备 {deviceNameOrUuid}。");

        var dataItems = new Dictionary<string, MtConnectDataItem>(StringComparer.Ordinal);
        foreach (var element in device.Descendants().Where(item => item.Name.LocalName == "DataItem"))
        {
            var id = ((string?)element.Attribute("id"))?.Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("MTConnect /probe 中的 DataItem 缺少 id。");
            var category = ((string?)element.Attribute("category"))?.Trim().ToUpperInvariant() ?? "";
            var type = ((string?)element.Attribute("type"))?.Trim().ToUpperInvariant() ?? "";
            if (category.Length == 0 || type.Length == 0)
                throw new InvalidDataException($"MTConnect DataItem {id} 缺少 category 或 type。");

            var ancestors = element.Ancestors()
                .TakeWhile(ancestor => !ReferenceEquals(ancestor, device))
                .Where(ancestor => ancestor.Name.LocalName is not ("DataItems" or "DataItem") &&
                                   ancestor.Attribute("id") is not null &&
                                   ancestor.Attribute("name") is not null)
                .Reverse()
                .ToArray();
            var componentPath = string.Join('/', ancestors
                .Select(ancestor => ((string?)ancestor.Attribute("name"))?.Trim())
                .Where(static name => !string.IsNullOrWhiteSpace(name)));
            var name = ((string?)element.Attribute("name"))?.Trim();
            var displayName = string.IsNullOrWhiteSpace(name) ? id : name;
            if (!string.IsNullOrWhiteSpace(componentPath)) displayName = $"{componentPath}/{displayName}";
            var item = new MtConnectDataItem(
                id,
                displayName,
                category,
                type,
                NormalizeOptional((string?)element.Attribute("subType")),
                NormalizeOptional((string?)element.Attribute("units")),
                ancestors.LastOrDefault() is { } component ? NormalizeOptional((string?)component.Attribute("id")) : null,
                componentPath);
            if (!dataItems.TryAdd(id, item))
                throw new InvalidDataException($"所选 MTConnect 设备中 DataItem id 重复：{id}。");
            if (dataItems.Count > MaximumDataItems)
                throw new InvalidDataException($"MTConnect /probe 点位数量超过 {MaximumDataItems} 上限。");
        }

        var agentVersion = root.DescendantsAndSelf()
            .FirstOrDefault(element => element.Name.LocalName == "Header")?
            .Attribute("version")?.Value;
        return new MtConnectProbeDocument(
            agentVersion,
            NormalizeOptional((string?)device.Attribute("name")),
            NormalizeOptional((string?)device.Attribute("uuid")),
            dataItems);
    }

    public static async Task<IReadOnlyDictionary<string, MtConnectObservation>> ReadCurrentAsync(
        HttpContent content,
        IReadOnlyDictionary<string, MtConnectDataItem> catalog,
        IReadOnlySet<string> configuredIds,
        int maximumPayloadBytes,
        CancellationToken ct)
    {
        var document = await ReadDocumentAsync(content, maximumPayloadBytes, ct).ConfigureAwait(false);
        var root = document.Root;
        if (root is null || root.Name.LocalName != "MTConnectStreams")
            throw new InvalidDataException("MTConnect /current 响应根节点必须是 MTConnectStreams。");

        var result = new Dictionary<string, MtConnectObservation>(StringComparer.Ordinal);
        foreach (var element in root.Descendants().Where(item => item.Attribute("dataItemId") is not null))
        {
            var id = ((string?)element.Attribute("dataItemId"))?.Trim();
            if (string.IsNullOrEmpty(id) || !configuredIds.Contains(id)) continue;
            if (!catalog.TryGetValue(id, out var dataItem))
                throw new InvalidDataException($"MTConnect /current 返回未在 /probe 设备目录中找到的 DataItem：{id}。");
            if (!dataItem.IsSupportedScalar)
                throw new InvalidDataException(
                    $"MTConnect DataItem {id} ({dataItem.Category}/{dataItem.Type}) 暂不支持；只支持标量 SAMPLE 和 EVENT。条件与复合值不能映射到普通采样值。");
            if (element.Elements().Any())
                throw new InvalidDataException($"MTConnect DataItem {id} 返回了复合 XML 值，当前只支持标量观察值。");

            var text = element.Value.Trim();
            var quality = string.Equals(text, "UNAVAILABLE", StringComparison.OrdinalIgnoreCase)
                ? "UNAVAILABLE"
                : null;
            object? value = quality is null ? text : null;
            var timestamp = ParseTimestamp((string?)element.Attribute("timestamp"), id);
            var sequence = (string?)element.Attribute("sequence");
            if (!string.IsNullOrWhiteSpace(sequence) &&
                !ulong.TryParse(sequence, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw new InvalidDataException($"MTConnect DataItem {id} 的观察 sequence 无效。");
            result[id] = new MtConnectObservation(value, quality, timestamp, sequence);
        }
        return result;
    }

    private static async Task<XDocument> ReadDocumentAsync(
        HttpContent content,
        int maximumPayloadBytes,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (maximumPayloadBytes is <= 0 or > MaximumPayloadBytes)
            throw new InvalidOperationException($"MTConnect XML 响应上限必须在 1-{MaximumPayloadBytes} 字节内。");
        if (content.Headers.ContentLength is { } declaredLength && declaredLength > maximumPayloadBytes)
            throw new InvalidDataException(
                $"MTConnect XML 响应声明长度 {declaredLength} 字节，超过 {maximumPayloadBytes} 字节上限。");

        await using var source = await content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var buffer = new MemoryStream();
        var rented = System.Buffers.ArrayPool<byte>.Shared.Rent(32 * 1024);
        try
        {
            var total = 0;
            while (true)
            {
                var remaining = maximumPayloadBytes - total;
                var read = await source.ReadAsync(
                    rented.AsMemory(0, Math.Min(rented.Length, remaining + 1)), ct).ConfigureAwait(false);
                if (read == 0) break;
                total += read;
                if (total > maximumPayloadBytes)
                    throw new InvalidDataException($"MTConnect XML 响应超过 {maximumPayloadBytes} 字节上限。");
                await buffer.WriteAsync(rented.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }

        try
        {
            buffer.Position = 0;
            using (var validator = XmlReader.Create(buffer, CreateSettings(maximumPayloadBytes)))
            {
                var nodeCount = 0;
                while (validator.Read())
                {
                    if (validator.Depth > MaximumXmlDepth)
                        throw new InvalidDataException($"MTConnect XML 嵌套深度超过 {MaximumXmlDepth} 层。");
                    if (++nodeCount > MaximumXmlNodes)
                        throw new InvalidDataException($"MTConnect XML 节点数量超过 {MaximumXmlNodes} 上限。");
                }
            }
            buffer.Position = 0;
            using var reader = XmlReader.Create(buffer, CreateSettings(maximumPayloadBytes));
            return XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("MTConnect 响应不是有效或受支持的 XML 文档。", exception);
        }
    }

    private static XmlReaderSettings CreateSettings(int maximumPayloadBytes)
        => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = maximumPayloadBytes,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            CloseInput = false
        };

    private static DateTimeOffset? ParseTimestamp(string? text, string id)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            throw new InvalidDataException($"MTConnect DataItem {id} 的观察时间戳无效。");
        return timestamp;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
