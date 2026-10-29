// Pins one SNMP IPv4 unicast endpoint and performs bounded, read-only GET and subtree-walk operations.
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ingot.Contracts.Acquisition;
using Ingot.Edge.ConnectorHost.Acquisition.Probers;
using Lextm.SharpSnmpLib;
using Lextm.SharpSnmpLib.Messaging;
using Lextm.SharpSnmpLib.Security;

namespace Ingot.Edge.ConnectorHost.Acquisition;

internal static class SnmpProtocolSupport
{
    public const int MaximumConfiguredOids = 128;
    public const int MaximumGetBatchSize = 32;
    public const int MaximumBrowseEntries = 512;
    public const int MaximumRepetitions = 25;

    public static async Task<SnmpSession> OpenAsync(
        SnmpConnection connection,
        IAcquisitionSecretResolver secretResolver,
        AcquisitionHttpEgressPolicy egressPolicy,
        int timeoutMs,
        ILogger logger,
        CancellationToken ct)
    {
        var configured = ParseFixedUnicastIpv4(connection.Host);
        var pinnedText = await egressPolicy.ResolvePinnedHostAsync(
            configured.ToString(),
            "SNMP",
            ct,
            requireExplicitAllowlist: true).ConfigureAwait(false);
        if (!IPAddress.TryParse(pinnedText, out var pinned) ||
            pinned.AddressFamily != AddressFamily.InterNetwork ||
            !pinned.Equals(configured))
            throw new InvalidOperationException("SNMP 出站策略未返回与配置相同的固定 IPv4 地址，已拒绝连接。");

        var endpoint = new IPEndPoint(pinned, connection.Port);
        var requestTimeout = Math.Clamp(timeoutMs, 100, 30_000);
        var version = connection.Version switch
        {
            "v2c" => VersionCode.V2,
            "v3" => VersionCode.V3,
            _ => throw new InvalidOperationException("SNMP 版本必须是 v2c 或 v3。")
        };

        if (connection.Version == "v2c")
        {
            var community = AcquisitionSecretReference.ResolveRequired(
                secretResolver,
                connection.CommunitySecretRef,
                "SNMP community");
            if (string.IsNullOrWhiteSpace(community))
                throw new InvalidOperationException("SNMP community 密钥不能为空。");
            var session = new SnmpSession(connection, endpoint, version, new OctetString(community), null,
                DefaultPrivacyProvider.DefaultPair, report: null, requestTimeout, logger);
            return session;
        }

        var username = connection.UserName;
        if (string.IsNullOrWhiteSpace(username))
            throw new InvalidOperationException("SNMPv3 用户名不能为空。");

        var authentication = connection.SecurityLevel == "noAuthNoPriv"
            ? DefaultAuthenticationProvider.Instance
            : CreateAuthenticationProvider(
                connection.AuthenticationProtocol,
                ResolvePassphrase(secretResolver, connection.AuthenticationSecretRef, "SNMPv3 authentication"));
        var privacy = connection.SecurityLevel switch
        {
            "noAuthNoPriv" => DefaultPrivacyProvider.DefaultPair,
            "authNoPriv" => new DefaultPrivacyProvider(authentication),
            "authPriv" => CreatePrivacyProvider(
                connection.PrivacyProtocol,
                ResolvePassphrase(secretResolver, connection.PrivacySecretRef, "SNMPv3 privacy"),
                authentication),
            _ => throw new InvalidOperationException("SNMPv3 安全级别无效。")
        };

        var discovery = Messenger.GetNextDiscovery(SnmpType.GetRequestPdu);
        ReportMessage report;
        try
        {
            report = await Task.Run(
                () => discovery.GetResponse(requestTimeout, endpoint),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is SnmpException or SocketException or
                Lextm.SharpSnmpLib.Messaging.TimeoutException or System.TimeoutException)
        {
            logger.LogDebug(exception, "SNMPv3 discovery failed for {Host}:{Port}", endpoint.Address, endpoint.Port);
            throw new IOException("SNMPv3 用户/引擎发现失败；请检查目标、用户名和安全级别。", exception);
        }

        return new SnmpSession(
            connection,
            endpoint,
            version,
            community: null,
            new OctetString(username),
            privacy,
            report,
            requestTimeout,
            logger);
    }

    public static IReadOnlyList<string> ConfiguredOids(AcquisitionDeployment deployment)
    {
        if (AcquisitionProbeSupport.IsDiscoveryProbe(deployment)) return [];
        var task = deployment.Task;
        IEnumerable<string?> selectors = task.ValueMappings.Select(static mapping => mapping.SourcePath)
            .Concat(task.ValueMappings.Select(static mapping => mapping.QualityPath))
            .Concat(task.ContextMappings.Select(static mapping => mapping.SourcePath));
        if (task.TimestampMode == "source") selectors = selectors.Append(task.TimestampPath);
        if (!string.IsNullOrWhiteSpace(task.SequencePath)) selectors = selectors.Append(task.SequencePath);
        if (task.ProcessSpecification is { } process)
        {
            selectors = selectors.Append(process.IdPath)
                .Append(process.VersionPath)
                .Append(process.NamePath)
                .Concat(process.ParameterMappings.Select(static mapping => mapping.SourcePath))
                .Concat(process.ParameterMappings.Select(static mapping => mapping.QualityPath));
        }

        var paths = selectors.Where(static item => !string.IsNullOrWhiteSpace(item))
            .Select(static item => item!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (paths.Length > MaximumConfiguredOids)
            throw new InvalidDataException($"SNMP 单任务最多读取 {MaximumConfiguredOids} 个不同 OID。");
        return paths;
    }

    public static IReadOnlyDictionary<string, string> ConfiguredDataTypes(AcquisitionDeployment deployment)
        => deployment.Task.ValueMappings
            .Concat(deployment.Task.ProcessSpecification?.ParameterMappings ?? [])
            .GroupBy(static mapping => mapping.SourcePath, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static mapping => mapping.SourceDataType)
                    .FirstOrDefault(static type => type != "auto") ?? "auto",
                StringComparer.Ordinal);

    public static string ResolveTimestampEncoding(IngestionTask task, object? rawTimestamp)
    {
        if (task.TimestampEncoding != AcquisitionTimestampEncodings.Auto) return task.TimestampEncoding;
        return rawTimestamp switch
        {
            string => AcquisitionTimestampEncodings.Iso8601,
            int or uint or short or ushort or byte or sbyte => AcquisitionTimestampEncodings.UnixSeconds,
            long or ulong => AcquisitionTimestampEncodings.UnixMilliseconds,
            _ => AcquisitionTimestampEncodings.Iso8601
        };
    }

    public static IReadOnlyList<uint> ResolveBrowseRoot(SnmpConnection connection, SourceDiscoveryQuery query)
    {
        if (!AcquisitionSelectors.TryParseSnmpOid(connection.BrowseRootOid, out var configuredRoot, out var error))
            throw new InvalidDataException($"SNMP 浏览根 OID 无效：{error}");

        if (string.IsNullOrWhiteSpace(query.RootPath)) return configuredRoot.Arcs;
        if (!AcquisitionSelectors.TryParseSnmpOid(query.RootPath, out var requestedRoot, out var requestedError))
            throw new InvalidDataException($"SNMP 发现查询根 OID 无效：{requestedError}");
        if (!IsInSubtree(requestedRoot.Arcs, configuredRoot.Arcs))
            throw new InvalidDataException("SNMP 发现查询根 OID 必须位于连接配置允许的浏览子树内。");
        return requestedRoot.Arcs;
    }

    public static bool IsInSubtree(IReadOnlyList<uint> oid, IReadOnlyList<uint> root)
    {
        if (oid.Count < root.Count) return false;
        for (var index = 0; index < root.Count; index++)
            if (oid[index] != root[index]) return false;
        return true;
    }

    public static int CompareOids(IReadOnlyList<uint> left, IReadOnlyList<uint> right)
    {
        var shared = Math.Min(left.Count, right.Count);
        for (var index = 0; index < shared; index++)
        {
            var comparison = left[index].CompareTo(right[index]);
            if (comparison != 0) return comparison;
        }
        return left.Count.CompareTo(right.Count);
    }

    private static IPAddress ParseFixedUnicastIpv4(string host)
    {
        var text = host?.Trim();
        if (string.IsNullOrWhiteSpace(text) ||
            !IPAddress.TryParse(text, out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            !string.Equals(address.ToString(), text, StringComparison.Ordinal))
            throw new InvalidOperationException("SNMP 目标必须是规范 IPv4 字面地址，不能使用主机名或 IPv6。");

        var bytes = address.GetAddressBytes();
        if (IPAddress.IsLoopback(address) ||
            address.Equals(IPAddress.Any) ||
            address.Equals(IPAddress.Broadcast) ||
            bytes[0] is 0 or >= 224 ||
            bytes[0] == 169 && bytes[1] == 254)
            throw new InvalidOperationException("SNMP 目标必须是可单播的固定 IPv4 设备地址，不能是回环、未指定、链路本地、广播或组播地址。");

        return address;
    }

    private static string ResolvePassphrase(
        IAcquisitionSecretResolver resolver,
        string? reference,
        string label)
    {
        var secret = AcquisitionSecretReference.ResolveRequired(resolver, reference, label);
        if (secret.Length < 8)
            throw new InvalidOperationException($"{label}密钥必须至少包含 8 个字符。");
        return secret;
    }

    private static IAuthenticationProvider CreateAuthenticationProvider(string? protocol, string phrase)
    {
        var key = new OctetString(phrase);
        return protocol switch
        {
            "sha256" => new SHA256AuthenticationProvider(key),
            "sha384" => new SHA384AuthenticationProvider(key),
            "sha512" => new SHA512AuthenticationProvider(key),
            _ => throw new InvalidOperationException("SNMPv3 认证算法必须是 SHA-256、SHA-384 或 SHA-512。")
        };
    }

    private static IPrivacyProvider CreatePrivacyProvider(
        string? protocol,
        string phrase,
        IAuthenticationProvider authentication)
    {
        var key = new OctetString(phrase);
        return protocol switch
        {
            "aes128" => new AESPrivacyProvider(key, authentication),
            "aes192" => new AES192PrivacyProvider(key, authentication),
            "aes256" => new AES256PrivacyProvider(key, authentication),
            _ => throw new InvalidOperationException("SNMPv3 隐私算法必须是 AES-128、AES-192 或 AES-256。")
        };
    }

    internal sealed class SnmpSession : IDisposable
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);
        private readonly SnmpConnection _connection;
        private readonly IPEndPoint _endpoint;
        private readonly VersionCode _version;
        private readonly OctetString? _community;
        private readonly OctetString? _userName;
        private readonly IPrivacyProvider _privacy;
        private readonly int _requestTimeoutMs;
        private readonly ILogger _logger;
        private ReportMessage? _report;

        public SnmpSession(
            SnmpConnection connection,
            IPEndPoint endpoint,
            VersionCode version,
            OctetString? community,
            OctetString? userName,
            IPrivacyProvider privacy,
            ReportMessage? report,
            int requestTimeoutMs,
            ILogger logger)
        {
            _connection = connection;
            _endpoint = endpoint;
            _version = version;
            _community = community;
            _userName = userName;
            _privacy = privacy;
            _report = report;
            _requestTimeoutMs = requestTimeoutMs;
            _logger = logger;
        }

        public async Task<Dictionary<string, object?>> ReadSnapshotAsync(
            IReadOnlyList<string> oids,
            IReadOnlyDictionary<string, string> dataTypes,
            CancellationToken ct)
        {
            if (oids.Count is < 1 or > MaximumConfiguredOids)
                throw new InvalidDataException($"SNMP GET 必须包含 1-{MaximumConfiguredOids} 个不同 OID。");

            var result = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var batch in oids.Chunk(MaximumGetBatchSize))
            {
                ct.ThrowIfCancellationRequested();
                var values = await Task.Run(() => ReadGetBatch(batch, dataTypes, ct), ct).ConfigureAwait(false);
                foreach (var item in values) result[item.Key] = item.Value;
            }
            return result;
        }

