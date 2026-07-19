using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ingot.Agent;

public sealed class DefaultPlanValidator(IOptions<ChatOptions> chatOptions) : IPlanValidator
{
    private readonly ChatOptions _chatOptions = chatOptions.Value;

    public bool TryValidate(
        string surface,
        AnalysisPlan plan,
        IReadOnlyDictionary<string, IAnalysisTool> tools,
        out string error)
    {
        if (plan.ToolCalls.Count == 0)
        {
            error = "计划没有选择任何已授权工具。";
            return false;
        }

        if (!string.Equals(surface, ProductSurfaces.Chat, StringComparison.Ordinal))
        {
            error = "计划包含无效的产品面。";
            return false;
        }
        var maxToolCalls = Math.Clamp(_chatOptions.MaxToolCalls, 1, 8);

        if (plan.ToolCalls.Count > maxToolCalls)
        {
            error = "分析计划超过允许的工具调用次数。";
            return false;
        }

        foreach (var call in plan.ToolCalls)
        {
            if (!tools.TryGetValue(call.Tool, out var tool))
            {
                error = $"分析计划请求了未授权工具: {call.Tool}";
                return false;
            }

            if (!string.Equals(tool.Definition.Surface, surface, StringComparison.Ordinal) ||
                !string.Equals(tool.Definition.Purpose, RunPurposes.ForSurface(surface), StringComparison.Ordinal))
            {
                error = $"工具 {call.Tool} 不属于当前产品面。";
                return false;
            }

            if (!TryValidateArguments(call, tool.Definition.InputSchema, out error))
                return false;
        }

        if (plan.From.HasValue && plan.To.HasValue && plan.From > plan.To)
        {
            error = "分析时间范围无效。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateArguments(
        AnalysisToolCall call,
        JsonElement schema,
        out string error)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("type", out var rootType) ||
            rootType.ValueKind != JsonValueKind.String ||
            !string.Equals(rootType.GetString(), "object", StringComparison.Ordinal))
        {
            error = $"工具 {call.Tool} 的输入 Schema 必须是 object。";
            return false;
        }

        var properties = default(JsonElement);
        if (schema.TryGetProperty("properties", out var propertiesElement))
        {
            if (propertiesElement.ValueKind != JsonValueKind.Object)
            {
                error = $"工具 {call.Tool} 的 properties Schema 无效。";
                return false;
            }

            properties = propertiesElement;
        }

        var rejectAdditional = false;
        if (schema.TryGetProperty("additionalProperties", out var additional))
        {
            if (additional.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                error = $"工具 {call.Tool} 的 additionalProperties Schema 无效。";
                return false;
            }

            rejectAdditional = additional.ValueKind == JsonValueKind.False;
        }

