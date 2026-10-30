// Verify review listing passes an authorized site to storage and rejects foreign-site requests.

using System.Security.Claims;
using Ingot.Contracts.Inspections;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Api.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class InspectionReviewsControllerTests
{
    [Fact]
    public async Task Query_PassesCanonicalAuthorizedSiteToStore()
    {
        var store = new RecordingReviewStore();
        var controller = CreateController(store, Identity("SITE-A"));

        var result = await controller.Query(null, null, 25, CancellationToken.None, "site-a");

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("SITE-A", store.SiteId);
        Assert.Equal(1, store.QueryCount);
    }

    [Fact]
    public async Task Query_RejectsForeignSiteBeforeReadingStore()
    {
        var store = new RecordingReviewStore();
        var controller = CreateController(store, Identity("SITE-A"));

        var result = Assert.IsType<ObjectResult>(
            await controller.Query(null, null, 25, CancellationToken.None, "SITE-B"));

        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
        Assert.Equal(0, store.QueryCount);
    }

    private static InspectionReviewsController CreateController(
        RecordingReviewStore reviews,
        ClaimsPrincipal principal)
        => new(
            new InspectionQueries(null!, null!, null!, reviews),
            null!,
            new PlatformUserResolver(new ProductionEnvironment()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            }
        };

    private static ClaimsPrincipal Identity(string siteId)
        => new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, "reviewer"),
            new Claim(ClaimTypes.Role, PlatformRoles.QualityReviewer),
            new Claim(PlatformClaimTypes.SiteId, siteId)
        ], "test"));

    private sealed class RecordingReviewStore : IInspectionReviewStore
    {
        public string? SiteId { get; private set; }
        public int QueryCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<StoreInspectionReviewResult> CreateAsync(CreateInspectionReviewRequest request, string executionId, string reviewedBy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InspectionReview>> QueryAsync(Guid? inspectionRecordId, string? executionId, int limit, string? siteId = null, CancellationToken ct = default)
        {
            QueryCount++;
            SiteId = siteId;
            return Task.FromResult<IReadOnlyList<InspectionReview>>([]);
        }
        public Task<IReadOnlyDictionary<Guid, InspectionReview>> GetLatestByInspectionRecordIdsAsync(IReadOnlyCollection<Guid> inspectionRecordIds, CancellationToken ct = default) => throw new NotSupportedException();
        public Task LogAccessAsync(Guid? inspectionRecordId, Guid? attachmentId, string action, string actor, string? detail, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InspectionAuditEntry>> QueryAuditAsync(Guid? inspectionRecordId, Guid? attachmentId, int limit, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ingot.Core.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
