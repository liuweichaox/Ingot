using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Events;
using Ingot.Platform.Infrastructure.Acquisition;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/acquisition-profiles")]
public sealed partial class AcquisitionProfilesController(
    IAcquisitionProfileStore store,
    IProcessConfigurationStore processStore,
    PlatformUserResolver userResolver,
    EdgeTokenValidator edgeTokenValidator,
    EdgeRegistry edgeRegistry,
    IHttpClientFactory httpClientFactory) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ?? Ok(new { data = await store.ListAsync(ct).ConfigureAwait(false) });

    [HttpGet("{profileId}/{version:int}")]
    public async Task<IActionResult> Get(string profileId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null) return denied;
        var value = await store.GetAsync(NormalizeCode(profileId), version, ct).ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
    }

    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<IActionResult> Active([FromQuery] string edgeId, CancellationToken ct)
    {
        var normalizedEdgeId = edgeId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedEdgeId))
            return BadRequest(new { error = "edgeId 不能为空。" });
        if (!edgeTokenValidator.IsAuthorized(normalizedEdgeId, Request.Headers.Authorization.ToString()))
            return Unauthorized(new { error = "边缘节点认证失败。" });

        var profiles = await store.ListPublishedForEdgeAsync(normalizedEdgeId, ct).ConfigureAwait(false);
        var deployments = new List<AcquisitionDeployment>();
        foreach (var profile in profiles)
        {
            var model = await processStore.GetDataModelAsync(profile.DataModelId, profile.DataModelVersion, ct)
                .ConfigureAwait(false);
            if (model is not null && model.Status == ConfigurationStatuses.Published)
                deployments.Add(new AcquisitionDeployment { Profile = profile, DataModel = model });
        }
        return Ok(new { data = deployments });
    }

    [HttpPost]
    public async Task<IActionResult> Upsert([FromBody] AcquisitionProfile? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (!TryNormalize(request, out var normalized, out var error))
            return BadRequest(new { error });

        var model = await processStore.GetDataModelAsync(
            normalized!.DataModelId,
            normalized.DataModelVersion,
            ct).ConfigureAwait(false);
        if (model is null)
            return BadRequest(new { error = "引用的工艺数据模型版本不存在。" });
        if (!ValidateMappings(normalized, model, out error))
            return BadRequest(new { error });
        if (normalized.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            return BadRequest(new { error = "发布采集配置前，引用的工艺数据模型必须已经发布。" });
        if (normalized.Status == ConfigurationStatuses.Published)
        {
            var probe = await ProbeEdgeAsync(
                new AcquisitionDeployment { Profile = normalized, DataModel = model },
                ct).ConfigureAwait(false);
            if (!probe.Success)
                return StatusCode(probe.StatusCode, new { error = probe.Error, validation = probe.Result });
            if (probe.Result is not { Success: true, MappingsValidated: true })
                return BadRequest(new
                {
                    error = probe.Result?.Message ?? "设备连接与映射验证未通过，不能发布采集配置。",
                    validation = probe.Result
                });
        }

        var existing = await store.GetAsync(normalized.ProfileId, normalized.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
        {
            if (existing.Status == ConfigurationStatuses.Published && normalized.Status == ConfigurationStatuses.Retired)
                normalized = existing with { Status = ConfigurationStatuses.Retired, UpdatedAt = DateTimeOffset.UtcNow };
            else if (SamePayload(existing with { UpdatedAt = default }, normalized with { UpdatedAt = default }))
                return Ok(existing);
            else
                return Conflict(new { error = "已发布或停用的采集配置不可修改，请创建新版本。", existing });
        }

        // 发布走单事务：退役旧 published 版本 + 写入新版本原子完成，
        // 消除读-改-写循环在并发发布下残留两个 published 版本的竞态。
        return normalized.Status == ConfigurationStatuses.Published
            ? Ok(await store.PublishExclusiveAsync(normalized, ct).ConfigureAwait(false))
            : Ok(await store.UpsertAsync(normalized, ct).ConfigureAwait(false));
    }

    [HttpPost("probe")]
    public async Task<IActionResult> Probe([FromBody] AcquisitionProfile? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null)
            return BadRequest(new { error = "采集配置不能为空。" });
        var model = await processStore.GetDataModelAsync(
            NormalizeCode(request.DataModelId),
            request.DataModelVersion,
            ct).ConfigureAwait(false);
        if (model is null)
            return BadRequest(new { error = "引用的工艺数据模型版本不存在。" });

        const string placeholderCode = "__probe_only__";
        var needsDiscoveryPlaceholder =
            request.Protocol is AcquisitionProtocols.HttpPolling or AcquisitionProtocols.Mqtt or AcquisitionProtocols.OpcUa &&
            request.ValueMappings.All(item => string.IsNullOrWhiteSpace(item.DataItemCode) ||
                                              string.IsNullOrWhiteSpace(item.SourcePath));
        var probeRequest = needsDiscoveryPlaceholder
            ? request with
            {
                ValueMappings =
                [
                    new AcquisitionValueMapping
                    {
                        DataItemCode = model.Acquisition.DataItems.First().Code,
                        SourcePath = placeholderCode,
                        Required = false
                    }
                ]
            }
            : request;
        if (!TryNormalize(probeRequest, out var normalized, out var error))
            return BadRequest(new { error });

        if (!ValidateMappings(normalized!, model, out error))
            return BadRequest(new { error });

        var probe = await ProbeEdgeAsync(
            new AcquisitionDeployment { Profile = normalized!, DataModel = model },
            ct).ConfigureAwait(false);
        if (!probe.Success)
            return StatusCode(probe.StatusCode, new { error = probe.Error });
        return needsDiscoveryPlaceholder && probe.Result is { } result
            ? Ok(result with
            {
                Message = $"连接成功，读取到 {result.Points.Count} 个设备点位；请选择点位并完成映射后再次验证。",
                MappingsValidated = false,
                Mappings = []
            })
            : Ok(probe.Result);
    }

    private async Task<EdgeProbeResponse> ProbeEdgeAsync(
        AcquisitionDeployment deployment,
        CancellationToken cancellationToken)
    {
        var state = edgeRegistry.Find(deployment.Profile.EdgeId);
        if (string.IsNullOrWhiteSpace(state?.HostBaseUrl))
            return EdgeProbeResponse.Failure(
                StatusCodes.Status400BadRequest,
                "该现场节点未上报访问地址，无法测试设备连接。");

        var client = httpClientFactory.CreateClient();
        if (edgeTokenValidator.TryGetToken(deployment.Profile.EdgeId, out var token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            using var response = await client.PostAsJsonAsync(
                new Uri(new Uri(state.HostBaseUrl), "/api/v1/acquisition/probe"),
                new AcquisitionProbeRequest { Deployment = deployment },
                cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = TryReadError(body);
                return EdgeProbeResponse.Failure((int)response.StatusCode, detail);
            }
            var result = JsonSerializer.Deserialize<AcquisitionProbeResult>(
                body,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            return result is null
                ? EdgeProbeResponse.Failure(StatusCodes.Status502BadGateway, "现场节点返回了无效的验证结果。")
                : EdgeProbeResponse.Succeeded(result);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EdgeProbeResponse.Failure(StatusCodes.Status504GatewayTimeout, "现场节点设备验证超时。");
        }
        catch (HttpRequestException exception)
        {
            return EdgeProbeResponse.Failure(
                StatusCodes.Status502BadGateway,
                $"现场节点不可访问：{exception.Message}");
        }
    }

    private static string TryReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                ? error.GetString() ?? "设备验证失败。"
                : body;
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(body) ? "设备验证失败。" : body;
        }
    }

    private sealed record EdgeProbeResponse(
        bool Success,
        int StatusCode,
        string? Error,
        AcquisitionProbeResult? Result)
    {
        public static EdgeProbeResponse Succeeded(AcquisitionProbeResult result)
            => new(true, StatusCodes.Status200OK, null, result);

        public static EdgeProbeResponse Failure(int statusCode, string error)
            => new(false, statusCode, error, null);
    }

    [HttpDelete("{profileId}/{version:int}")]
    public async Task<IActionResult> Delete(string profileId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var existing = await store.GetAsync(NormalizeCode(profileId), version, ct).ConfigureAwait(false);
        if (existing is null) return NotFound();
        if (existing.Status != ConfigurationStatuses.Draft)
            return Conflict(new { error = "只有草稿采集配置可以删除。" });
        return await store.DeleteAsync(existing.ProfileId, version, ct).ConfigureAwait(false)
            ? NoContent()
            : NotFound();
    }

    private static bool TryNormalize(
        AcquisitionProfile? value,
        out AcquisitionProfile? normalized,
        out string error)
    {
        normalized = null;
        if (value is null) return Fail("采集配置不能为空。", out error);
        var id = NormalizeCode(value.ProfileId);
        var status = value.Status?.Trim().ToLowerInvariant();
        var protocol = value.Protocol?.Trim().ToLowerInvariant();
        if (!CodePattern().IsMatch(id) || value.Version < 1 || string.IsNullOrWhiteSpace(value.Name))
            return Fail("采集配置编码无效、版本小于 1 或名称为空。", out error);
        if (!ConfigurationStatuses.IsValid(status))
            return Fail("采集配置状态必须是 draft、published 或 retired。", out error);
        if (!AcquisitionProtocols.IsSupported(protocol))
            return Fail("采集协议必须是 HTTP 轮询、MQTT、OPC UA、Modbus TCP 或 MELSEC 1E。", out error);
        if (string.IsNullOrWhiteSpace(value.EdgeId) ||
            string.IsNullOrWhiteSpace(value.SubjectId) || string.IsNullOrWhiteSpace(value.Source))
            return Fail("现场节点、设备编号和内部事件来源不能为空。", out error);
        if (!ValidateConnection(value, protocol!, out error))
            return false;
        if (value.Execution.TimeoutMs < 100 || value.Execution.ReconnectDelayMs < 100)
            return Fail("连接超时和重连间隔不能小于 100ms。", out error);
        if (string.IsNullOrWhiteSpace(value.SampleEventType))
            return Fail("采样事件类型不能为空。", out error);
        if (value.TimestampMode is not ("source" or "edge-received"))
            return Fail("时间戳模式必须是源数据时间或边缘接收时间。", out error);
        if (value.TimestampMode == "source" &&
            (protocol is AcquisitionProtocols.HttpPolling or AcquisitionProtocols.Mqtt) &&
            string.IsNullOrWhiteSpace(value.TimestampPath))
            return Fail("HTTP 和 MQTT 数据需要配置时间戳字段路径。", out error);

        var contextMappings = value.ContextMappings
            .Where(item => !string.IsNullOrWhiteSpace(item.ContextKey) || !string.IsNullOrWhiteSpace(item.SourcePath))
            .Select(item => item with
            {
                ContextKey = NormalizeCode(item.ContextKey),
                SourcePath = item.SourcePath.Trim()
            }).ToArray();
        if (contextMappings.Any(item => !CodePattern().IsMatch(item.ContextKey) || string.IsNullOrWhiteSpace(item.SourcePath)) ||
            contextMappings.Select(item => item.ContextKey).Distinct(StringComparer.Ordinal).Count() != contextMappings.Length)
            return Fail("上下文映射的键或设备字段路径无效或重复。", out error);

        var valueMappings = value.ValueMappings.Select(item => item with
        {
            DataItemCode = NormalizeCode(item.DataItemCode),
            SourcePath = protocol == AcquisitionProtocols.ModbusTcp && item.ModbusAddress.HasValue
                ? $"{item.ModbusArea}:{item.ModbusAddress.Value}:{item.SourceDataType}"
                : item.SourcePath?.Trim() ?? string.Empty
        }).ToArray();
        if (valueMappings.Count(item => HasSourceSelector(protocol!, item)) == 0)
            return Fail("至少需要启用一个采集数据项。", out error);
        if (valueMappings.Any(item => !CodePattern().IsMatch(item.DataItemCode) || !HasSourceSelector(protocol!, item)) ||
            valueMappings.Select(item => item.DataItemCode).Distinct(StringComparer.Ordinal).Count() != valueMappings.Length)
            return Fail("采集数据项映射无效或重复。", out error);
        if (valueMappings.Any(item => !double.IsFinite(item.Scale) || !double.IsFinite(item.Offset)))
            return Fail("数据项换算倍率和偏移必须是有限数字。", out error);
        if (protocol == AcquisitionProtocols.ModbusTcp &&
            valueMappings.Any(item =>
                item.ModbusQuantity is < 1 or > 64 ||
                item.SourceDataType is not ("auto" or "int16" or "uint16" or "int32" or "uint32" or
                    "float32" or "int64" or "uint64" or "float64" or "string") ||
                item.ByteOrder is not ("big-endian" or "little-endian") ||
                item.WordOrder is not ("high-low" or "low-high")))
            return Fail("Modbus 数据类型、寄存器数量、字节序或字序无效。", out error);

        var staticContext = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in value.StaticContext.Where(pair =>
                     !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value)))
        {
            var key = NormalizeCode(pair.Key);
            if (!CodePattern().IsMatch(key) || !staticContext.TryAdd(key, pair.Value.Trim()))
                return Fail("固定上下文键无效或重复。", out error);
        }
        normalized = value with
        {
            ProfileId = id,
            Name = value.Name.Trim(),
            Status = status!,
            EdgeId = value.EdgeId.Trim(),
            Protocol = protocol!,
            DataModelId = NormalizeCode(value.DataModelId),
            Source = value.Source.Trim().TrimStart('/'),
            // 接入页面只面向一个清晰概念：设备。SubjectType 是事件契约内部字段，不让用户选择。
            SubjectType = "equipment",
            SubjectId = value.SubjectId.Trim(),
            Connection = value.Connection with
            {
                BaseUrl = value.Connection.BaseUrl.Trim().TrimEnd('/'),
                SnapshotPath = value.Connection.SnapshotPath.Trim()
            },
            Mqtt = NormalizeMqtt(value.Mqtt),
            OpcUa = NormalizeOpcUa(value.OpcUa),
            ModbusTcp = NormalizeModbusTcp(value.ModbusTcp),
            MelsecA1E = NormalizeMelsecA1E(value.MelsecA1E),
            TimestampMode = value.TimestampMode.Trim().ToLowerInvariant(),
            TimestampPath = value.TimestampPath?.Trim() ?? string.Empty,
            SequencePath = string.IsNullOrWhiteSpace(value.SequencePath) ? null : value.SequencePath.Trim(),
            SampleEventType = value.SampleEventType.Trim(),
            StaticContext = staticContext,
            ContextMappings = contextMappings,
            ValueMappings = valueMappings,
            Recipe = NormalizeRecipe(value.Recipe),
            Lifecycle = NormalizeLifecycle(value.Lifecycle),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        error = string.Empty;
        return true;
    }

    private static bool ValidateConnection(AcquisitionProfile value, string protocol, out string error)
    {
        switch (protocol)
        {
            case AcquisitionProtocols.HttpPolling:
                if (!Uri.TryCreate(value.Connection.BaseUrl, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https"))
                    return Fail("设备地址必须是 HTTP 或 HTTPS 绝对地址。", out error);
                if (string.IsNullOrWhiteSpace(value.Connection.SnapshotPath) ||
                    value.Connection.PollIntervalMs < 1)
                    return Fail("快照路径不能为空，读取后等待时间必须大于 0ms。", out error);
                break;
            case AcquisitionProtocols.Mqtt:
                if (value.Mqtt is null || string.IsNullOrWhiteSpace(value.Mqtt.Host) ||
                    value.Mqtt.Port is < 1 or > 65535 || value.Mqtt.Topics.Count == 0)
                    return Fail("MQTT 主机、有效端口和至少一个订阅主题不能为空。", out error);
                if (value.Mqtt.Topics.Any(item =>
                        string.IsNullOrWhiteSpace(item.Topic) || item.Qos is < 0 or > 2))
                    return Fail("MQTT 主题不能为空，QoS 必须是 0、1 或 2。", out error);
                if (value.Mqtt.Topics.Select(item => item.Topic.Trim())
                    .Distinct(StringComparer.Ordinal).Count() != value.Mqtt.Topics.Count)
                    return Fail("MQTT 订阅主题不能重复。", out error);
                if (value.Mqtt.ProtocolVersion is not ("3.1.1" or "5.0"))
                    return Fail("MQTT 协议版本必须是 3.1.1 或 5.0。", out error);
                break;
            case AcquisitionProtocols.OpcUa:
                if (value.OpcUa is null ||
                    !Uri.TryCreate(value.OpcUa.EndpointUrl, UriKind.Absolute, out var opcUri) ||
                    opcUri.Scheme is not ("opc.tcp" or "https"))
                    return Fail("OPC UA 端点必须是 opc.tcp 或 HTTPS 绝对地址。", out error);
                if (value.OpcUa.PublishingIntervalMs < 1 || value.OpcUa.SamplingIntervalMs < 1)
                    return Fail("OPC UA 发布和采样周期必须大于 0ms。", out error);
                if (value.OpcUa.AuthenticationType is not ("anonymous" or "username" or "certificate"))
                    return Fail("OPC UA 身份认证类型无效。", out error);
                if (value.OpcUa.SecurityMode is not ("none" or "sign" or "sign-and-encrypt") ||
                    value.OpcUa.SecurityPolicy is not ("None" or "Basic256Sha256" or
                        "Aes128_Sha256_RsaOaep" or "Aes256_Sha256_RsaPss"))
                    return Fail("OPC UA 安全模式或安全策略无效。", out error);
                if (value.OpcUa.SecurityMode != "none" &&
                    string.IsNullOrWhiteSpace(value.OpcUa.ClientCertificatePath))
                    return Fail("启用 OPC UA 安全通道时必须配置客户端证书路径。", out error);
                if (value.OpcUa.AuthenticationType == "username" &&
                    string.IsNullOrWhiteSpace(value.OpcUa.Username))
                    return Fail("OPC UA 用户名认证需要填写用户名。", out error);
                if (value.OpcUa.AuthenticationType == "certificate" &&
                    string.IsNullOrWhiteSpace(value.OpcUa.ClientCertificatePath))
                    return Fail("OPC UA 证书认证需要配置客户端证书路径。", out error);
                break;
            case AcquisitionProtocols.ModbusTcp:
                if (value.ModbusTcp is null || string.IsNullOrWhiteSpace(value.ModbusTcp.Host) ||
                    value.ModbusTcp.Port is < 1 or > 65535 || value.ModbusTcp.PollIntervalMs < 1)
                    return Fail("Modbus TCP 主机、端口或读取后等待时间无效。", out error);
                if (value.ModbusTcp.AddressBase is not ("zero-based" or "one-based"))
                    return Fail("Modbus 地址起点必须是 0 基地址或 1 基地址。", out error);
                break;
            case AcquisitionProtocols.MelsecA1E:
                if (value.MelsecA1E is null || string.IsNullOrWhiteSpace(value.MelsecA1E.Host) ||
                    value.MelsecA1E.Port is < 1 or > 65535 || value.MelsecA1E.PollIntervalMs < 1)
                    return Fail("MELSEC 1E 主机、端口或读取后等待时间无效。", out error);
                if (value.MelsecA1E.WordOrderLayout != "A")
                    return Fail("MELSEC 1E 软元件字段顺序必须是 A（FX3U-ENET-ADP A-compatible 1E）。", out error);
                if (value.MelsecA1E.DataCode is not ("binary" or "ascii"))
                    return Fail("MELSEC 1E 通信数据码必须是二进制或 ASCII。", out error);
                break;
        }
        error = string.Empty;
        return true;
    }

    private static bool HasSourceSelector(string protocol, AcquisitionValueMapping item)
        => protocol == AcquisitionProtocols.ModbusTcp
            ? item.ModbusAddress.HasValue &&
              item.ModbusArea is "holding-register" or "input-register" or "coil" or "discrete-input"
            : !string.IsNullOrWhiteSpace(item.SourcePath);

    private static MqttConnection? NormalizeMqtt(MqttConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            ClientId = value.ClientId.Trim(),
            Username = CleanOptional(value.Username),
            PasswordSecretRef = CleanOptional(value.PasswordSecretRef),
            CaCertificatePath = CleanOptional(value.CaCertificatePath),
            ClientCertificatePath = CleanOptional(value.ClientCertificatePath),
            ClientCertificatePasswordSecretRef = CleanOptional(value.ClientCertificatePasswordSecretRef),
            Topics = value.Topics
                .Where(item => !string.IsNullOrWhiteSpace(item.Topic))
                .Select(item => item with { Topic = item.Topic.Trim() })
                .ToArray()
        };

    private static OpcUaConnection? NormalizeOpcUa(OpcUaConnection? value)
        => value is null ? null : value with
        {
            EndpointUrl = value.EndpointUrl.Trim(),
            SecurityMode = value.SecurityMode.Trim().ToLowerInvariant(),
            SecurityPolicy = value.SecurityPolicy.Trim(),
            AuthenticationType = value.AuthenticationType.Trim().ToLowerInvariant(),
            Username = CleanOptional(value.Username),
            PasswordSecretRef = CleanOptional(value.PasswordSecretRef),
            ClientCertificatePath = CleanOptional(value.ClientCertificatePath),
            ClientCertificatePasswordSecretRef = CleanOptional(value.ClientCertificatePasswordSecretRef)
        };

    private static ModbusTcpConnection? NormalizeModbusTcp(ModbusTcpConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            AddressBase = value.AddressBase.Trim().ToLowerInvariant()
        };

    private static McA1EConnection? NormalizeMelsecA1E(McA1EConnection? value)
        => value is null ? null : value with
        {
            Host = value.Host.Trim(),
            DataCode = value.DataCode.Trim().ToLowerInvariant(),
            WordOrderLayout = value.WordOrderLayout.Trim().ToUpperInvariant()
        };

    private static string? CleanOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AcquisitionRecipeMapping? NormalizeRecipe(AcquisitionRecipeMapping? value)
    {
        if (value is null) return null;
        return value with
        {
            EventType = value.EventType.Trim(),
            IdPath = value.IdPath.Trim(),
            VersionPath = value.VersionPath.Trim(),
            NamePath = string.IsNullOrWhiteSpace(value.NamePath) ? null : value.NamePath.Trim(),
            ParametersPath = value.ParametersPath.Trim(),
            ParameterMappings = value.ParameterMappings.Select(item => item with
            {
                DataItemCode = NormalizeCode(item.DataItemCode),
                SourcePath = item.SourcePath.Trim()
            }).ToArray()
        };
    }

    private static AcquisitionLifecycleMapping? NormalizeLifecycle(AcquisitionLifecycleMapping? value)
    {
        if (value is null) return null;
        return value with
        {
            Mode = value.Mode.Trim().ToLowerInvariant(),
            CorrelationIdContextKey = string.IsNullOrWhiteSpace(value.CorrelationIdContextKey)
                ? null
                : NormalizeCode(value.CorrelationIdContextKey),
            ActiveContextKey = string.IsNullOrWhiteSpace(value.ActiveContextKey)
                ? null
                : NormalizeCode(value.ActiveContextKey),
            ActiveValue = value.ActiveValue.Trim(),
            StartedEventType = value.StartedEventType.Trim().ToLowerInvariant(),
            CompletedEventType = value.CompletedEventType.Trim().ToLowerInvariant(),
            StepChangedEventType = value.StepChangedEventType.Trim().ToLowerInvariant()
        };
    }

    private static bool ValidateMappings(AcquisitionProfile profile, ProcessDataModel model, out string error)
    {
        var dataItems = model.Acquisition.DataItems.ToDictionary(item => item.Code, StringComparer.Ordinal);
        var unknown = profile.ValueMappings.FirstOrDefault(item => !dataItems.ContainsKey(item.DataItemCode));
        if (unknown is not null) return Fail($"数据项未在工艺数据模型中定义：{unknown.DataItemCode}。", out error);
        if (profile.Status == ConfigurationStatuses.Published)
        {
            var missing = dataItems.Values.FirstOrDefault(item => !item.Nullable &&
                profile.ValueMappings.All(mapping => mapping.DataItemCode != item.Code));
            if (missing is not null) return Fail($"缺少必填数据项映射：{missing.Code}。", out error);
        }
        if (profile.Recipe is not null)
        {
            if (string.IsNullOrWhiteSpace(profile.Recipe.IdPath) ||
                string.IsNullOrWhiteSpace(profile.Recipe.VersionPath) ||
                string.IsNullOrWhiteSpace(profile.Recipe.ParametersPath))
                return Fail("启用配方采集后，配方编号、版本和参数路径不能为空。", out error);
            if (profile.Recipe.ParameterMappings.Any(item =>
                    !CodePattern().IsMatch(item.DataItemCode) || string.IsNullOrWhiteSpace(item.SourcePath)) ||
                profile.Recipe.ParameterMappings.Select(item => item.DataItemCode)
                    .Distinct(StringComparer.Ordinal).Count() != profile.Recipe.ParameterMappings.Count)
                return Fail("配方参数映射无效或重复。", out error);
            var definitions = model.RecipeParameters.ToDictionary(item => item.Code, StringComparer.Ordinal);
            var unknownParameter = profile.Recipe.ParameterMappings.FirstOrDefault(item => !definitions.ContainsKey(item.DataItemCode));
            if (unknownParameter is not null)
                return Fail($"配方参数未在工艺数据模型中定义：{unknownParameter.DataItemCode}。", out error);
        }
        if (profile.Lifecycle is not null)
        {
            var lifecycle = profile.Lifecycle;
            if (lifecycle.Mode != "discrete-cycle" ||
                (!string.IsNullOrWhiteSpace(lifecycle.CorrelationIdContextKey) &&
                 !CodePattern().IsMatch(lifecycle.CorrelationIdContextKey)) ||
                !EventTypePattern().IsMatch(lifecycle.StartedEventType) ||
                !EventTypePattern().IsMatch(lifecycle.CompletedEventType) ||
                !EventTypePattern().IsMatch(lifecycle.StepChangedEventType))
            {
                return Fail("周期边界配置无效。", out error);
            }
            if (!string.IsNullOrWhiteSpace(lifecycle.CorrelationIdContextKey) &&
                profile.ContextMappings.All(item =>
                    item.ContextKey != lifecycle.CorrelationIdContextKey))
            {
                return Fail($"周期边界缺少关联号上下文映射：{lifecycle.CorrelationIdContextKey}。", out error);
            }
            if (string.IsNullOrWhiteSpace(lifecycle.CorrelationIdContextKey) &&
                string.IsNullOrWhiteSpace(lifecycle.ActiveContextKey))
            {
                return Fail("周期边界必须配置生产状态上下文映射，由 Edge 自动生成周期关联号。", out error);
            }
            if (!string.IsNullOrWhiteSpace(lifecycle.ActiveContextKey) &&
                (string.IsNullOrWhiteSpace(lifecycle.ActiveValue) ||
                 profile.ContextMappings.All(item =>
                     item.ContextKey != lifecycle.ActiveContextKey)))
            {
                return Fail(
                    $"周期边界缺少运行激活状态上下文映射：{lifecycle.ActiveContextKey}。",
                    out error);
            }
        }
        error = string.Empty;
        return true;
    }

    private static bool SamePayload<T>(T left, T right)
        => JsonSerializer.Serialize(left) == JsonSerializer.Serialize(right);
    private static string NormalizeCode(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static bool Fail(string message, out string error) { error = message; return false; }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();

    [GeneratedRegex("^[a-z][a-z0-9]*(?:\\.[a-z][a-z0-9_]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex EventTypePattern();
}
