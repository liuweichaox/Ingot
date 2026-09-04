// 采集探查共用辅助：点位展平、映射预览、发现分页与证据定位。
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.Acquisition;
using Opc.Ua;
using Opc.Ua.Client;

namespace Ingot.Edge.ConnectorHost.Acquisition.Probers;

internal static class AcquisitionProbeSupport
{
    public const int MaximumPoints = 20_000;

    public static bool IsDiscoveryProbe(AcquisitionDeployment deployment)
        => deployment.Task.ValueMappings.Any(item =>
            item.SourcePath == "__probe_only__" && !item.Required);

    public static bool UsesCredentials(MqttConnection connection)
        => !string.IsNullOrWhiteSpace(connection.Username) ||
           !string.IsNullOrWhiteSpace(connection.PasswordSecretRef) ||
           !string.IsNullOrWhiteSpace(connection.ClientCertificatePath) ||
           !string.IsNullOrWhiteSpace(connection.ClientCertificatePasswordSecretRef);

    public static bool UsesCredentials(OpcUaConnection connection)
        => connection.AuthenticationType != "anonymous" ||
           !string.IsNullOrWhiteSpace(connection.Username) ||
           !string.IsNullOrWhiteSpace(connection.PasswordSecretRef) ||
           !string.IsNullOrWhiteSpace(connection.ClientCertificatePath) ||
           !string.IsNullOrWhiteSpace(connection.ClientCertificatePasswordSecretRef);

    public static bool ValidateProtocolMapping(
        AcquisitionDeployment deployment,
        IReadOnlyDictionary<string, object?> values)
    {
        if (IsDiscoveryProbe(deployment))
            return true;

        try
        {
            var occurredAt = DateTimeOffset.UtcNow;
            if (deployment.Task.TimestampMode == "source" &&
                !string.IsNullOrWhiteSpace(deployment.Task.TimestampPath))
            {
                if (!values.TryGetValue(deployment.Task.TimestampPath, out var rawTimestamp) ||
                    rawTimestamp is null)
                {
                    throw new InvalidDataException(
                        $"配置的时间来源没有读到值：{deployment.Task.TimestampPath}。");
                }
                occurredAt = AcquisitionTimestampParser.Parse(
                    rawTimestamp,
                    deployment.Task.TimestampEncoding,
                    deployment.Task.TimestampPath,
                    occurredAt,
                    deployment.Task.Execution.MaximumFutureTimestampSkewMs);
            }

            ProtocolAcquisitionSnapshotMapper.Map(
                deployment,
                values,
                deployment.Task.Source,
                previousProcessSpecificationIdentity: null,
                occurredAt: occurredAt);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or FormatException or InvalidCastException or OverflowException)
        {
            return false;
        }
    }

#pragma warning disable CS0618
    public static void BrowseOpcNodes(
        Opc.Ua.Client.ISession session,
        NodeId parent,
        string parentName,
        int depth,
        IDictionary<string, object?> values,
        ICollection<AcquisitionProbePoint> points,
        ISet<NodeId>? visited = null)
    {
        visited ??= new HashSet<NodeId>();
        if (depth >= 32 || points.Count >= MaximumPoints || !visited.Add(parent))
            return;
        session.Browse(
            null,
            null,
            parent,
            0u,
            BrowseDirection.Forward,
            ReferenceTypeIds.HierarchicalReferences,
            true,
            (uint)(NodeClass.Object | NodeClass.Variable),
            out var continuationPoint,
            out var references);
        while (true)
        {
            foreach (var reference in references)
            {
                if (points.Count >= MaximumPoints)
                    break;
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris);
                if (nodeId is null)
                    continue;
                var name = string.IsNullOrWhiteSpace(parentName)
                    ? reference.DisplayName.Text
                    : $"{parentName}/{reference.DisplayName.Text}";
                if (reference.NodeClass == NodeClass.Variable)
                {
                    var path = nodeId.ToString();
                    points.Add(new AcquisitionProbePoint
                    {
                        Path = path,
                        Name = name,
                        Kind = "opc-variable",
                        DataType = "unknown"
                    });
                }
                else
                {
                    BrowseOpcNodes(session, nodeId, name, depth + 1, values, points, visited);
                }
            }
            if (points.Count >= MaximumPoints || continuationPoint is null || continuationPoint.Length == 0)
                break;
            session.BrowseNext(
                null,
                false,
                continuationPoint,
                out continuationPoint,
                out references);
        }
    }
