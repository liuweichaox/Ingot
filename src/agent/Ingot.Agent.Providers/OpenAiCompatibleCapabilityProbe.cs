using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Agent.Providers;

public sealed class OpenAiCompatibleCapabilityProbe(
    IHttpClientFactory clients,
    IOptions<ChatOptions> options,
    ILogger<OpenAiCompatibleCapabilityProbe> logger) : IHostedService
{
    private readonly ChatOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || !_options.ProbeOnStartup || string.IsNullOrWhiteSpace(_options.BaseUrl))
            return;

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("探查 OpenAI-compatible 服务时缺少 OPENAI_API_KEY。");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.ProbeTimeoutSeconds, 1, 60)));
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildModelsUri(_options.BaseUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await clients.CreateClient(nameof(OpenAiCompatibleCapabilityProbe))
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"OpenAI-compatible 能力探查失败（HTTP {(int)response.StatusCode}）。");

        var available = ReadModelIds(body);
        var required = new[] { _options.FastModel, _options.ReasoningModel }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var missing = required.Where(model => !available.Contains(model)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"OpenAI-compatible 服务未报告已配置模型：{string.Join("、", missing)}。");

        logger.LogInformation(
            "OpenAI-compatible 能力探查通过：快速模型 {FastModel}，推理模型 {ReasoningModel}",
            _options.FastModel,
            _options.ReasoningModel);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public static Uri BuildModelsUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Chat:BaseUrl 必须是绝对 HTTP 或 HTTPS 地址。");
        if (!endpoint.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Chat:BaseUrl 必须指向 OpenAI-compatible /v1 API 根路径。");
        return new Uri($"{endpoint.ToString().TrimEnd('/')}/models", UriKind.Absolute);
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
