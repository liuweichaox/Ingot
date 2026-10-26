// 启动时只读探查当前 OpenAI-compatible 模型服务及模型标识。
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Agent.Providers;

public sealed class OpenAiCompatibleCapabilityProbe(
    IHttpClientFactory clients,
    IOptions<ChatOptions> options,
    IModelServiceConfigurationProvider configurationProvider,
    ILogger<OpenAiCompatibleCapabilityProbe> logger) : IHostedService, IModelServiceConnectionTester
{
    private readonly ChatOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = configurationProvider.Current;
        if (!settings.Enabled || !_options.ProbeOnStartup ||
            string.Equals(settings.Provider, "Deterministic", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            return;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            logger.LogWarning("跳过 {Provider} 模型探查：尚未通过管理页面配置 API key", settings.Provider);
            return;
        }

        var result = await TestAsync(settings, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            logger.LogWarning(
                "OpenAI-compatible 能力探查未通过，保留服务运行并等待管理员修正配置：{Message}",
                result.Message);
            return;
        }

        logger.LogInformation(
            "OpenAI-compatible 能力探查通过：快速模型 {FastModel}，推理模型 {ReasoningModel}",
            settings.FastModel,
            settings.ReasoningModel);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<ModelServiceConnectionTestResult> TestAsync(
        ModelServiceConnectionSettings settings,
        CancellationToken ct = default)
    {
        if (string.Equals(settings.Provider, "Deterministic", StringComparison.OrdinalIgnoreCase))
            return new(true, "当前为确定性模型，无需测试外部连接。");
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            return new(false, "API 根地址不能为空。");
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            return new(false, "API key 不能为空。");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.ProbeTimeoutSeconds, 1, 60)));
            var client = clients.CreateClient(nameof(OpenAiCompatibleCapabilityProbe));
            var probe = BuildCompletionProbe(settings);
            using var request = new HttpRequestMessage(HttpMethod.Post, probe.Uri)
            {
                Content = new StringContent(probe.Body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
            using var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new(false, $"模型服务返回 HTTP {(int)response.StatusCode}，请检查协议、模型、API 地址和 API key。");
            if (string.IsNullOrWhiteSpace(body))
                return new(false, "模型服务连接成功，但没有返回有效响应。");
            try
            {
                using var _ = JsonDocument.Parse(body);
            }
            catch (JsonException)
            {
                return new(false, "模型服务连接成功，但响应不是有效 JSON。请检查协议类型。");
            }

            return new(true, $"连接成功，已验证 {settings.Protocol} 协议和模型 {settings.FastModel} 可用。");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, "连接测试超时，请检查地址、网络或服务状态。");
        }
        catch (HttpRequestException)
        {
            return new(false, "无法连接模型服务，请检查 API 地址和网络连接。");
        }
        catch (InvalidOperationException exception)
        {
            return new(false, exception.Message);
        }
    }

    public static Uri BuildModelsUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException(
                "Chat:BaseUrl 必须是无用户信息、查询或片段的绝对 HTTP 或 HTTPS API 根地址。");
        }
        return new Uri($"{endpoint.ToString().TrimEnd('/')}/models", UriKind.Absolute);
    }

    private static (Uri Uri, string Body) BuildCompletionProbe(ModelServiceConnectionSettings settings)
    {
        var endpoint = Uri.TryCreate(settings.BaseUrl?.Trim(), UriKind.Absolute, out var baseUri)
            ? baseUri
            : throw new InvalidOperationException("Chat:BaseUrl 必须是无用户信息、查询或片段的绝对 HTTP 或 HTTPS API 根地址。");
        if ((endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) ||
            !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new InvalidOperationException(
                "Chat:BaseUrl 必须是无用户信息、查询或片段的绝对 HTTP 或 HTTPS API 根地址。");
        }

        var root = endpoint.ToString().TrimEnd('/');
        if (string.Equals(settings.Protocol, OpenAiCompatibleModelConfiguration.ResponsesProtocol, StringComparison.OrdinalIgnoreCase))
        {
            return (
                new Uri($"{root}/responses", UriKind.Absolute),
                JsonSerializer.Serialize(new
                {
                    model = settings.FastModel,
                    input = "Reply with a short JSON object containing ok=true.",
                    max_output_tokens = 32
                }));
        }

        if (string.Equals(settings.Protocol, OpenAiCompatibleModelConfiguration.ChatCompletionsProtocol, StringComparison.OrdinalIgnoreCase))
        {
            return (
                new Uri($"{root}/chat/completions", UriKind.Absolute),
                JsonSerializer.Serialize(new
                {
                    model = settings.FastModel,
                    messages = new[] { new { role = "user", content = "Reply with a short JSON object containing ok=true." } },
                    max_tokens = 32,
                    temperature = 0
                }));
        }

        throw new InvalidOperationException("Chat:Protocol 必须是 Responses 或 ChatCompletions。");
    }

    public static IReadOnlySet<string> ReadModelIds(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("OpenAI-compatible /models 响应缺少 data 数组。");
        return data.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.Object &&
                                  item.TryGetProperty("id", out var id) &&
                                  id.ValueKind == JsonValueKind.String &&
                                  !string.IsNullOrWhiteSpace(id.GetString()))
            .Select(static item => item.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
    }
}
