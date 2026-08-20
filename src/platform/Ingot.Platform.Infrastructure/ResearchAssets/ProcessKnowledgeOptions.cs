// 实现基础设施适配器 ProcessKnowledgeOptions，满足应用层端口而不改变领域契约。

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class ProcessKnowledgeOptions
{
    public string RootPath { get; init; } = "data/process-knowledge";
    public string? ArchiveRootPath { get; init; }
    public long MaxFileBytes { get; init; } = 50 * 1024 * 1024;
}