        public async Task<IReadOnlyList<AcquisitionProbePoint>> BrowseAsync(
            IReadOnlyList<uint> root,
            int executionTimeoutMs,
            CancellationToken ct)
        {
            var budgetMs = Math.Clamp(executionTimeoutMs, 100, 30_000);
            var timer = Stopwatch.StartNew();
            var points = new List<AcquisitionProbePoint>(MaximumBrowseEntries);
            var current = new ObjectIdentifier(FormatOid(root));
            IReadOnlyList<uint>? previous = null;

            while (points.Count < MaximumBrowseEntries)
            {
                ct.ThrowIfCancellationRequested();
                var remaining = budgetMs - (int)timer.ElapsedMilliseconds;
                if (remaining <= 0)
                    throw new System.TimeoutException($"SNMP OID 子树浏览超过总预算 {budgetMs}ms。");
                var timeout = Math.Min(_requestTimeoutMs, remaining);
                var reply = await Task.Run(() => GetBulk(current, timeout), ct).ConfigureAwait(false);
                EnsureNoPduError(reply, "SNMP GETBULK 子树浏览");
                var variables = reply.Pdu().Variables;
                if (variables.Count == 0) break;
                if (variables.Count > MaximumRepetitions)
                    throw new InvalidDataException(
                        $"SNMP GETBULK 返回 {variables.Count} 个变量，超过本次请求上限 {MaximumRepetitions}。 ");

                var stop = false;
                foreach (var variable in variables)
                {
                    if (IsEndOfMib(variable.Data.TypeCode))
                    {
                        stop = true;
                        break;
                    }
                    if (IsMissing(variable.Data.TypeCode))
                    {
                        stop = true;
                        break;
                    }
                    if (!AcquisitionSelectors.TryParseSnmpOid(variable.Id.ToString(), out var oid, out var oidError))
                        throw new InvalidDataException($"SNMP GETBULK 返回非法 OID：{oidError}");
                    if (previous is not null && CompareOids(oid.Arcs, previous) <= 0)
                        throw new InvalidDataException("SNMP GETBULK 返回的 OID 没有严格按数字顺序递增，已停止浏览。");
                    if (!IsInSubtree(oid.Arcs, root))
                    {
                        stop = true;
                        break;
                    }

                    var value = ConvertData(variable.Data, "auto");
                    points.Add(new AcquisitionProbePoint
                    {
                        Path = oid.Value,
                        Name = oid.Value,
                        Kind = "snmp",
                        DataType = variable.Data.TypeCode.ToString(),
                        RawValue = AcquisitionProbeSupport.Format(value),
                        Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["asnType"] = variable.Data.TypeCode.ToString(),
                            ["protocolVersion"] = _connection.Version,
                            ["readOnly"] = "true"
                        }
                    });
                    previous = oid.Arcs;
                    current = variable.Id;
                    if (points.Count >= MaximumBrowseEntries)
                    {
                        stop = true;
                        break;
                    }
                }
                if (stop) break;
            }
            return points;
        }

        private IReadOnlyList<KeyValuePair<string, object?>> ReadGetBatch(
            IReadOnlyList<string> oids,
            IReadOnlyDictionary<string, string> dataTypes,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var variables = oids.Select(static oid => new Variable(new ObjectIdentifier(oid))).ToList();
            var reply = Get(variables, _requestTimeoutMs);
            EnsureNoPduError(reply, "SNMP GET");
            var received = reply.Pdu().Variables;
            if (received.Count != oids.Count)
                throw new InvalidDataException($"SNMP GET 返回 {received.Count} 个变量，期望 {oids.Count} 个。");

            var result = new List<KeyValuePair<string, object?>>(oids.Count);
            for (var index = 0; index < oids.Count; index++)
            {
                var variable = received[index];
                if (!string.Equals(variable.Id.ToString(), oids[index], StringComparison.Ordinal))
                    throw new InvalidDataException($"SNMP GET 响应 OID 与请求不一致：请求 {oids[index]}，收到 {variable.Id}。");
                var value = IsMissing(variable.Data.TypeCode) || IsEndOfMib(variable.Data.TypeCode)
                    ? null
                    : ConvertData(variable.Data, dataTypes.GetValueOrDefault(oids[index]) ?? "auto");
                result.Add(new KeyValuePair<string, object?>(oids[index], value));
            }
            return result;
        }

        private ISnmpMessage Get(IList<Variable> variables, int timeoutMs)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                ISnmpMessage request = _version == VersionCode.V2
                    ? new GetRequestMessage(Messenger.NextRequestId, _version, _community!, variables)
                    : new GetRequestMessage(
                        VersionCode.V3,
                        Messenger.NextMessageId,
                        Messenger.NextRequestId,
                        _userName!,
                        new OctetString(_connection.ContextName ?? string.Empty),
                        variables,
                        _privacy,
                        Messenger.MaxMessageSize,
                        _report ?? throw new InvalidOperationException("SNMPv3 discovery Report 尚未初始化。"));
                var reply = request.GetResponse(timeoutMs, _endpoint);
                if (reply is not ReportMessage report) return reply;
                _report = report;
                _logger.LogDebug("SNMPv3 agent returned a Report; synchronized discovery state and retrying read once.");
            }
            throw new InvalidDataException("SNMPv3 agent 连续返回 REPORT，读取请求已停止。");
        }

        private ISnmpMessage GetBulk(ObjectIdentifier cursor, int timeoutMs)
        {
            var variables = new List<Variable> { new(cursor) };
            for (var attempt = 0; attempt < 2; attempt++)
            {
                ISnmpMessage request = _version == VersionCode.V2
                    ? new GetBulkRequestMessage(
                        Messenger.NextRequestId, VersionCode.V2, _community!, 0,
                        MaximumRepetitions, variables)
                    : new GetBulkRequestMessage(
                        VersionCode.V3,
                        Messenger.NextMessageId,
                        Messenger.NextRequestId,
                        _userName!,
                        new OctetString(_connection.ContextName ?? string.Empty),
                        nonRepeaters: 0,
                        maxRepetitions: MaximumRepetitions,
                        variables,
                        _privacy,
                        Messenger.MaxMessageSize,
                        _report ?? throw new InvalidOperationException("SNMPv3 discovery Report 尚未初始化。"));
                var reply = request.GetResponse(timeoutMs, _endpoint);
                if (reply is not ReportMessage report) return reply;
                _report = report;
                _logger.LogDebug("SNMPv3 agent returned a Report during subtree browse; synchronized discovery state and retrying once.");
            }
            throw new InvalidDataException("SNMPv3 agent 连续返回 REPORT，子树浏览已停止。");
        }

        private void EnsureNoPduError(ISnmpMessage reply, string operation)
        {
            var errorStatus = reply.Pdu().ErrorStatus.ToInt32();
            if (errorStatus != 0)
                throw new InvalidDataException($"{operation} 失败，SNMP 错误状态 {errorStatus}。");
        }

        private static object? ConvertData(ISnmpData data, string dataType)
        {
            if (IsMissing(data.TypeCode) || IsEndOfMib(data.TypeCode) || data.TypeCode == SnmpType.Null)
                return null;

            if (dataType == "string") return ConvertString(data);
            if (dataType == "boolean") return ConvertBoolean(data);
            if (dataType is "int32" or "uint32" or "uint64") return ConvertNumeric(data, dataType);

            return data.TypeCode switch
            {
                SnmpType.Integer32 => ((Integer32)data).ToInt32(),
                SnmpType.Counter32 => ((Counter32)data).ToUInt32(),
                SnmpType.Gauge32 => ((Gauge32)data).ToUInt32(),
                SnmpType.TimeTicks => ((TimeTicks)data).ToUInt32(),
                SnmpType.Counter64 => ((Counter64)data).ToUInt64(),
                SnmpType.OctetString => DecodeUtf8OrHex((OctetString)data),
                SnmpType.ObjectIdentifier or SnmpType.IPAddress => data.ToString(),
                _ => throw new InvalidDataException($"SNMP ASN.1 类型 {data.TypeCode} 暂不支持 auto 映射。")
            };
        }

        private static object ConvertNumeric(ISnmpData data, string targetType)
        {
            var number = data.TypeCode switch
            {
                SnmpType.Integer32 => (decimal)((Integer32)data).ToInt32(),
                SnmpType.Counter32 => ((Counter32)data).ToUInt32(),
                SnmpType.Gauge32 => ((Gauge32)data).ToUInt32(),
                SnmpType.TimeTicks => ((TimeTicks)data).ToUInt32(),
                SnmpType.Counter64 => ((Counter64)data).ToUInt64(),
                SnmpType.OctetString when decimal.TryParse(
                    DecodeUtf8((OctetString)data), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => throw new InvalidDataException($"SNMP ASN.1 类型 {data.TypeCode} 不能转换为 {targetType}。")
            };
            return targetType switch
            {
                "int32" => checked((int)number),
                "uint32" => checked((uint)number),
                "uint64" => checked((ulong)number),
                _ => throw new InvalidDataException($"SNMP 数据类型 {targetType} 不受支持。")
            };
        }

        private static bool ConvertBoolean(ISnmpData data)
        {
            var text = data.ToString();
            if (bool.TryParse(text, out var boolean)) return boolean;
            if (data.TypeCode == SnmpType.OctetString)
            {
                var stringValue = DecodeUtf8((OctetString)data);
                if (bool.TryParse(stringValue, out boolean)) return boolean;
                text = stringValue;
            }
            if (data.TypeCode is SnmpType.Integer32 or SnmpType.Counter32 or SnmpType.Gauge32 or SnmpType.TimeTicks or SnmpType.Counter64)
            {
                var number = ConvertNumeric(data, "uint64");
                return (ulong)number switch
                {
                    0 => false,
                    1 => true,
                    _ => throw new InvalidDataException($"SNMP 布尔映射只接受 0 或 1：{text}。")
                };
            }
            if (text == "0") return false;
            if (text == "1") return true;
            throw new InvalidDataException($"SNMP 布尔映射只接受 true/false 或 0/1：{text}。");
        }

        private static string ConvertString(ISnmpData data)
            => data.TypeCode == SnmpType.OctetString
                ? DecodeUtf8((OctetString)data)
                : data.ToString();

        private static string DecodeUtf8(OctetString data)
        {
            try
            {
                return StrictUtf8.GetString(data.GetRaw());
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("SNMP OctetString 不是有效 UTF-8 文本；请使用设备侧文本 OID。", exception);
            }
        }

        private static string DecodeUtf8OrHex(OctetString data)
        {
            try
            {
                var text = StrictUtf8.GetString(data.GetRaw());
                return text.Any(static character =>
                    char.IsControl(character) && character is not ('\t' or '\r' or '\n'))
                    ? Convert.ToHexString(data.GetRaw()).ToLowerInvariant()
                    : text;
            }
            catch (DecoderFallbackException)
            {
                return Convert.ToHexString(data.GetRaw()).ToLowerInvariant();
            }
        }

        private static bool IsMissing(SnmpType type)
            => type is SnmpType.NoSuchObject or SnmpType.NoSuchInstance;

        private static bool IsEndOfMib(SnmpType type) => type == SnmpType.EndOfMibView;

        private static string FormatOid(IReadOnlyList<uint> arcs)
            => string.Join(".", arcs.Select(static arc => arc.ToString(CultureInfo.InvariantCulture)));

        public void Dispose()
        {
            // SharpSnmpLib opens a short-lived UDP socket per request and closes it after each response.
        }
    }
}
