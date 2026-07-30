using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

public sealed class ProcessOptimizerOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "http://127.0.0.1:8100";
    public int RequestTimeoutSeconds { get; init; } = 30;
}

public sealed record OptimizerVariableInput(
    string Name,
    double Low,
    double High,
    string Unit);

public sealed record OptimizerObjectiveInput
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public double? Threshold { get; init; }
    public double? Target { get; init; }
    public double? Tol { get; init; }
    public double? Lower { get; init; }
    public double? Upper { get; init; }
    public double Weight { get; init; } = 1;
    public string Unit { get; init; } = "";
}

public sealed record OptimizerConstraintInput
{
    public required string Variable { get; init; }
    public required string Operator { get; init; }
    public double Limit { get; init; }

    [JsonPropertyName("safety_critical")]
    public bool SafetyCritical { get; init; }
}

public sealed record OptimizerOutcomeConstraintInput
{
    public required string Name { get; init; }
    public required string Operator { get; init; }
    public double Limit { get; init; }
    public string Unit { get; init; } = "";

    [JsonPropertyName("safety_critical")]
    public bool SafetyCritical { get; init; }

    [JsonPropertyName("minimum_probability")]
    public double MinimumProbability { get; init; } = 0.95;
}

public sealed record OptimizerCampaignInput
{
    public required string Name { get; init; }
    [JsonPropertyName("process_profile")]
    public string ProcessProfile { get; init; } = "generic";
    [JsonPropertyName("decision_intent")]
    public string DecisionIntent { get; init; } = "reach-specification";
    [JsonPropertyName("hypothesis_variables")]
    public IReadOnlyList<string> HypothesisVariables { get; init; } = [];
    public IReadOnlyList<OptimizerVariableInput> Variables { get; init; } = [];
    public IReadOnlyList<OptimizerObjectiveInput> Objectives { get; init; } = [];
    public IReadOnlyList<OptimizerConstraintInput> Constraints { get; init; } = [];

    [JsonPropertyName("outcome_constraints")]
    public IReadOnlyList<OptimizerOutcomeConstraintInput> OutcomeConstraints { get; init; } = [];

    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();
}

public sealed record OptimizerObservationInput
{
    public IReadOnlyDictionary<string, double> Params { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> Outcomes { get; init; } =
        new Dictionary<string, double>();

    [JsonPropertyName("constraint_outcomes")]
    public IReadOnlyDictionary<string, double> ConstraintOutcomes { get; init; } =
        new Dictionary<string, double>();

    [JsonPropertyName("process_features")]
    public IReadOnlyDictionary<string, double> ProcessFeatures { get; init; } =
        new Dictionary<string, double>();
}

public sealed record OptimizerSuggestionCall
{
    public required OptimizerCampaignInput Campaign { get; init; }
    public IReadOnlyList<OptimizerObservationInput> Observations { get; init; } = [];

    [JsonPropertyName("pending_points")]
    public IReadOnlyList<IReadOnlyDictionary<string, double>> PendingPoints { get; init; } = [];

    [JsonPropertyName("candidate_pool")]
    public IReadOnlyList<IReadOnlyDictionary<string, double>>? CandidatePool { get; init; }

    [JsonPropertyName("top_k")]
    public int TopK { get; init; } = 3;

    public int Seed { get; init; }

    [JsonPropertyName("n_random")]
    public int CandidateCount { get; init; } = 4000;

    [JsonPropertyName("n_samples")]
    public int PosteriorSampleCount { get; init; } = 256;
}

public sealed record OptimizerObjectivePrediction
{
    public double Mean { get; init; }

    [JsonPropertyName("standard_deviation")]
    public double StandardDeviation { get; init; }

    [JsonPropertyName("lower_95")]
    public double Lower95 { get; init; }

    [JsonPropertyName("upper_95")]
    public double Upper95 { get; init; }

    public string Unit { get; init; } = "";
}

public sealed record OptimizerSuggestionOutput
{
    [JsonPropertyName("recommended_params")]
    public IReadOnlyDictionary<string, double> RecommendedParameters { get; init; } =
        new Dictionary<string, double>();

    [JsonPropertyName("objective_predictions")]
    public IReadOnlyDictionary<string, OptimizerObjectivePrediction> Predictions { get; init; } =
        new Dictionary<string, OptimizerObjectivePrediction>();

    [JsonPropertyName("constraint_predictions")]
    public IReadOnlyDictionary<string, OptimizerObjectivePrediction> ConstraintPredictions { get; init; } =
        new Dictionary<string, OptimizerObjectivePrediction>();

    [JsonPropertyName("predicted_distance_to_spec")]
    public double? PredictedDistanceToSpec { get; init; }

