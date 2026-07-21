using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Infrastructure.Inspections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class InspectionAttachmentsControllerTests
{
    [Fact]
    public async Task OpenContent_ReturnsOriginalFileForReview()
    {
        var attachmentId = Guid.CreateVersion7();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var reviews = new StubReviewStore();
        var controller = new InspectionAttachmentsController(
            new StubAttachmentStore(attachmentId, bytes),
            reviews,
            new PlatformUserResolver(new TestHostEnvironment()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await controller.OpenContent(attachmentId, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/tiff", file.ContentType);
        Assert.True(file.EnableRangeProcessing);
        await using var content = file.FileStream;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal(bytes, copy.ToArray());
        Assert.Contains("inline", controller.Response.Headers.ContentDisposition.ToString());
        Assert.Equal(attachmentId, reviews.OpenedAttachmentId);
    }

    private sealed class StubAttachmentStore(Guid attachmentId, byte[] bytes) : IInspectionAttachmentStore
    {
        private readonly InspectionAttachment _attachment = new()
        {
            AttachmentId = attachmentId,
            StorageRef = "attachment://sha256/test/original.tiff",
            Sha256 = new string('a', 64),
            MediaType = "image/tiff",
            FileName = "original.tiff",
            SizeBytes = bytes.Length
        };

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<AttachmentUploadResponse> SaveAsync(
            Stream content,
            string fileName,
            string mediaType,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<InspectionAttachment?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<InspectionAttachment?>(id == attachmentId ? _attachment : null);

        public Task<Stream?> OpenReadAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<Stream?>(id == attachmentId ? new MemoryStream(bytes, writable: false) : null);

        public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(id == attachmentId);
    }

    private sealed class StubReviewStore : IInspectionReviewStore
    {
        public Guid? OpenedAttachmentId { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<StoreInspectionReviewResult> CreateAsync(CreateInspectionReviewRequest request, string operationRunId, string reviewedBy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default) => Task.FromResult<InspectionReview?>(null);
        public Task<IReadOnlyList<InspectionReview>> QueryAsync(Guid? inspectionRecordId, string? operationRunId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionReview>>([]);
        public Task<IReadOnlyDictionary<Guid, InspectionReview>> GetLatestByInspectionRecordIdsAsync(IReadOnlyCollection<Guid> inspectionRecordIds, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, InspectionReview>>(new Dictionary<Guid, InspectionReview>());
        public Task LogAccessAsync(Guid? inspectionRecordId, Guid? attachmentId, string action, string actor, string? detail, CancellationToken ct = default)
        {
            OpenedAttachmentId = attachmentId;
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<InspectionAuditEntry>> QueryAuditAsync(Guid? inspectionRecordId, Guid? attachmentId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionAuditEntry>>([]);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Ingot.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