        if (schema.TryGetProperty("required", out var required))
        {
            if (required.ValueKind != JsonValueKind.Array)
            {
                error = $"工具 {call.Tool} 的 required Schema 无效。";
                return false;
            }

            foreach (var item in required.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
                    error = $"工具 {call.Tool} 的 required Schema 无效。";
                    return false;
                }

                var name = item.GetString()!;
                if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(name, out _))
                {
                    error = $"工具 {call.Tool} 的必填参数 {name} 未在 properties 中声明。";
                    return false;
                }
                if (!call.Arguments.TryGetValue(name, out var value) || value is null)
                {
                    error = $"工具 {call.Tool} 缺少必填参数: {name}";
                    return false;
                }
            }
        }

        foreach (var (name, value) in call.Arguments)
        {
            if (properties.ValueKind != JsonValueKind.Object ||
                !properties.TryGetProperty(name, out var propertySchema))
            {
                if (rejectAdditional)
                {
                    error = $"工具 {call.Tool} 包含未声明参数: {name}";
                    return false;
                }

                continue;
            }

            if (!TryValidateString(call.Tool, name, value, propertySchema, out error))
                return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateString(
        string tool,
        string name,
        string? value,
        JsonElement schema,
        out string error)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            !string.Equals(type.GetString(), "string", StringComparison.Ordinal))
        {
            error = $"工具 {tool} 的参数 {name} 使用了不支持的 Schema。";
            return false;
        }

        if (value is null)
        {
            error = $"工具 {tool} 的参数 {name} 必须是字符串。";
            return false;
        }

        var length = value.EnumerateRunes().Count();
        if (!TryReadNonNegativeInteger(schema, "minLength", out var minLength, out var lengthError))
        {
            error = $"工具 {tool} 的参数 {name} Schema 无效: {lengthError}";
            return false;
        }
        if (!TryReadNonNegativeInteger(schema, "maxLength", out var maxLength, out lengthError))
        {
            error = $"工具 {tool} 的参数 {name} Schema 无效: {lengthError}";
            return false;
        }
        if (minLength.HasValue && maxLength.HasValue && minLength > maxLength)
        {
            error = $"工具 {tool} 的参数 {name} Schema 无效: minLength 不得大于 maxLength";
            return false;
        }
        if (minLength.HasValue && length < minLength.Value)
        {
            error = $"工具 {tool} 的参数 {name} 长度不得小于 {minLength.Value}。";
            return false;
        }
        if (maxLength.HasValue && length > maxLength.Value)
        {
            error = $"工具 {tool} 的参数 {name} 长度不得超过 {maxLength.Value}。";
            return false;
        }

        if (schema.TryGetProperty("enum", out var enumValues))
        {
            if (enumValues.ValueKind != JsonValueKind.Array ||
                enumValues.EnumerateArray().Any(static item => item.ValueKind != JsonValueKind.String))
            {
                error = $"工具 {tool} 的参数 {name} enum Schema 无效。";
                return false;
            }

            if (!enumValues.EnumerateArray()
                    .Any(item => string.Equals(item.GetString(), value, StringComparison.Ordinal)))
            {
                error = $"工具 {tool} 的参数 {name} 不在允许值范围内。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadNonNegativeInteger(
        JsonElement schema,
        string property,
        out int? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!schema.TryGetProperty(property, out var element))
            return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var parsed) || parsed < 0)
        {
            error = $"{property} 必须是非负整数";
            return false;
        }

        value = parsed;
        return true;
    }
}