    [JsonPropertyName("feasibility_probability")]
    public double? FeasibilityProbability { get; init; }

    [JsonPropertyName("acquisition_value")]
    public double? AcquisitionValue { get; init; }

    [JsonPropertyName("cold_start")]
    public bool ColdStart { get; init; }

    [JsonPropertyName("model_version")]
    public string ModelVersion { get; init; } = "";

    public string Rationale { get; init; } = "";
}

public sealed record OptimizerSuggestionResponse
{
    [JsonPropertyName("model_version")]
    public string ModelVersion { get; init; } = "";

    [JsonPropertyName("observation_count")]
    public int ObservationCount { get; init; }

    public IReadOnlyList<OptimizerSuggestionOutput> Suggestions { get; init; } = [];

    [JsonPropertyName("state_persisted")]
    public bool StatePersisted { get; init; }
}

public sealed record ProcessDiagnosticFeatureInput
{
    [JsonPropertyName("data_source")]
    public required string DataSource { get; init; }

    [JsonPropertyName("source_kind")]
    public required string SourceKind { get; init; }

    public required string Actionability { get; init; }
}

public sealed record ProcessDiagnosticObservationInput
{
    [JsonPropertyName("run_key")]
    public required string RunKey { get; init; }

    public double Outcome { get; init; }
    public double Weight { get; init; } = 1;
    public IReadOnlyDictionary<string, double> Values { get; init; } =
        new Dictionary<string, double>();
    public IReadOnlyDictionary<string, string> Context { get; init; } =
        new Dictionary<string, string>();

    [JsonPropertyName("occurred_at")]
    public double OccurredAt { get; init; }
}

public sealed record ProcessDiagnosisCall
{
    [JsonPropertyName("outcome_kind")]
    public string OutcomeKind { get; init; } = "binary";
    public IReadOnlyList<ProcessDiagnosticFeatureInput> Features { get; init; } = [];
    public IReadOnlyList<ProcessDiagnosticObservationInput> Observations { get; init; } = [];
    public int Seed { get; init; }
}

public sealed record ProcessDiagnosticCandidateOutput
{
    [JsonPropertyName("data_source")]
    public required string DataSource { get; init; }
    [JsonPropertyName("adjusted_effect")]
    public double AdjustedEffect { get; init; }
    [JsonPropertyName("model_importance")]
    public double ModelImportance { get; init; }
    [JsonPropertyName("stability_selection_rate")]
    public double StabilitySelectionRate { get; init; }
    [JsonPropertyName("sign_stability")]
    public double SignStability { get; init; }
    [JsonPropertyName("rank_score")]
    public double RankScore { get; init; }
}

public sealed record ProcessDiagnosticInteractionOutput
{
    [JsonPropertyName("left_data_source")]
    public required string LeftDataSource { get; init; }
    [JsonPropertyName("right_data_source")]
    public required string RightDataSource { get; init; }
    [JsonPropertyName("adjusted_effect")]
    public double AdjustedEffect { get; init; }
    [JsonPropertyName("stability_selection_rate")]
    public double StabilitySelectionRate { get; init; }
    [JsonPropertyName("rank_score")]
    public double RankScore { get; init; }
}

public sealed record ProcessDiagnosisResponse
{
    [JsonPropertyName("algorithm_version")]
    public required string AlgorithmVersion { get; init; }
    [JsonPropertyName("model_family")]
    public required string ModelFamily { get; init; }
    [JsonPropertyName("adjustment_method")]
    public required string AdjustmentMethod { get; init; }
    [JsonPropertyName("cross_validation_score")]
    public double? CrossValidationScore { get; init; }
    [JsonPropertyName("fold_count")]
    public int FoldCount { get; init; }
    [JsonPropertyName("stability_runs")]
    public int StabilityRuns { get; init; }
    [JsonPropertyName("context_variables")]
    public IReadOnlyList<string> ContextVariables { get; init; } = [];
    public IReadOnlyList<ProcessDiagnosticCandidateOutput> Candidates { get; init; } = [];
    public IReadOnlyList<ProcessDiagnosticInteractionOutput> Interactions { get; init; } = [];
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public interface IProcessOptimizerClient
{
    Task<OptimizerSuggestionResponse> SuggestAsync(
        OptimizerSuggestionCall request,
        CancellationToken ct = default);

    Task<ProcessDiagnosisResponse> DiagnoseAsync(
        ProcessDiagnosisCall request,
        CancellationToken ct = default)
        => throw new NotSupportedException("当前优化客户端不支持多变量诊断。");
}

public sealed class ProcessOptimizerUnavailableException(
    string message,
    Exception? innerException = null) : HttpRequestException(message, innerException);

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
}
