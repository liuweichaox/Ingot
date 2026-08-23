using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Contracts.Agents;
using Microsoft.Extensions.Options;

namespace Ingot.Agent;

public sealed class DefaultPlanValidator(IOptions<ChatOptions> chatOptions) : IPlanValidator
{
    private static readonly Regex CanonicalInteger = new(
        @"^-?(?:0|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex CanonicalNumber = new(
        @"^-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly ChatOptions _chatOptions = chatOptions.Value;

    public bool TryValidate(
        string entryPoint,
        AnalysisPlan plan,
        IReadOnlyDictionary<string, IAnalysisTool> tools,
        out string error)
    {
        if (plan.ToolCalls.Count == 0)
        {
            error = "没有选择可用的数据查询。";
            return false;
        }

        if (!ProductEntryPoints.All.Contains(entryPoint))
        {
            error = "选择的功能入口无效。";
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

            if (!string.Equals(tool.Definition.EntryPoint, entryPoint, StringComparison.Ordinal) ||
                !string.Equals(tool.Definition.Purpose, RunPurposes.ForEntryPoint(entryPoint), StringComparison.Ordinal))
            {
                error = $"查询功能 {call.Tool} 不适用于当前功能入口。";
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

            if (!TryValidateValue(call.Tool, name, value, propertySchema, out error))
                return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateValue(
        string tool,
        string name,
        string? value,
        JsonElement schema,
        out string error)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String)
        {
            error = $"工具 {tool} 的参数 {name} 使用了无效 Schema。";
            return false;
        }

        return type.GetString() switch
        {
            "string" => TryValidateString(tool, name, value, schema, out error),
            "integer" => TryValidateInteger(tool, name, value, schema, out error),
            "number" => TryValidateNumber(tool, name, value, schema, out error),
            "boolean" => TryValidateBoolean(tool, name, value, out error),
            _ => FailUnsupportedSchema(tool, name, out error)
        };
    }

    private static bool FailUnsupportedSchema(string tool, string name, out string error)
    {
        error = $"工具 {tool} 的参数 {name} 使用了不支持的 Schema。";
        return false;
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

    private static bool TryValidateInteger(
        string tool,
        string name,
        string? value,
        JsonElement schema,
        out string error)
    {
        if (value is null ||
            !CanonicalInteger.IsMatch(value) ||
            !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            error = $"工具 {tool} 的参数 {name} 必须是整数。";
            return false;
        }

        return TryValidateNumericRange(tool, name, parsed, schema, out error);
    }

    private static bool TryValidateNumber(
        string tool,
        string name,
        string? value,
        JsonElement schema,
        out string error)
    {
        if (value is null ||
            !CanonicalNumber.IsMatch(value) ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed))
        {
            error = $"工具 {tool} 的参数 {name} 必须是有限数值。";
            return false;
        }

        return TryValidateNumericRange(tool, name, parsed, schema, out error);
    }

    private static bool TryValidateBoolean(
        string tool,
        string name,
        string? value,
        out string error)
    {
        if (value is not ("true" or "false"))
        {
            error = $"工具 {tool} 的参数 {name} 必须是 true 或 false。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateNumericRange(
        string tool,
        string name,
        double value,
        JsonElement schema,
        out string error)
    {
        if (!TryReadFiniteNumber(schema, "minimum", out var minimum) ||
            !TryReadFiniteNumber(schema, "maximum", out var maximum) ||
            minimum.HasValue && maximum.HasValue && minimum > maximum)
        {
            error = $"工具 {tool} 的参数 {name} 数值范围 Schema 无效。";
            return false;
        }
        if (minimum.HasValue && value < minimum.Value)
        {
            error = $"工具 {tool} 的参数 {name} 不得小于 {minimum.Value}.";
            return false;
        }
        if (maximum.HasValue && value > maximum.Value)
        {
            error = $"工具 {tool} 的参数 {name} 不得大于 {maximum.Value}.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadFiniteNumber(
        JsonElement schema,
        string property,
        out double? value)
    {
        value = null;
        if (!schema.TryGetProperty(property, out var element))
            return true;
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var parsed) ||
            !double.IsFinite(parsed))
            return false;
        value = parsed;
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

public sealed class DefaultAnalysisResultValidator : IAnalysisResultValidator
{
    private const int MaxToolDataBytes = 32 * 1024;
    private const int MaxCharts = 8;
    private const int MaxLabels = 500;
    private const int MaxSeries = 16;
    private const int MaxProposals = 8;
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
        out IReadOnlyList<RelatedRecordRef> relatedRecords,
        out string error)
    {
        foreach (var result in results)
        {
            var dataBytes = Encoding.UTF8.GetByteCount(result.Data.GetRawText());
            if (dataBytes > MaxToolDataBytes)
            {
                relatedRecords = [];
                error = $"工具 {result.Tool} 的 Data 超过 {MaxToolDataBytes} 字节上限。";
                return false;
            }

            if (result.Details.Any(static detail =>
                    string.IsNullOrWhiteSpace(detail.Kind) ||
                    string.IsNullOrWhiteSpace(detail.Label) ||
                    string.IsNullOrWhiteSpace(detail.Url)))
            {
                relatedRecords = [];
                error = $"工具 {result.Tool} 包含无效 明细结果 引用。";
                return false;
            }
        }

        relatedRecords = results.SelectMany(static result => result.RelatedRecords)
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

        if (relatedRecords.Count == 0)
        {
            error = "分析结果无法关联到原始生产记录。";
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
            (answer.Findings.Count > 0 || answer.Charts.Count > 0 || answer.Proposals.Count > 0 ||
                answer.CombinedAnalysis is not null))
        {
            error = "数据不足时不能给出明确原因或分析图表。";
            return false;
        }
        if (insufficientData && answer.Limitations.Count == 0)
        {
            error = "数据不足时必须直接说明缺少什么。";
            return false;
        }

        if (!TryValidateClaims(answer, results, out error))
            return false;

        if (!TryValidateProposals(answer.Proposals, results, out error))
            return false;

        var source = string.Join('\n', results.Select(result =>
            $"{result.Summary}\n{result.Data.GetRawText()}"));
        var answerText = string.Join('\n', new[] { answer.Summary }
            .Concat(answer.Findings.Select(static finding => finding.Statement))
            .Concat(answer.Limitations)
            .Concat(answer.Proposals.SelectMany(static proposal =>
                new[] { proposal.Title, proposal.Rationale }.Concat(proposal.DraftFields.Values))));
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
            error = "回答把参数相关性说成了已经确认的原因。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateClaims(
        AnalysisAnswer answer,
        IReadOnlyList<AnalysisToolResult> results,
        out string error)
    {
        if (!AnalysisClaimStrengths.All.Contains(answer.SummaryStrength))
        {
            error = "回答摘要必须声明有效的结论强度。";
            return false;
        }
        if (string.Equals(answer.SummaryStrength, AnalysisClaimStrengths.Causal, StringComparison.Ordinal))
        {
            error = "只读分析不能把摘要声明为因果结论。";
            return false;
        }

        var availableEvidence = results.SelectMany(static result => result.RelatedRecords)
            .Select(static reference => (reference.Kind, reference.Id))
            .ToHashSet();
        foreach (var finding in answer.Findings)
        {
            if (string.IsNullOrWhiteSpace(finding.Statement) || finding.Statement.Length > 4000 ||
                !AnalysisClaimStrengths.All.Contains(finding.Strength))
            {
                error = "分析发现必须包含有效陈述和结论强度。";
                return false;
            }
            if (string.Equals(finding.Strength, AnalysisClaimStrengths.Causal, StringComparison.Ordinal))
            {
                error = "只读分析不能声明因果结论；请降级为关联或待验证假设。";
                return false;
            }
            if (finding.EvidenceReferences.Count == 0 || finding.EvidenceReferences.Any(reference =>
                    !availableEvidence.Contains((reference.Kind, reference.Id))))
            {
                error = "每条分析发现都必须引用本次只读工具返回的正式记录。";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateProposals(
        IReadOnlyList<AgentProposalEnvelope> proposals,
        IReadOnlyList<AnalysisToolResult> results,
        out string error)
    {
        if (proposals.Count > MaxProposals)
        {
            error = $"回答中的提议数量不得超过 {MaxProposals}。";
            return false;
        }
        var availableEvidence = results.SelectMany(static result => result.RelatedRecords)
            .Select(static reference => (reference.Kind, reference.Id))
            .ToHashSet();
        foreach (var proposal in proposals)
        {
            if (!AgentProposalKinds.All.Contains(proposal.Kind) ||
                string.IsNullOrWhiteSpace(proposal.Title) || proposal.Title.Length > 200 ||
                string.IsNullOrWhiteSpace(proposal.Rationale) || proposal.Rationale.Length > 2000)
            {
                error = "Agent 提议的类型、标题或依据无效。";
                return false;
            }
            if (!string.Equals(proposal.Persistence, "preview-only", StringComparison.Ordinal) ||
                !proposal.RequiresHumanConfirmation)
            {
                error = "Agent 提议只能作为等待人工确认的预览，不能直接持久化。";
                return false;
            }
            if (proposal.EvidenceReferences.Count == 0 || proposal.EvidenceReferences.Any(reference =>
                    !availableEvidence.Contains((reference.Kind, reference.Id))))
            {
                error = "Agent 提议必须引用本次只读工具返回的正式记录。";
                return false;
            }
            if (proposal.DraftFields.Count > 32 || proposal.DraftFields.Any(static pair =>
                    string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Length > 64 || pair.Value.Length > 4000))
            {
                error = "Agent 提议草稿字段无效或超过边界。";
                return false;
            }
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
