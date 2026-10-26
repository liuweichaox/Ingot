// 为 AI 引用提供经过站点权限和复核状态校验的知识片段只读入口。
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ResearchAssets;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/knowledge")]
public sealed class KnowledgeCitationController(
    ResearchAssetApplication assets,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet("sources/{sourceId:guid}/records/{recordId:guid}")]
    public async Task<IActionResult> GetCitation(Guid sourceId, Guid recordId, CancellationToken ct)
    {
        var denied = DeniedKnowledgeRead();
        if (denied is not null)
            return denied;

        var source = await assets.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        var identity = ResolveIdentity();
        if (source is null || identity is null || !identity.CanAccessSite(source.SiteCode) ||
            source.Status != KnowledgeSourceStatuses.Reviewed)
            return ResourceNotFound();

        var record = (await assets.ListKnowledgeRecordsAsync(sourceId, ct).ConfigureAwait(false))
            .FirstOrDefault(value => value.RecordId == recordId && value.HumanReviewed);
        return record is null
            ? ResourceNotFound()
            : Ok(new
            {
                source = new
                {
                    source.SourceId,
                    source.SiteCode,
                    source.Title,
                    source.SourceKind,
                    source.FileName,
                    source.Sha256,
                    source.ReviewedBy,
                    source.ReviewedAt
                },
                record
            });
    }
}
