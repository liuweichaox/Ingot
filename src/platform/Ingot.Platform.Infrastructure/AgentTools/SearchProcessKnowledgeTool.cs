
using System.Text.Json;
using System.Text.RegularExpressions;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.ResearchAssets;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed partial class SearchProcessKnowledgeTool(
    IResearchAssetStore store,
    IResearchProjectContextReader projects,
    IProcessKnowledgeSearch? search = null) : IAnalysisTool
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
        var projectId = context.Request.PageContext is { Kind: "research-project" } pageContext &&
                        Guid.TryParse(pageContext.Id, out var parsedProjectId)
            ? (Guid?)parsedProjectId
            : null;
        if (projectId is null)
            return Insufficient(query, "知识检索必须在工艺研发项目上下文中执行。", productFamilyCode, equipmentId);
        var project = await projects.GetProjectAsync(projectId.Value, ct).ConfigureAwait(false);
        var normalizedUserId = context.UserId.Trim().ToLowerInvariant();
        if (project is null ||
            !(string.Equals(project.OwnerUserId, normalizedUserId, StringComparison.Ordinal) ||
              project.MemberUserIds.Contains(normalizedUserId, StringComparer.Ordinal)))
            return Insufficient(query, "研发项目不存在或当前用户无权访问。", productFamilyCode, equipmentId);
        try
        {
            context.AccessScope.EnsureAuthorizedSite(project.SiteCode);
        }
        catch (UnauthorizedAccessException)
        {
            return Insufficient(query, "当前用户无权访问该研发项目所在站点。", productFamilyCode, equipmentId);
        }

        var result = search is null
            ? await SearchFallbackAsync(projectId.Value, query, productFamilyCode, equipmentId, limit, ct).ConfigureAwait(false)
            : await search.SearchAsync(new ProcessKnowledgeSearchRequest
            {
                ResearchProjectId = projectId.Value,
                Query = query.Trim(),
                AllowAllSites = context.AccessScope.AllowAllSites,
                SiteIds = context.AccessScope.SiteIds,
                ProductFamilyCode = NullIfBlank(productFamilyCode),
                EquipmentId = NullIfBlank(equipmentId),
                Limit = limit
            }, ct).ConfigureAwait(false);
        var selected = result.Hits;
        var limitations = result.Limitations.ToList();
        if (selected.Count == 0)
            limitations.Add("已复核知识中没有与当前问题直接匹配的记录，不能据此形成现场结论。");
        var related = selected
            .Select(match => new RelatedRecordRef
            {
                Kind = "process-knowledge-record",
                Id = match.Record.RecordId.ToString(),
                Label = match.Record.PageOrSheet is null
                    ? match.Source.Title
                    : $"{match.Source.Title} · {match.Record.PageOrSheet}"
            })
            .ToArray();
        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = selected.Count == 0
                ? "没有找到可用于当前问题的已复核工艺知识。"
                : $"找到 {selected.Count} 条已复核工艺知识记录，来自 {selected.Select(static hit => hit.Source.SourceId).Distinct().Count()} 个来源。",
            Data = JsonSerializer.SerializeToElement(new
            {
                query = query.Trim(),
                retrievalMode = result.RetrievalMode,
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
                    sourceSha256 = match.Source.Sha256,
                    recordId = match.Record.RecordId,
                    category = match.Record.Category,
                    pageOrSheet = match.Record.PageOrSheet,
                    region = match.Record.Region,
                    content = match.Record.Content,
                    structuredValues = match.Record.StructuredValues,
                    reviewedBy = match.Record.ReviewedBy,
                    reviewedAt = match.Record.ReviewedAt,
                    retrievalMethod = match.RetrievalMethod,
                    score = match.Score,
                    contentHash = match.Record.Citation?.ContentHash,
                    citation = match.Record.Citation
                })
            }),
            RelatedRecords = related,
            Limitations = limitations,
            Outcome = selected.Count > 0
                ? AnalysisToolOutcomes.Sufficient
                : AnalysisToolOutcomes.InsufficientData
        };
    }

    private async Task<ProcessKnowledgeSearchResult> SearchFallbackAsync(
        Guid projectId,
        string query,
        string? productFamilyCode,
        string? equipmentId,
        int limit,
        CancellationToken ct)
    {
        var terms = BuildTerms(query);
        var sources = (await store.ListKnowledgeSourcesAsync(projectId, ct).ConfigureAwait(false))
            .Where(static source => source.Status == KnowledgeSourceStatuses.Reviewed)
            .Where(source => MatchesContext(source.ContextSelector, "product_family_code", productFamilyCode))
            .Where(source => MatchesContext(source.ContextSelector, "equipment_id", equipmentId))
            .ToArray();
        var matches = new List<ProcessKnowledgeSearchHit>();
        foreach (var source in sources)
        {
            var records = await store.ListKnowledgeRecordsAsync(source.SourceId, ct).ConfigureAwait(false);
            matches.AddRange(records
                .Where(static record => record.HumanReviewed)
                .Select(record => new ProcessKnowledgeSearchHit
                {
                    Source = source,
                    Record = record,
                    Score = Score(record, source, terms),
                    RetrievalMethod = "keyword-fallback"
                })
                .Where(static match => match.Score > 0));
        }
        return new ProcessKnowledgeSearchResult
        {
            Hits = matches.OrderByDescending(static match => match.Score)
                .ThenByDescending(static match => match.Record.ReviewedAt ?? match.Record.CreatedAt)
                .Take(limit)
                .ToArray(),
            RetrievalMode = "keyword-fallback",
            Limitations = ["数据库混合检索未启用，已使用兼容关键词检索。"]
        };
    }

    private AnalysisToolResult Insufficient(
        string query,
        string limitation,
        string? productFamilyCode,
        string? equipmentId)
        => new()
        {
            Tool = Definition.Name,
            Summary = "没有找到可用于当前问题的已复核工艺知识。",
            Data = JsonSerializer.SerializeToElement(new
            {
                query = query.Trim(),
                appliedContext = new
                {
                    productFamilyCode = NullIfBlank(productFamilyCode),
                    equipmentId = NullIfBlank(equipmentId)
                },
                records = Array.Empty<object>()
            }),
            Limitations = [limitation],
            Outcome = AnalysisToolOutcomes.InsufficientData
        };

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

    [GeneratedRegex(@"[\s,，。！？；;:：/\\|()\[\]{}]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"\p{IsCJKUnifiedIdeographs}", RegexOptions.CultureInvariant)]
    private static partial Regex HasCjkPattern();
}
