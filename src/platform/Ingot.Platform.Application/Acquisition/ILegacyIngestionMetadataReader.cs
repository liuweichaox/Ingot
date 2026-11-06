// Exposes read-only historical acquisition metadata used to verify persisted sample evidence.
namespace Ingot.Platform.Application.Acquisition;

/// <summary>Reads archived task metadata without exposing retired configuration writes.</summary>
public interface ILegacyIngestionMetadataReader
{
    Task<IReadOnlyList<IngestionTaskVersionMetadata>> FindVersionMetadataAsync(
        IReadOnlyCollection<IngestionTaskVersionReference> references,
        IReadOnlyCollection<string> authorizedEdgeIds,
        CancellationToken ct = default);

    Task<IReadOnlyList<IngestionTaskCoverageMetadata>> FindCoverageMetadataAsync(
        IReadOnlyCollection<IngestionTaskVersionReference> references,
        IReadOnlyCollection<string> authorizedEdgeIds,
        CancellationToken ct = default);
}

public sealed record IngestionTaskVersionReference(string TaskId, int Version);

public sealed record IngestionTaskVersionMetadata(
    string TaskId,
    int Version,
    string? AuthorizedEdgeId,
    string? AuthorizedStatus,
    bool ExistsOutsideAuthorizedSite);

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
