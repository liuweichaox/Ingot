using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>
/// 受项目与站点范围约束的工艺知识检索端口。实现必须先应用范围过滤，再计算检索排序。
/// </summary>
public interface IProcessKnowledgeSearch
{
    Task<ProcessKnowledgeSearchResult> SearchAsync(
        ProcessKnowledgeSearchRequest request,
        CancellationToken ct = default);
}

public sealed record ProcessKnowledgeSearchRequest
{
    public required Guid ResearchProjectId { get; init; }
    public required string Query { get; init; }
    public bool AllowAllSites { get; init; }
    public IReadOnlyCollection<string> SiteIds { get; init; } = [];
    public string? ProductFamilyCode { get; init; }
    public string? EquipmentId { get; init; }
    public int Limit { get; init; } = 8;
}

public sealed record ProcessKnowledgeSearchResult
{
    public IReadOnlyList<ProcessKnowledgeSearchHit> Hits { get; init; } = [];
    public string RetrievalMode { get; init; } = "keyword";
    public IReadOnlyList<string> Limitations { get; init; } = [];
}

public sealed record ProcessKnowledgeSearchHit
{
    public required KnowledgeSource Source { get; init; }
    public required KnowledgeRecord Record { get; init; }
    public double Score { get; init; }
    public string RetrievalMethod { get; init; } = "keyword";
}
