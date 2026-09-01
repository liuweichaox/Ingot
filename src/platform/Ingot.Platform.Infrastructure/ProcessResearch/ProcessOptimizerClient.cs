
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ingot.Platform.Application.ProcessResearch;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

// 适配外部优化器 HTTP 协议；应用层仅依赖 IProcessOptimizerClient 端口。
public sealed class ProcessOptimizerOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "http://127.0.0.1:8100";
    public int RequestTimeoutSeconds { get; init; } = 300;
    public int CircuitFailureThreshold { get; init; } = 3;
    public int CircuitBreakSeconds { get; init; } = 30;
}

public sealed class ProcessOptimizerClient(
    HttpClient httpClient,
    IOptions<ProcessOptimizerOptions> options) : IProcessOptimizerClient
{
    private readonly ProcessOptimizerOptions _options = options.Value;

    public async Task<OptimizerSuggestionResponse> SuggestAsync(
        OptimizerSuggestionCall request,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
            throw new ProcessResearchRuleException("优化服务未启用。");
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                "v1/suggestions",
                request,
                ct).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ProcessOptimizerUnavailableException("优化服务暂时不可用。", exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new ProcessOptimizerUnavailableException("优化服务请求超时。", exception);
        }
        using (response)
        {
            if ((int)response.StatusCode >= 500)
                throw new ProcessOptimizerUnavailableException(
                    $"优化服务暂时不可用（{(int)response.StatusCode}）。");
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (detail.Length > 1000)
                    detail = detail[..1000];
                throw new ProcessResearchRuleException(
                    $"优化服务拒绝请求（{(int)response.StatusCode}）：{detail}");
            }
            var result = await response.Content.ReadFromJsonAsync<OptimizerSuggestionResponse>(
                    cancellationToken: ct)
                .ConfigureAwait(false)
                ?? throw new ProcessResearchRuleException("优化服务返回了空响应。");
            if (result.StatePersisted)
                throw new ProcessResearchRuleException("优化服务违反无状态契约。");
            if (result.Suggestions.Count == 0 || string.IsNullOrWhiteSpace(result.ModelVersion))
                throw new ProcessResearchRuleException("优化服务响应缺少模型版本或建议。");
            if (!string.Equals(
                    result.FeatureSetId,
                    request.Campaign.FeatureSetId,
                    StringComparison.Ordinal)
                || result.FeatureSetVersion != request.Campaign.FeatureSetVersion
                || result.DerivedFeatureCount != request.Campaign.DerivedFeatures.Count)
            {
                throw new ProcessResearchRuleException("优化服务响应的特征集契约与请求不一致。");
            }
            return result;
        }
    }

    public async Task<OptimizerDesignResponse> DesignAsync(
        OptimizerDesignCall request,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
            throw new ProcessResearchRuleException("优化服务未启用。");
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync("v1/designs", request, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ProcessOptimizerUnavailableException("配方建议服务暂时不可用。", exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new ProcessOptimizerUnavailableException("配方建议服务请求超时。", exception);
        }
        using (response)
        {
            if ((int)response.StatusCode >= 500)
                throw new ProcessOptimizerUnavailableException(
                    $"配方建议服务暂时不可用（{(int)response.StatusCode}）。");
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                throw new ProcessResearchRuleException(
                    $"配方建议服务拒绝请求（{(int)response.StatusCode}）：{detail[..Math.Min(detail.Length, 1000)]}");
            }
            var result = await response.Content.ReadFromJsonAsync<OptimizerDesignResponse>(
                    cancellationToken: ct)
                .ConfigureAwait(false)
                ?? throw new ProcessResearchRuleException("配方建议服务返回了空响应。");
            if (result.StatePersisted || result.Runs.Count == 0)
                throw new ProcessResearchRuleException("配方建议服务响应无效或违反无状态契约。");
            return result;
        }
    }

    public async Task<ProcessDiagnosisResponse> DiagnoseAsync(
        ProcessDiagnosisCall request,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
            throw new ProcessResearchRuleException("数值分析服务未启用。");
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                "v1/diagnosis",
                request,
                ct).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ProcessOptimizerUnavailableException("数值分析服务暂时不可用。", exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new ProcessOptimizerUnavailableException("数值分析服务请求超时。", exception);
        }
        using (response)
        {
            if ((int)response.StatusCode >= 500)
                throw new ProcessOptimizerUnavailableException(
                    $"数值分析服务暂时不可用（{(int)response.StatusCode}）。");
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (detail.Length > 1000)
                    detail = detail[..1000];
                throw new ProcessResearchRuleException(
                    $"数值分析服务拒绝诊断请求（{(int)response.StatusCode}）：{detail}");
            }
            return await response.Content.ReadFromJsonAsync<ProcessDiagnosisResponse>(
                    cancellationToken: ct)
                .ConfigureAwait(false)
                ?? throw new ProcessResearchRuleException("数值分析服务返回了空诊断响应。");
        }
    }

    public async Task<JsonElement> ReplayHistoryAsync(
        OptimizerHistoricalReplayCall request,
        CancellationToken ct = default)
    {
        if (!_options.Enabled)
            throw new ProcessResearchRuleException("优化服务未启用。");
        HttpResponseMessage response;
        try
        {
            response = await httpClient.PostAsJsonAsync(
                "v1/historical-replay", request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            throw new ProcessOptimizerUnavailableException("历史回放服务暂时不可用。", exception);
        }
        catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
        {
            throw new ProcessOptimizerUnavailableException("历史回放请求超时。", exception);
        }
        using (response)
        {
            if ((int)response.StatusCode >= 500)
                throw new ProcessOptimizerUnavailableException(
                    $"历史回放服务暂时不可用（{(int)response.StatusCode}）。");
            if (!response.IsSuccessStatusCode)
            {
                var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                if (detail.Length > 1000)
                    detail = detail[..1000];
                throw new ProcessResearchRuleException(
                    $"优化服务拒绝历史回放（{(int)response.StatusCode}）：{detail}");
            }
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false),
                cancellationToken: ct).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.TryGetProperty("state_persisted", out var persisted) && persisted.GetBoolean())
                throw new ProcessResearchRuleException("历史回放服务违反无状态契约。");
            if (!root.TryGetProperty("engine_policy", out _) ||
                !root.TryGetProperty("step_traces", out _))
                throw new ProcessResearchRuleException("历史回放响应缺少生产模型路径或逐步审计轨迹。");
            return root.Clone();
        }
    }
}
