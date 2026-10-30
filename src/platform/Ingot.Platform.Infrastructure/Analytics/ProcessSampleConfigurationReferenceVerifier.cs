// Resolves only site-scoped metadata for configuration references reported by persisted sample frames.
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.Acquisition;
using Ingot.Platform.Application.Analytics;

namespace Ingot.Platform.Infrastructure.Analytics;

public sealed class ProcessSampleConfigurationReferenceVerifier(
    IIngestionTaskStore ingestionTasks,
    IEdgeSiteDirectory edgeSites) : IProcessSampleConfigurationReferenceVerifier
{
    public const int MaximumUniqueReferences = 40_000;

    public async Task<IReadOnlyList<ProcessSampleConfigurationEvidence>> VerifyAsync(
        string siteId,
        IReadOnlyList<ProcessSampleConfigurationEvidence> evidence,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siteId);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count == 0)
            return [];

        var authorizedEdgeIds = new HashSet<string>(
            await edgeSites.ListEdgeIdsAsync(siteId.Trim(), ct).ConfigureAwait(false),
            StringComparer.OrdinalIgnoreCase);
        var references = new Dictionary<ReferenceKey, IngestionTaskVersionReference>();
        var limitedReferences = new HashSet<ReferenceKey>();
        foreach (var item in evidence)
        {
            if (!CanVerify(item, authorizedEdgeIds, out var key, out var reference))
                continue;
            if (references.ContainsKey(key))
                continue;
            if (references.Count >= MaximumUniqueReferences)
            {
                limitedReferences.Add(key);
                continue;
            }
            references.Add(key, reference);
        }

        IReadOnlyList<IngestionTaskVersionMetadata> metadata = [];
        if (references.Count > 0)
        {
            metadata = await ingestionTasks.FindVersionMetadataAsync(
                references.Values.ToArray(), authorizedEdgeIds, ct).ConfigureAwait(false);
        }
        var metadataByReference = metadata
            .GroupBy(static item => new ReferenceKey(item.TaskId, item.Version))
            .ToDictionary(static group => group.Key, static group => group.First());

        return evidence.Select(item => VerifyItem(item, authorizedEdgeIds, references,
            limitedReferences, metadataByReference)).ToArray();
    }

    private static ProcessSampleConfigurationEvidence VerifyItem(
        ProcessSampleConfigurationEvidence item,
        IReadOnlySet<string> authorizedEdgeIds,
        IReadOnlyDictionary<ReferenceKey, IngestionTaskVersionReference> references,
        IReadOnlySet<ReferenceKey> limitedReferences,
        IReadOnlyDictionary<ReferenceKey, IngestionTaskVersionMetadata> metadataByReference)
    {
        if (!CanVerify(item, authorizedEdgeIds, out var key, out _))
            return Unverifiable(item);
        if (limitedReferences.Contains(key) || !references.ContainsKey(key))
            return Unverifiable(item);
        if (!metadataByReference.TryGetValue(key, out var metadata))
        {
            return item with
            {
                ReferenceVerificationStatus = ProcessSampleConfigurationReferenceStatuses.Unverifiable,
                ConfigurationStatus = null
            };
        }
        if (metadata.AuthorizedEdgeId is null)
        {
            return item with
            {
                ReferenceVerificationStatus = metadata.ExistsOutsideAuthorizedSite
                    ? ProcessSampleConfigurationReferenceStatuses.Unverifiable
                    : ProcessSampleConfigurationReferenceStatuses.VersionNotFound,
                ConfigurationStatus = null
            };
        }
        if (!authorizedEdgeIds.Contains(metadata.AuthorizedEdgeId))
            return Unverifiable(item);
        if (!string.Equals(metadata.AuthorizedEdgeId, item.SourceEdgeId, StringComparison.OrdinalIgnoreCase))
        {
            return item with
            {
                ReferenceVerificationStatus = ProcessSampleConfigurationReferenceStatuses.EdgeMismatch,
                ConfigurationStatus = null
            };
        }

        var configurationStatus = metadata.AuthorizedStatus?.Trim().ToLowerInvariant();
        return item with
        {
            ReferenceVerificationStatus = ProcessSampleConfigurationReferenceStatuses.Matched,
            ConfigurationStatus = ConfigurationStatuses.IsValid(configurationStatus)
                ? configurationStatus
                : null
        };
    }

    private static bool CanVerify(
        ProcessSampleConfigurationEvidence item,
        IReadOnlySet<string> authorizedEdgeIds,
        out ReferenceKey key,
        out IngestionTaskVersionReference reference)
    {
        key = default;
        reference = new IngestionTaskVersionReference(string.Empty, 0);
        if (item.SummarizedConfigurationGroupCount > 0 ||
            !string.Equals(item.AppliedConfigurationKind, "ingestion-task", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(item.AppliedConfigurationId) ||
            item.AppliedConfigurationId.Length > 128 ||
            item.AppliedConfigurationVersion is not > 0 ||
            string.IsNullOrWhiteSpace(item.SourceEdgeId) ||
            !authorizedEdgeIds.Contains(item.SourceEdgeId))
            return false;

        var taskId = item.AppliedConfigurationId.Trim().ToLowerInvariant();
        key = new ReferenceKey(taskId, item.AppliedConfigurationVersion.Value);
        reference = new IngestionTaskVersionReference(taskId, item.AppliedConfigurationVersion.Value);
        return true;
    }

    private static ProcessSampleConfigurationEvidence Unverifiable(
        ProcessSampleConfigurationEvidence item)
        => item with
        {
            ReferenceVerificationStatus = ProcessSampleConfigurationReferenceStatuses.Unverifiable,
            ConfigurationStatus = null
        };

    private readonly record struct ReferenceKey
    {
        public string TaskId { get; }

        public int Version { get; }

        public ReferenceKey(string taskId, int version)
        {
            TaskId = taskId.Trim().ToLowerInvariant();
            Version = version;
        }
    }
}
