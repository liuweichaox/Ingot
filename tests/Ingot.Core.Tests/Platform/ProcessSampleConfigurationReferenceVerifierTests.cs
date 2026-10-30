// 验证采样帧任务版本引用的站点范围、Edge 归属和脱敏结果。

using System.Text.Json;
using Ingot.Contracts.Acquisition;
using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.Analytics;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessSampleConfigurationReferenceVerifierTests
{
    [Fact]
    public async Task VerifyAsync_ReportsOnlySiteScopedReferenceAndEdgeEvidence()
    {
        var taskStore = new FakeTaskStore(
        [
            new("task-published", 1, "EDGE-A", "published", false),
            new("task-retired", 2, "EDGE-A", "retired", false),
            new("task-draft", 1, "EDGE-A", "draft", false),
            new("task-edge-mismatch", 1, "EDGE-B", "published", false),
            new("task-other-site", 4, "EDGE-OTHER-SITE", "published", false)
        ]);
        var edgeDirectory = new FakeEdgeSiteDirectory("SITE-A", ["EDGE-A", "EDGE-B"]);
        var verifier = new ProcessSampleConfigurationReferenceVerifier(taskStore, edgeDirectory);
        var evidence = new[]
        {
            Evidence("task-published", 1, "EDGE-A"),
            Evidence("task-published", 1, "EDGE-A"),
            Evidence("task-retired", 2, "EDGE-A"),
            Evidence("task-draft", 1, "EDGE-A"),
            Evidence("task-edge-mismatch", 1, "EDGE-A"),
            Evidence("task-missing", 9, "EDGE-A"),
            Evidence("task-other-site", 4, "EDGE-A"),
            Evidence("task-published", 1, "EDGE-A", kind: "process-specification"),
            Evidence(null, null, "EDGE-A"),
            Evidence("task-published", 1, "EDGE-A", summarizedGroups: 3),
            Evidence("task-published", 1, "EDGE-OUTSIDE")
        };

        var verified = await verifier.VerifyAsync("SITE-A", evidence);

        Assert.Equal(evidence.Length, verified.Count);
        Assert.All(verified.Take(2), item =>
        {
            Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Matched, item.ReferenceVerificationStatus);
            Assert.Equal("published", item.ConfigurationStatus);
        });
        Assert.Equal("retired", verified[2].ConfigurationStatus);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Matched, verified[2].ReferenceVerificationStatus);
        Assert.Equal("draft", verified[3].ConfigurationStatus);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.EdgeMismatch, verified[4].ReferenceVerificationStatus);
        Assert.Null(verified[4].ConfigurationStatus);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.VersionNotFound, verified[5].ReferenceVerificationStatus);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Unverifiable, verified[6].ReferenceVerificationStatus);
        Assert.Null(verified[6].ConfigurationStatus);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Unverifiable, verified[7].ReferenceVerificationStatus);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Unverifiable, verified[8].ReferenceVerificationStatus);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Unverifiable, verified[9].ReferenceVerificationStatus);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Unverifiable, verified[10].ReferenceVerificationStatus);
        Assert.Equal("SITE-A", edgeDirectory.RequestedSiteId);
        Assert.Equal(1, taskStore.LookupCount);
        Assert.Equal(6, taskStore.LastReferences.Count);
        Assert.Contains(taskStore.LastAuthorizedEdgeIds, edgeId => edgeId == "EDGE-A");
        var externalMetadata = Assert.Single(taskStore.LastMetadata, item => item.TaskId == "task-other-site");
        Assert.Null(externalMetadata.AuthorizedEdgeId);
        Assert.Null(externalMetadata.AuthorizedStatus);
        Assert.True(externalMetadata.ExistsOutsideAuthorizedSite);

        var serialized = JsonSerializer.Serialize(verified);
        Assert.DoesNotContain("EDGE-OTHER-SITE", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("baseUrl", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("headersText", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secretRef", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_BatchesUniqueReferencesAndMarksOverflowUnverifiable()
    {
        var evidence = Enumerable.Range(1, ProcessSampleConfigurationReferenceVerifier.MaximumUniqueReferences + 1)
            .Select(index => Evidence($"task-{index:D5}", 1, "EDGE-A"))
            .ToArray();
        var taskStore = new FakeTaskStore([]);
        var verifier = new ProcessSampleConfigurationReferenceVerifier(
            taskStore, new FakeEdgeSiteDirectory("SITE-A", ["EDGE-A"]));

        var verified = await verifier.VerifyAsync("SITE-A", evidence);

        Assert.Equal(1, taskStore.LookupCount);
        Assert.Equal(ProcessSampleConfigurationReferenceVerifier.MaximumUniqueReferences, taskStore.LastReferences.Count);
        Assert.Equal(ProcessSampleConfigurationReferenceStatuses.Unverifiable,
            verified[^1].ReferenceVerificationStatus);
    }

    private static ProcessSampleConfigurationEvidence Evidence(
        string? taskId,
        int? version,
        string? edgeId,
        string kind = "ingestion-task",
        int summarizedGroups = 0)
        => new()
        {
            AppliedConfigurationKind = taskId is null ? null : kind,
            AppliedConfigurationId = taskId,
            AppliedConfigurationVersion = version,
            SourceEdgeId = edgeId,
            SummarizedConfigurationGroupCount = summarizedGroups,
            SampleFrameCount = 1
        };

    private sealed class FakeEdgeSiteDirectory(string siteId, IReadOnlyCollection<string> edgeIds)
        : IEdgeSiteDirectory
    {
        public string? RequestedSiteId { get; private set; }

        public Task<IReadOnlySet<string>> ListEdgeIdsAsync(string requestedSiteId, CancellationToken ct = default)
        {
            RequestedSiteId = requestedSiteId;
            return Task.FromResult<IReadOnlySet<string>>(
                string.Equals(siteId, requestedSiteId, StringComparison.OrdinalIgnoreCase)
                    ? edgeIds.ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed class FakeTaskStore(IReadOnlyList<IngestionTaskVersionMetadata> metadata)
        : IIngestionTaskStore
    {
        public int LookupCount { get; private set; }
        public IReadOnlyList<IngestionTaskVersionReference> LastReferences { get; private set; } = [];
        public IReadOnlyCollection<string> LastAuthorizedEdgeIds { get; private set; } = [];
        public IReadOnlyList<IngestionTaskVersionMetadata> LastMetadata { get; private set; } = [];

        public Task InitializeAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTask>> ListAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IngestionTask>> ListPublishedForEdgeAsync(string edgeId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTask?> GetAsync(string taskId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTask> UpsertAsync(IngestionTask value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IngestionTask> PublishExclusiveAsync(IngestionTask published, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string taskId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<IngestionTaskVersionMetadata>> FindVersionMetadataAsync(
            IReadOnlyCollection<IngestionTaskVersionReference> references,
            IReadOnlyCollection<string> authorizedEdgeIds,
            CancellationToken ct = default)
        {
            LookupCount++;
            LastReferences = references.ToArray();
            LastAuthorizedEdgeIds = authorizedEdgeIds.ToArray();
            var allowedEdges = authorizedEdgeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var result = references.Select(reference =>
            {
                var stored = metadata.FirstOrDefault(item =>
                    string.Equals(item.TaskId, reference.TaskId, StringComparison.OrdinalIgnoreCase) &&
                    item.Version == reference.Version);
                if (stored is null)
                    return new IngestionTaskVersionMetadata(reference.TaskId, reference.Version, null, null, false);
                if (stored.AuthorizedEdgeId is not null && allowedEdges.Contains(stored.AuthorizedEdgeId))
                    return stored;
                return new IngestionTaskVersionMetadata(reference.TaskId, reference.Version, null, null, true);
            }).ToArray();
            LastMetadata = result;
            return Task.FromResult<IReadOnlyList<IngestionTaskVersionMetadata>>(result);
        }
    }
}