#pragma warning restore CS0618

    public static void FlattenJson(
        JsonElement element,
        string path,
        IDictionary<string, object?> values,
        ICollection<AcquisitionProbePoint> points,
        string? topic = null)
    {
        if (points.Count >= MaximumPoints)
            return;
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                FlattenJson(property.Value, Join(path, property.Name), values, points, topic);
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
                FlattenJson(item, $"{path}[{index++}]", values, points, topic);
            return;
        }
        var raw = JsonValue(element);
        values[path] = raw;
        AddPoint(points, path, path, "json-field", raw, topic);
    }

    public static IReadOnlyList<AcquisitionMappingPreview> BuildPreviews(
        AcquisitionDeployment deployment,
        IReadOnlyDictionary<string, object?> raw,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> topicValues)
    {
        var definitions = deployment.DataModel.Acquisition.DataItems
            .ToDictionary(item => item.Code, StringComparer.Ordinal);
        return deployment.Task.ValueMappings.Select(mapping =>
        {
            var previewRaw = raw;
            object? value;
            if (deployment.Task.Protocol == AcquisitionProtocols.Mqtt &&
                !string.IsNullOrWhiteSpace(mapping.Topic))
            {
                previewRaw = topicValues.TryGetValue(mapping.Topic, out var isolated)
                    ? isolated
                    : new Dictionary<string, object?>(StringComparer.Ordinal);
                previewRaw.TryGetValue(mapping.SourcePath, out value);
            }
            else
            {
                raw.TryGetValue(mapping.SourcePath, out value);
            }
            var sourceFound = value is not null;
            string? converted = null;
            string? error = null;
            try
            {
                var resolved = AcquisitionValuePolicy.Resolve(
                    previewRaw,
                    mapping,
                    definitions[mapping.DataItemCode].DataType);
                converted = Format(resolved);
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }
            var acceptedWithoutSource = mapping.MissingValueBehavior == "use-default" ||
                                        (!mapping.Required && mapping.MissingValueBehavior is "inherit" or "omit");
            return new AcquisitionMappingPreview
            {
                DataItemCode = mapping.DataItemCode,
                SourcePath = mapping.SourcePath,
                Found = sourceFound,
                Accepted = error is null && (sourceFound || acceptedWithoutSource),
                RawValue = Format(value),
                ConvertedValue = converted,
                DataType = value?.GetType().Name,
                SourceUnit = mapping.SourceUnit,
                TargetUnit = definitions.GetValueOrDefault(mapping.DataItemCode)?.Unit,
                Error = error
            };
        }).ToArray();
    }

    public static ProbeSnapshot FromRegisterValues(
        Dictionary<string, object?> values,
        string kind,
        bool mappingsValidated = true)
        => new(
            values,
            values.Select(item => new AcquisitionProbePoint
            {
                Path = item.Key,
                Name = item.Key,
                Kind = kind,
                DataType = item.Value?.GetType().Name ?? "null",
                RawValue = Format(item.Value)
            }).ToArray(),
            mappingsValidated);

    public static void AddPoint(
        ICollection<AcquisitionProbePoint> points,
        string path,
        string name,
        string kind,
        object? value,
        string? topic = null)
    {
        if (string.IsNullOrWhiteSpace(path) || points.Count >= MaximumPoints)
            return;
        points.Add(new AcquisitionProbePoint
        {
            Path = path,
            Name = name,
            Kind = kind,
            DataType = value?.GetType().Name ?? "null",
            RawValue = Format(value),
            Topic = topic
        });
    }

    public static void AddOpcPoint(
        ICollection<AcquisitionProbePoint> points,
        string path,
        string name,
        DataValue value)
    {
        if (string.IsNullOrWhiteSpace(path) || points.Count >= MaximumPoints)
            return;
        points.Add(new AcquisitionProbePoint
        {
            Path = path,
            Name = name,
            Kind = "opc-variable",
            DataType = value.Value?.GetType().Name ?? "null",
            RawValue = Format(value.Value),
            Quality = value.StatusCode.ToString(),
            SourceTimestamp = value.SourceTimestamp == DateTime.MinValue
                ? null
                : new DateTimeOffset(DateTime.SpecifyKind(value.SourceTimestamp, DateTimeKind.Utc))
        });
    }

    public static DiscoveryPage ApplyDiscoveryQuery(
        IReadOnlyList<AcquisitionProbePoint> points,
        SourceDiscoveryQuery query)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 500);
        var search = query.Search?.Trim();
        var root = query.RootPath?.Trim();
        var kinds = query.Kinds.Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var namespaces = query.Namespaces.Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item.Trim().TrimStart('n', 's', '='))
            .ToHashSet(StringComparer.Ordinal);
        var pathPattern = CompileDiscoveryPattern(query.PathPattern, "点位路径正则");
        var namePattern = CompileDiscoveryPattern(query.NamePattern, "点位名称正则");
        var cursor = DecodeCursor(query.Cursor);

        var filtered = points
            .Where(point => string.IsNullOrEmpty(search) ||
                            point.Path.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            point.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                            (point.Topic?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .Where(point => string.IsNullOrEmpty(root) ||
                            point.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
                            point.Name.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            .Where(point => kinds.Count == 0 || kinds.Contains(point.Kind))
            .Where(point => namespaces.Count == 0 || namespaces.Contains(NodeNamespace(point.Path)))
            .Where(point => pathPattern is null || pathPattern.IsMatch(point.Path))
            .Where(point => namePattern is null || namePattern.IsMatch(point.Name))
            .OrderBy(static point => PointKey(point), StringComparer.Ordinal)
            .Where(point => cursor is null || string.CompareOrdinal(PointKey(point), cursor) > 0)
            .Take(pageSize + 1)
            .ToArray();
        var hasMore = filtered.Length > pageSize;
        var page = filtered.Take(pageSize).ToArray();
        return new DiscoveryPage(
            page,
            hasMore && page.Length > 0 ? EncodeCursor(PointKey(page[^1])) : null);
    }

    public static IEnumerable<string> MappedPaths(AcquisitionDeployment deployment)
    {
        var task = deployment.Task;
        var paths = task.ValueMappings.Select(item => item.SourcePath)
            .Concat(task.ValueMappings.Select(item => item.QualityPath))
            .Concat(task.ContextMappings.Select(item => item.SourcePath));
        if (task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath))
            paths = paths.Append(task.TimestampPath);
        if (!string.IsNullOrWhiteSpace(task.SequencePath)) paths = paths.Append(task.SequencePath);
        if (task.ProcessSpecification is { } specification)
            paths = paths.Append(specification.IdPath).Append(specification.VersionPath)
                .Append(specification.NamePath)
                .Concat(specification.ParameterMappings.Select(item => item.SourcePath))
                .Concat(specification.ParameterMappings.Select(item => item.QualityPath));
        return paths.Where(item => !string.IsNullOrWhiteSpace(item) &&
                                   item != "__probe_only__" && item != "$status")
            .Select(static item => item!)
            .Distinct(StringComparer.Ordinal);
    }

    public static IEnumerable<PublicationEvidencePath> PublicationEvidencePaths(AcquisitionDeployment deployment)
    {
        var task = deployment.Task;
        var paths = task.ValueMappings.SelectMany(static item => new[]
            {
                new PublicationEvidencePath(item.SourcePath, item.Topic),
                new PublicationEvidencePath(item.QualityPath, item.Topic)
            })
            .Concat(task.ContextMappings.Select(static item =>
                new PublicationEvidencePath(item.SourcePath, item.Topic)));
        if (task.TimestampMode == "source" && !string.IsNullOrWhiteSpace(task.TimestampPath))
            paths = paths.Append(new PublicationEvidencePath(task.TimestampPath, null));
        if (!string.IsNullOrWhiteSpace(task.SequencePath))
            paths = paths.Append(new PublicationEvidencePath(task.SequencePath, null));
        if (task.ProcessSpecification is { } specification)
        {
            paths = paths.Append(new PublicationEvidencePath(specification.IdPath, null))
                .Append(new PublicationEvidencePath(specification.VersionPath, null))
                .Append(new PublicationEvidencePath(specification.NamePath, null))
                .Concat(specification.ParameterMappings.Select(item =>
                    new PublicationEvidencePath(
                        MqttSnapshotAssembler.Combine(specification.ParametersPath, item.SourcePath),
                        item.Topic)))
                .Concat(specification.ParameterMappings
                    .Where(static item => !string.IsNullOrWhiteSpace(item.QualityPath))
                    .Select(item => new PublicationEvidencePath(
                        MqttSnapshotAssembler.Combine(specification.ParametersPath, item.QualityPath!),
                        item.Topic)));
        }
        return paths.Where(static item => !string.IsNullOrWhiteSpace(item.Path) && item.Path != "$status")
            .Distinct();
    }

    public static bool EvidenceLocated(
        string protocol,
        PublicationEvidencePath evidence,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> topicValues)
    {
        if (protocol == AcquisitionProtocols.Mqtt && !string.IsNullOrWhiteSpace(evidence.Topic))
            return topicValues.TryGetValue(evidence.Topic, out var isolated) &&
                   isolated.TryGetValue(evidence.Path!, out var topicValue) && topicValue is not null;
        return values.TryGetValue(evidence.Path!, out var value) && value is not null;
    }

    public static string? Format(object? value)
        => value is null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static Regex? CompileDiscoveryPattern(string? pattern, string label)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return null;
        try
        {
            return new Regex(
                pattern.Trim(),
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException($"{label}无效：{exception.Message}", exception);
        }
    }

    private static string PointKey(AcquisitionProbePoint point) => $"{point.Topic}\u001f{point.Path}";

    private static string NodeNamespace(string path)
    {
        if (!path.StartsWith("ns=", StringComparison.Ordinal)) return "0";
        var separator = path.IndexOf(';');
        return separator > 3 ? path[3..separator] : string.Empty;
    }

    private static string EncodeCursor(string value)
        => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string? DecodeCursor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var base64 = value.Trim().Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("点位分页游标无效。", exception);
        }
    }

    private static string Join(string parent, string name)
        => string.IsNullOrEmpty(parent) ? name : $"{parent}.{name}";

    private static object? JsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => value.GetRawText()
    };
}

public sealed record ProbeSnapshot(
    Dictionary<string, object?> Values,
    IReadOnlyList<AcquisitionProbePoint> Points,
    bool MappingsValidated = true,
    DiscoveryPage? Page = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? TopicValuesSource = null)
{
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> TopicValues { get; } =
        TopicValuesSource ?? new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.Ordinal);
}

public sealed record DiscoveryPage(
    IReadOnlyList<AcquisitionProbePoint> Points,
    string? NextCursor);

internal sealed record PublicationEvidencePath(string? Path, string? Topic)
{
    public string Display => string.IsNullOrWhiteSpace(Topic) ? Path! : $"{Topic} → {Path}";
}
