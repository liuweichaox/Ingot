// Defines ingestion-task persistence operations and the bounded metadata lookup used for reference verification.
using Ingot.Contracts.Acquisition;

namespace Ingot.Platform.Application.Acquisition;

/// <summary>Provides task persistence and redacted version metadata for authorized reference checks.</summary>
public interface IIngestionTaskStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default);
    Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default);
    Task<IReadOnlyList<IngestionTaskVersionMetadata>> FindVersionMetadataAsync(
        IReadOnlyCollection<IngestionTaskVersionReference> references,
        IReadOnlyCollection<string> authorizedEdgeIds,
        CancellationToken ct = default);
    Task<IReadOnlyList<IngestionTaskCoverageMetadata>> FindCoverageMetadataAsync(
        IReadOnlyCollection<IngestionTaskVersionReference> references,
        IReadOnlyCollection<string> authorizedEdgeIds,
        CancellationToken ct = default);
    Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default);

    Task<IngestionTask> PublishExclusiveAsync(IngestionTask published, CancellationToken ct = default);

    Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default);
}

public sealed record IngestionTaskVersionReference(string TaskId, int Version);

public sealed record IngestionTaskVersionMetadata(
    string TaskId,
    int Version,
    string? AuthorizedEdgeId,
    string? AuthorizedStatus,
    bool ExistsOutsideAuthorizedSite);

/// <summary>Authorized task-version fields needed to compare expected mappings with observed sample frames.</summary>
public sealed record IngestionTaskCoverageMetadata(
    string TaskId,
    int Version,
    string? AuthorizedEdgeId,
    string? AuthorizedStatus,
    string? SubjectType,
    string? SubjectId,
    IReadOnlyList<string> RequiredDataItemCodes,
    bool ExistsOutsideAuthorizedSite,
    bool RequiredDataItemCodesTruncated);
