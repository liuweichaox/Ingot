// 实现只读 Agent 工具 SearchProcessKnowledgeTool，仅暴露授权范围内的确定性证据。

using Ingot.Platform.Application.ResearchAssets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Infrastructure.ResearchAssets;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed partial class SearchProcessKnowledgeTool(
    IResearchAssetStore store) : IAnalysisTool
{
    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "search_process_knowledge",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "检索已经过现场人员复核的工艺文档、表格、图片说明和现场记录。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            required = new[] { "query" },
            properties = new
            {
                query = new { type = "string", minLength = 1, maxLength = 500 },
                productFamilyCode = new { type = "string", maxLength = 120 },
                equipmentId = new { type = "string", maxLength = 120 },
                limit = new { type = "integer", minimum = 1, maximum = 20 }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        if (!call.Arguments.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("请提供要检索的工艺问题。", nameof(call));
        call.Arguments.TryGetValue("productFamilyCode", out var productFamilyCode);
        call.Arguments.TryGetValue("equipmentId", out var equipmentId);
        var limit = call.Arguments.TryGetValue("limit", out var limitText) &&
                    int.TryParse(limitText, out var parsedLimit)
            ? Math.Clamp(parsedLimit, 1, 20)
            : 8;
        var terms = BuildTerms(query);
        var projectId = context.Request.PageContext is { Kind: "research-project" } pageContext &&
                        Guid.TryParse(pageContext.Id, out var parsedProjectId)
            ? parsedProjectId.ToString()
            : null;
        var sources = (await store.ListKnowledgeSourcesAsync(ct).ConfigureAwait(false))
            .Where(source =>
                projectId is not null &&
                source.ContextSelector.TryGetValue("research-project-id", out var value) &&
                string.Equals(value, projectId, StringComparison.OrdinalIgnoreCase))
            .Where(static source => source.Status == KnowledgeSourceStatuses.Reviewed)
            .Where(source => MatchesContext(source.ContextSelector, "product_family_code", productFamilyCode))
            .Where(source => MatchesContext(source.ContextSelector, "equipment_id", equipmentId))
            .ToArray();
        var matches = new List<KnowledgeMatch>();
        foreach (var source in sources)
        {
            var records = await store.ListKnowledgeRecordsAsync(source.SourceId, ct).ConfigureAwait(false);
            matches.AddRange(records
                .Where(static record => record.HumanReviewed)
                .Select(record => new KnowledgeMatch(
                    source,
                    record,
                    Score(record, source, terms)))
                .Where(static match => match.Score > 0));
        }
        var selected = matches
            .OrderByDescending(static match => match.Score)
            .ThenByDescending(static match => match.Record.ReviewedAt ?? match.Record.CreatedAt)
            .Take(limit)
            .ToArray();
        var limitations = new List<string>();
        if (sources.Length == 0)
            limitations.Add("当前适用范围内没有已复核的工艺知识来源。");
        else if (selected.Length == 0)
            limitations.Add("已复核知识中没有与当前问题直接匹配的记录，不能据此形成现场结论。");
        var related = selected
            .Select(static match => match.Source)
            .DistinctBy(static source => source.SourceId)
            .Select(static source => new RelatedRecordRef
            {
                Kind = "process-knowledge",
                Id = source.SourceId.ToString(),
                Label = source.Title,
                Url = source.ContextSelector.TryGetValue("research-project-id", out var sourceProjectId)
                    ? $"/research-projects?projectId={sourceProjectId}&sourceId={source.SourceId}"
                    : "/research-projects"
            })
            .ToArray();
        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = selected.Length == 0
                ? "没有找到可用于当前问题的已复核工艺知识。"
                : $"找到 {selected.Length} 条已复核工艺知识记录，来自 {related.Length} 个来源。",
            Data = JsonSerializer.SerializeToElement(new
            {
                query = query.Trim(),
                appliedContext = new
                {
                    researchProjectId = projectId,
                    productFamilyCode = NullIfBlank(productFamilyCode),
                    equipmentId = NullIfBlank(equipmentId)
                },
                records = selected.Select(static match => new
                {
                    sourceId = match.Source.SourceId,
                    sourceTitle = match.Source.Title,
                    sourceKind = match.Source.SourceKind,
                    fileName = match.Source.FileName,
                    recordId = match.Record.RecordId,
                    category = match.Record.Category,
                    pageOrSheet = match.Record.PageOrSheet,
                    region = match.Record.Region,
                    content = match.Record.Content,
                    structuredValues = match.Record.StructuredValues,
                    reviewedBy = match.Record.ReviewedBy,
                    reviewedAt = match.Record.ReviewedAt
                })
            }),
            RelatedRecords = related,
            Limitations = limitations,
            Outcome = selected.Length > 0
                ? AnalysisToolOutcomes.Sufficient
                : AnalysisToolOutcomes.InsufficientData
        };
    }

    private static bool MatchesContext(
        IReadOnlyDictionary<string, string> context,
        string key,
        string? requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return true;
        return !context.TryGetValue(key, out var value) ||
               string.Equals(value, requested.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static double Score(
        KnowledgeRecord record,
        KnowledgeSource source,
        IReadOnlyList<string> terms)
    {
        var content = $"{source.Title} {record.Category} {record.Content} " +
                      string.Join(' ', record.StructuredValues.Select(static pair => $"{pair.Key} {pair.Value}"));
        var score = 0d;
        foreach (var term in terms)
        {
            if (source.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += 3;
            if (record.Content.Contains(term, StringComparison.OrdinalIgnoreCase))
                score += term.Length > 2 ? 2 : 0.5;
            if (record.StructuredValues.Any(pair =>
                    pair.Key.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    pair.Value.Contains(term, StringComparison.OrdinalIgnoreCase)))
                score += 1.5;
        }
        return score + (content.Contains(string.Join(' ', terms), StringComparison.OrdinalIgnoreCase) ? 2 : 0);
    }

    private static IReadOnlyList<string> BuildTerms(string query)
    {
        var normalized = SeparatorPattern().Replace(query.Trim().ToLowerInvariant(), " ");
        var values = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => value.Length > 1)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var token in values.Where(static value => HasCjkPattern().IsMatch(value) && value.Length > 3).ToArray())
        {
            for (var index = 0; index < token.Length - 1; index++)
                values.Add(token.Substring(index, 2));
        }
        return values.ToArray();
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record KnowledgeMatch(
        KnowledgeSource Source,
        KnowledgeRecord Record,
        double Score);

    [GeneratedRegex(@"[\s,，。！？；;:：/\\|()\[\]{}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"\p{IsCJKUnifiedIdeographs}", RegexOptions.CultureInvariant)]
    private static partial Regex HasCjkPattern();
}
