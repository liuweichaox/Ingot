// 汇总当前用户可见站点的现场节点、生产事件和质量记录概况。
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.Agents;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Application.Events;
using Ingot.Platform.Application.Inspections;
using Ingot.Platform.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace Ingot.Platform.Infrastructure.AgentTools;

public sealed class SystemOverviewTool(
    EdgeRegistry edgeRegistry,
    SystemSiteCatalog siteCatalog,
    IChatEventReader events,
    IInspectionRecordStore inspections,
    IOptions<ChatOptions> options) : IAnalysisTool
{
    private readonly int _maxSites = Math.Clamp(options.Value.MaxSystemOverviewSites, 1, 1_000);
    private readonly int _heartbeatWindowSeconds = Math.Clamp(options.Value.SystemOverviewHeartbeatSeconds, 5, 3_600);

    public AnalysisToolDefinition Definition { get; } = new()
    {
        Name = "get_system_overview",
        Version = "1.0.0",
        EntryPoint = ProductEntryPoints.Chat,
        Purpose = RunPurposes.ReadOnlyAnalysis,
        Description = "按当前用户可见范围汇总站点、现场节点、生产事件和质量检测记录。只查询，不修改数据。",
        InputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                siteId = new { type = "string", minLength = 1, maxLength = 128 }
            },
            additionalProperties = false
        })
    };

    public async Task<AnalysisToolResult> ExecuteAsync(
        AnalysisToolCall call,
        AgentExecutionContext context,
        CancellationToken ct = default)
    {
        call.Arguments.TryGetValue("siteId", out var requestedSiteId);
        requestedSiteId = NullIfBlank(requestedSiteId);

        var registeredEdges = await LoadRegisteredEdgesAsync(
                context.AccessScope,
                requestedSiteId,
                ct)
            .ConfigureAwait(false);
        var allSiteIds = await ResolveSiteIdsAsync(
                context.AccessScope,
                requestedSiteId,
                siteCatalog,
                ct)
            .ConfigureAwait(false);
        var siteIds = allSiteIds.Take(_maxSites).ToArray();
        var now = DateTimeOffset.UtcNow;
        var summaries = await Task.WhenAll(siteIds.Select(siteId => BuildSiteOverviewAsync(
                siteId,
                context.UserId,
                registeredEdges,
                events,
                inspections,
                now,
                _heartbeatWindowSeconds,
                ct)))
            .ConfigureAwait(false);

        var limitations = new List<string>
        {
            $"现场节点在线按最近 {_heartbeatWindowSeconds} 秒内收到心跳且无已知错误判定。",
            "本工具仅返回站点级统计，不返回生产事件或质量检测记录明细。"
        };
        if (allSiteIds.Count > siteIds.Length)
            limitations.Add($"授权范围包含 {allSiteIds.Count} 个站点，本次按 {_maxSites} 个站点查询预算返回结果。请缩小范围后继续查看其余站点。");
        if (allSiteIds.Count == 0)
            limitations.Add(context.AccessScope.AllowAllSites
                ? "系统当前没有发现站点目录、采集节点或业务数据。"
                : "当前账号没有可访问的站点。");

        var scopeLabel = requestedSiteId is null ? "当前授权范围" : $"站点 {requestedSiteId}";
        var summary = allSiteIds.Count == 0
            ? $"{scopeLabel}没有可汇总的站点。"
            : $"{scopeLabel}共 {allSiteIds.Count} 个站点，已汇总 {summaries.Length} 个站点，登记现场节点 {summaries.Sum(static item => item.EdgeCount)} 个，" +
              $"生产事件 {summaries.Sum(static item => item.ProductionEventCount):N0} 条，" +
              $"质量检测记录 {summaries.Sum(static item => item.QualityRecordCount):N0} 条。";

        return new AnalysisToolResult
        {
            Tool = Definition.Name,
            Summary = summary,
            Data = JsonSerializer.SerializeToElement(new
            {
                scope = requestedSiteId is null ? "authorized-sites" : "site",
                siteId = requestedSiteId,
                authorizedSiteCount = allSiteIds.Count,
                siteCount = summaries.Length,
                truncated = allSiteIds.Count > siteIds.Length,
                sites = summaries
            }),
            RelatedRecords = BuildRelatedRecords(summaries),
            Limitations = limitations,
            // 系统概览的“0 条”本身就是可回答的事实，不应被解释成无法分析。
            Outcome = AnalysisToolOutcomes.Sufficient
        };
    }

    private static async Task<IReadOnlyList<string>> ResolveSiteIdsAsync(
        AgentAccessScope accessScope,
        string? requestedSiteId,
        SystemSiteCatalog siteCatalog,
        CancellationToken ct)
    {
        if (requestedSiteId is not null)
        {
            if (!accessScope.AllowAllSites)
                return [accessScope.EnsureAuthorizedSite(requestedSiteId)];

            return [requestedSiteId];
        }

        if (accessScope.AllowAllSites)
            return await siteCatalog.ListKnownSiteIdsAsync(ct).ConfigureAwait(false);

        return accessScope.SiteIds
            .Where(static siteId => !string.IsNullOrWhiteSpace(siteId) && siteId != "*")
            .Select(static siteId => siteId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task<IReadOnlyCollection<EdgeRegistry.EdgeState>> LoadRegisteredEdgesAsync(
        AgentAccessScope accessScope,
        string? requestedSiteId,
        CancellationToken ct)
    {
        if (accessScope.AllowAllSites)
            return await edgeRegistry.ListAsync(ct, requestedSiteId).ConfigureAwait(false);

        var siteIds = requestedSiteId is not null
            ? [accessScope.EnsureAuthorizedSite(requestedSiteId)]
            : accessScope.SiteIds
                .Where(static siteId => !string.IsNullOrWhiteSpace(siteId) && siteId != "*")
                .Select(static siteId => siteId.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var edges = new List<EdgeRegistry.EdgeState>();
        foreach (var siteId in siteIds)
        {
            edges.AddRange(await edgeRegistry.ListAsync(ct, siteId).ConfigureAwait(false));
        }
        return edges;
    }

    private static async Task<SiteOverview> BuildSiteOverviewAsync(
        string siteId,
        string userId,
        IReadOnlyCollection<EdgeRegistry.EdgeState> registeredEdges,
        IChatEventReader events,
        IInspectionRecordStore inspections,
        DateTimeOffset now,
        int heartbeatWindowSeconds,
        CancellationToken ct)
    {
        var eventStatsTask = events.GetScopeStatsAsync(
            userId,
            new PlatformEventQuery { SiteId = siteId },
            ct);
        var inspectionPageTask = inspections.QueryPageAsync(
            new InspectionRecordQuery { SiteId = siteId, Limit = 1 },
            ct);
        await Task.WhenAll(eventStatsTask, inspectionPageTask).ConfigureAwait(false);

        var siteEdges = registeredEdges
            .Where(edge => string.Equals(edge.SiteId, siteId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var statuses = siteEdges
            .Select(edge => GetEdgeStatus(edge, now, heartbeatWindowSeconds))
            .ToArray();

        return new SiteOverview
        {
            SiteId = siteId,
            EdgeCount = siteEdges.Length,
            OnlineEdgeCount = statuses.Count(static status => status == "online"),
            DegradedEdgeCount = statuses.Count(static status => status == "degraded"),
            OfflineEdgeCount = statuses.Count(static status => status == "offline"),
            UnknownEdgeCount = statuses.Count(static status => status == "unknown"),
            LatestEdgeSeenAt = siteEdges.Length == 0 ? null : siteEdges.Max(static edge => edge.LastSeen),
            ProductionEventCount = eventStatsTask.Result.Count,
            EarliestProductionEventAt = eventStatsTask.Result.EarliestOccurredAt,
            LatestProductionEventAt = eventStatsTask.Result.LatestOccurredAt,
            QualityRecordCount = inspectionPageTask.Result.Total
        };
    }

    private static string GetEdgeStatus(
        EdgeRegistry.EdgeState edge,
        DateTimeOffset now,
        int heartbeatWindowSeconds)
    {
        if (edge.LastSeen == default)
            return "unknown";
        if (!string.IsNullOrWhiteSpace(edge.LastError) ||
            edge.Acquisition?.State is "degraded" or "failed" ||
            edge.Delivery?.State is "degraded" or "failed")
            return "degraded";
        return now - edge.LastSeen <= TimeSpan.FromSeconds(heartbeatWindowSeconds)
            ? "online"
            : "offline";
    }

    private static IReadOnlyList<RelatedRecordRef> BuildRelatedRecords(
        IReadOnlyList<SiteOverview> summaries)
    {
        var related = new List<RelatedRecordRef>
        {
            new()
            {
                Kind = "system-overview",
                Id = "system-overview",
                Label = "系统概览统计",
                Url = "/chat"
            }
        };
        foreach (var site in summaries)
        {
            var encodedSiteId = Uri.EscapeDataString(site.SiteId);
            related.Add(new RelatedRecordRef
            {
                Kind = "edge-registry-scope",
                Id = $"{site.SiteId}:edges",
                Label = $"{site.SiteId} 现场节点状态",
                Url = $"/edges?siteId={encodedSiteId}"
            });
            related.Add(new RelatedRecordRef
            {
                Kind = "production-event-scope",
                Id = $"{site.SiteId}:production-events",
                Label = $"{site.SiteId} 生产事件统计",
                Url = $"/events?siteId={encodedSiteId}"
            });
            related.Add(new RelatedRecordRef
            {
                Kind = "inspection-record-scope",
                Id = $"{site.SiteId}:inspection-records",
                Label = $"{site.SiteId} 质量检测记录统计",
                Url = $"/inspections?siteId={encodedSiteId}"
            });
        }
        return related;
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record SiteOverview
    {
        public required string SiteId { get; init; }

        public int EdgeCount { get; init; }

        public int OnlineEdgeCount { get; init; }

        public int DegradedEdgeCount { get; init; }

        public int OfflineEdgeCount { get; init; }

        public int UnknownEdgeCount { get; init; }

        public DateTimeOffset? LatestEdgeSeenAt { get; init; }

        public long ProductionEventCount { get; init; }

        public DateTimeOffset? EarliestProductionEventAt { get; init; }

        public DateTimeOffset? LatestProductionEventAt { get; init; }

        public int QualityRecordCount { get; init; }
    }
}
