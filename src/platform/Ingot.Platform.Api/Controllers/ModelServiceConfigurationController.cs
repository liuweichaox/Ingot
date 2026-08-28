// 管理模型服务连接；API key 只接受写入且永不回显。
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ModelServices;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/model-service-configuration")]
public sealed class ModelServiceConfigurationController(
    PlatformUserResolver userResolver,
    ModelServiceConfigurationApplication application,
    ILogger<ModelServiceConfigurationController> logger) : PlatformApiController
{
    private const int MaxProviderLength = 100;
    private const int MaxModelLength = 200;
    private const int MaxBaseUrlLength = 2048;
    private const int MaxApiKeyLength = 8192;

    [HttpGet]
    public IActionResult Get()
        => DeniedAdmin() ?? Ok(application.GetCurrent());

    [HttpPut]
    public async Task<IActionResult> Save(
        [FromBody] SaveModelServiceConfigurationCommand? request,
        CancellationToken ct)
    {
        var denied = DeniedAdmin();
        if (denied is not null) return denied;
        if (request is null)
            return InvalidRequest("模型服务配置不能为空。");
        if (string.IsNullOrWhiteSpace(request.Provider) || request.Provider.Trim().Length > MaxProviderLength)
            return InvalidRequest($"provider 必须为 1 到 {MaxProviderLength} 位。");
        if (request.Enabled && string.Equals(request.Provider, "Deterministic", StringComparison.OrdinalIgnoreCase))
            return InvalidRequest("启用外部模型服务时 provider 不能是 Deterministic。");
        if (!string.Equals(request.Protocol, "Responses", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Protocol, "ChatCompletions", StringComparison.OrdinalIgnoreCase))
            return InvalidRequest("protocol 必须是 Responses 或 ChatCompletions。");
        if (!ValidModel(request.FastModel) || !ValidModel(request.ReasoningModel))
            return InvalidRequest($"模型标识必须为 1 到 {MaxModelLength} 位。");
        if (request.Enabled && string.IsNullOrWhiteSpace(request.BaseUrl))
            return InvalidRequest("启用模型服务时 baseUrl 不能为空。");
        if (!string.IsNullOrWhiteSpace(request.BaseUrl) && !ValidBaseUrl(request.BaseUrl))
            return InvalidRequest("baseUrl 必须是无用户信息、查询或片段的绝对 HTTP 或 HTTPS API 根地址。");
        if (request.ClearApiKey && !string.IsNullOrWhiteSpace(request.ApiKey))
            return InvalidRequest("不能同时清除和替换 API key。");
        if (request.ApiKey is { Length: > MaxApiKeyLength })
            return InvalidRequest($"API key 不能超过 {MaxApiKeyLength} 位。");
        if (request.Enabled && request.ClearApiKey)
            return InvalidRequest("启用模型服务时不能清除 API key。");
        var current = application.GetCurrent();
        if (request.Enabled && !current.HasApiKey && string.IsNullOrWhiteSpace(request.ApiKey))
            return InvalidRequest("启用模型服务前必须填写 API key。");

        var actor = userResolver.Resolve(User) ?? "unknown";
        try
        {
            var saved = await application.SaveAsync(request, actor, ct).ConfigureAwait(false);
            logger.LogInformation(
                "ModelServiceAudit action=configuration.updated actorUserId={ActorUserId} " +
                "provider={Provider} protocol={Protocol} enabled={Enabled} apiKeyChanged={ApiKeyChanged}",
                actor,
                saved.Provider,
                saved.Protocol,
                saved.Enabled,
                request.ClearApiKey || !string.IsNullOrWhiteSpace(request.ApiKey));
            return Ok(saved);
        }
        catch (InvalidOperationException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }

    private IActionResult? DeniedAdmin()
    {
        var identity = userResolver.ResolveIdentity(User);
        if (identity is null)
            return AuthenticationRequired("需要平台统一认证。");
        return identity.HasAnyRole(PlatformRoles.PlatformAdministrator) ? null : AuthorizationDenied();
    }

    private static bool ValidModel(string value)
        => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= MaxModelLength;

    private static bool ValidBaseUrl(string value)
        => value.Length <= MaxBaseUrlLength &&
           Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
           uri.Scheme is "http" or "https" &&
           string.IsNullOrEmpty(uri.UserInfo) &&
           string.IsNullOrEmpty(uri.Query) &&
           string.IsNullOrEmpty(uri.Fragment);
}
