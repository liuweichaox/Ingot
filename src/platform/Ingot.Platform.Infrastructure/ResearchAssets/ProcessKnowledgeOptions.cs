
namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class ProcessKnowledgeOptions
{
    public string RootPath { get; init; } = "data/process-knowledge";
    public string? ArchiveRootPath { get; init; }
    public long MaxFileBytes { get; init; } = 50 * 1024 * 1024;
}