public sealed class DefaultEvidenceVerifier : IEvidenceVerifier
{
    private const int MaxCharts = 8;
    private const int MaxLabels = 500;
    private const int MaxSeries = 16;
    private static readonly IReadOnlySet<string> AllowedChartTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "line",
        "bar",
        "scatter",
        "histogram",
        "boxplot"
    };
    public bool TryVerify(
        IReadOnlyList<AnalysisToolResult> results,
        out IReadOnlyList<EvidenceRef> evidence,
        out string error)
    {
        evidence = results.SelectMany(static result => result.Evidence)
            .Where(static item =>
                !string.IsNullOrWhiteSpace(item.Kind) &&
                !string.IsNullOrWhiteSpace(item.Id) &&
                !string.IsNullOrWhiteSpace(item.Label))
            .DistinctBy(static item => (item.Kind, item.Id))
            .ToArray();
        if (results.Count == 0)
        {
            error = "没有工具结果可供验证。";
            return false;
        }

        if (evidence.Count == 0)
        {
            error = "工具结果没有可解析的证据引用。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public bool TryVerifyAnswer(
        AnalysisAnswer answer,
        IReadOnlyList<AnalysisToolResult> results,
        out string error)
    {
        var insufficientData = results.Any(static result =>
            string.Equals(result.Outcome, AnalysisToolOutcomes.InsufficientData, StringComparison.Ordinal));
        if (insufficientData &&
            (answer.Findings.Count > 0 || answer.Charts.Count > 0 || answer.Investigation is not null))
        {
            error = "数据不足时不得生成确定性发现、图表或候选根因。";
            return false;
        }
        if (insufficientData && answer.Limitations.Count == 0)
        {
            error = "数据不足时回答必须明确列出限制条件。";
            return false;
        }

        var source = string.Join('\n', results.Select(result =>
            $"{result.Summary}\n{result.Data.GetRawText()}"));
        var answerText = string.Join('\n', new[] { answer.Summary }
            .Concat(answer.Findings)
            .Concat(answer.Limitations));
        var sourceNumbers = NumberGrounding.ExtractNormalized(source);
        if (!TryValidateCharts(answer.Charts, sourceNumbers, out error))
            return false;

        if (!NumberGrounding.IsGrounded(answerText, sourceNumbers, out var unsupportedRaw))
        {
            error = $"回答包含工具结果无法支持的数字: {unsupportedRaw}";
            return false;
        }

        if (answerText.Contains("导致", StringComparison.Ordinal) ||
            answerText.Contains("已证明因果", StringComparison.Ordinal) ||
            answerText.Contains("确定原因", StringComparison.Ordinal) ||
            answerText.Contains("confirmed root cause", StringComparison.OrdinalIgnoreCase) ||
            answerText.Contains("proven cause", StringComparison.OrdinalIgnoreCase) ||
            answerText.Contains("directly caused", StringComparison.OrdinalIgnoreCase) ||
            answerText.Contains("caused by", StringComparison.OrdinalIgnoreCase))
        {
            error = "回答把候选关联表述成了已验证因果关系。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateCharts(
        IReadOnlyList<ChartSpec> charts,
        IReadOnlySet<string> sourceNumbers,
        out string error)
    {
        if (charts.Count > MaxCharts)
        {
            error = $"回答中的图表数量不得超过 {MaxCharts}。";
            return false;
        }

        foreach (var chart in charts)
        {
            if (!AllowedChartTypes.Contains(chart.Type))
            {
                error = $"图表类型不在白名单中: {chart.Type}";
                return false;
            }
            if (string.IsNullOrWhiteSpace(chart.Title) || chart.Title.Length > 200)
            {
                error = "图表标题必须为 1 到 200 个字符。";
                return false;
            }
            if (chart.Labels.Count is 0 or > MaxLabels ||
                chart.Labels.Any(static label => string.IsNullOrWhiteSpace(label) || label.Length > 200))
            {
                error = $"图表必须包含 1 到 {MaxLabels} 个有效标签。";
                return false;
            }
            if (chart.Series.Count is 0 or > MaxSeries)
            {
                error = $"图表必须包含 1 到 {MaxSeries} 个数据系列。";
                return false;
            }
            if (chart.Series.Select(static series => series.Name)
                .Distinct(StringComparer.Ordinal).Count() != chart.Series.Count)
            {
                error = "图表数据系列名称不得重复。";
                return false;
            }

            foreach (var series in chart.Series)
            {
                if (string.IsNullOrWhiteSpace(series.Name) || series.Name.Length > 200)
                {
                    error = "图表数据系列名称必须为 1 到 200 个字符。";
                    return false;
                }
                if (series.Values.Count != chart.Labels.Count)
                {
                    error = $"图表数据系列 {series.Name} 的数据点数量必须与标签数量一致。";
                    return false;
                }
                if (series.Values.Any(static value => value.HasValue && !double.IsFinite(value.Value)))
                {
                    error = $"图表数据系列 {series.Name} 包含非有限数值。";
                    return false;
                }
                var unsupported = series.Values
                    .Where(static value => value.HasValue)
                    .Select(static value => value!.Value.ToString("R", CultureInfo.InvariantCulture))
                    .FirstOrDefault(value => !sourceNumbers.Contains(NumberGrounding.Normalize(value)));
                if (unsupported is not null)
                {
                    error = $"图表数据系列 {series.Name} 包含工具结果无法支持的数字: {unsupported}";
                    return false;
                }
            }
        }

        error = string.Empty;
        return true;
    }
}
