// 验证平台组件 InspectionAttachmentsController 的成功、拒绝和安全边界。

using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
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
        var attachments = new StubAttachmentStore(attachmentId, bytes);
        var controller = new InspectionAttachmentsController(
            new InspectionQueries(null!, null!, attachments, reviews),
            new InspectionCommands(null!, null!, attachments, reviews, null!),
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
        Assert.Contains("attachment", controller.Response.Headers.ContentDisposition.ToString());
        Assert.Equal("default-src 'none'; sandbox", controller.Response.Headers.ContentSecurityPolicy.ToString());
        Assert.Equal(attachmentId, reviews.OpenedAttachmentId);
    }

    [Theory]
    [InlineData("sample.png", "89504e470d0a1a0a", "image/png")]
    [InlineData("sample.jpg", "ffd8ffe000104a46", "image/jpeg")]
    [InlineData("sample.tiff", "49492a0008000000", "image/tiff")]
    [InlineData("sample.pdf", "255044462d312e37", "application/pdf")]
    public void AttachmentPolicy_DetectsSupportedFileSignatures(
        string fileName,
        string prefixHex,
        string expectedMediaType)
    {
        var prefix = Convert.FromHexString(prefixHex);

        Assert.Equal(expectedMediaType, InspectionAttachmentPolicy.DetectMediaType(fileName, prefix));
    }

    [Theory]
    [InlineData("payload.html", "3c68746d6c3e")]
    [InlineData("payload.svg", "3c7376673e")]
    [InlineData("fake.png", "255044462d312e37")]
    public void AttachmentPolicy_RejectsActiveOrMismatchedContent(string fileName, string prefixHex)
        => Assert.Throws<InvalidDataException>(() =>
            InspectionAttachmentPolicy.DetectMediaType(fileName, Convert.FromHexString(prefixHex)));

    private sealed class StubAttachmentStore(Guid attachmentId, byte[] bytes) : IInspectionAttachmentStore
    {
        private readonly InspectionAttachment _attachment = new()
        {
            SiteId = "site-test",
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
            string siteId,
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
        public Task<StoreInspectionReviewResult> CreateAsync(CreateInspectionReviewRequest request, string executionId, string reviewedBy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default) => Task.FromResult<InspectionReview?>(null);
        public Task<IReadOnlyList<InspectionReview>> QueryAsync(Guid? inspectionRecordId, string? executionId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionReview>>([]);
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
