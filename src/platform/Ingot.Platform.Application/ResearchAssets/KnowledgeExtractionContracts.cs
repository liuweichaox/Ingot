namespace Ingot.Platform.Application.ResearchAssets;

public sealed record KnowledgeExtractionJob(
    Guid SourceId,
    string UserId,
    Guid LeaseId,
    int AttemptCount);

public enum KnowledgeExtractionFailureDisposition
{
    RetryScheduled,
    DeadLettered
}
