// 为机理声明提供按站点隔离的浏览、草稿、审核、冲突与证据升级入口。
using System.Security.Cryptography;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Application.ResearchAssets;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/mechanism-knowledge")]
public sealed class MechanismKnowledgeController(
    MechanismKnowledgeService knowledge,
    MechanismKnowledgeQueries queries,
    ResearchAssetApplication assets,
    MechanismClaimDraftService draftService,
    ResearchAssetWorkflow assetWorkflow,
    RecipeKnowledgeScopeReader recipeScopes,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet("claims")]
    public async Task<IActionResult> ListClaims(
        [FromQuery] string? siteCode,
        [FromQuery] string? processSpecificationId,
        CancellationToken ct)
    {
        var denied = DeniedKnowledgeRead();
        if (denied is not null) return denied;
        var siteFailure = ResolveSiteScope(siteCode, out var authorizedSiteCode);
        if (siteFailure is not null) return siteFailure;
        if (string.IsNullOrWhiteSpace(processSpecificationId))
            return InvalidRequest("必须指定配方编号。");

        var claims = await knowledge.ListClaimsAsync(
            authorizedSiteCode!, processSpecificationId.Trim(), ct).ConfigureAwait(false);
        return Ok(new { data = claims });
    }

    [HttpGet("conflicts")]
    public async Task<IActionResult> ListConflicts(
        [FromQuery] string? siteCode,
        [FromQuery] string? processSpecificationId,
        CancellationToken ct)
    {
        var denied = DeniedKnowledgeRead();
        if (denied is not null) return denied;
        var siteFailure = ResolveSiteScope(siteCode, out var authorizedSiteCode);
        if (siteFailure is not null) return siteFailure;
        if (string.IsNullOrWhiteSpace(processSpecificationId))
            return InvalidRequest("必须指定配方编号。");

        var conflicts = await knowledge.ListConflictsAsync(
            authorizedSiteCode!, processSpecificationId.Trim(), ct).ConfigureAwait(false);
        return Ok(new { data = conflicts });
    }

    [HttpGet("sources")]
    public async Task<IActionResult> ListSources(
        [FromQuery] string? siteCode,
        [FromQuery] string? processSpecificationId,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var denied = DeniedKnowledgeRead();
        if (denied is not null) return denied;
        var siteFailure = ResolveSiteScope(siteCode, out var authorizedSiteCode);
        if (siteFailure is not null) return siteFailure;
        var pageFailure = ValidateSourcePage(limit, cursor);
        if (pageFailure is not null) return pageFailure;
        var normalizedProcessSpecificationId = string.IsNullOrWhiteSpace(processSpecificationId)
            ? null
            : processSpecificationId.Trim();
        if (normalizedProcessSpecificationId is { Length: > 120 })
            return InvalidRequest("配方编号最长 120 个字符。");

        ResearchAssetPage<KnowledgeSource> page;
        try
        {
            page = await assets.ListReviewedKnowledgeSourcesPageAsync(
                authorizedSiteCode!, normalizedProcessSpecificationId, limit, cursor, ct).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
        return Ok(new
        {
            data = page.Data.Select(static source => new
                {
                    source.SourceId,
                    source.SiteCode,
                    source.Title,
                    source.SourceKind,
                    source.FileName,
                    source.Sha256,
                    source.Status,
                    source.ExtractionStatus,
                    source.ContextSelector,
                    source.ReviewedAt
                }),
            nextCursor = page.NextCursor
        });
    }

    [HttpGet("sources/manage")]
    public async Task<IActionResult> ListSourcesForManagement(
        [FromQuery] string? siteCode,
        [FromQuery] int limit = 50,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var siteFailure = ResolveSiteScope(siteCode, out var authorizedSiteCode);
        if (siteFailure is not null) return siteFailure;
        var pageFailure = ValidateSourcePage(limit, cursor);
        if (pageFailure is not null) return pageFailure;

        ResearchAssetPage<KnowledgeSource> page;
        try
        {
            page = await assets.ListKnowledgeSourcesForManagementPageAsync(
                authorizedSiteCode!, limit, cursor, ct).ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
        return Ok(new
        {
            data = page.Data.Select(SourceMetadata),
            nextCursor = page.NextCursor
        });
    }

    [HttpGet("sources/{sourceId:guid}")]
    public async Task<IActionResult> GetSource(Guid sourceId, CancellationToken ct)
    {
        var denied = DeniedKnowledgeRead();
        if (denied is not null) return denied;
        var source = await assets.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        var identity = ResolveIdentity();
        if (source is null || identity is null || !identity.CanAccessSite(source.SiteCode))
            return ResourceNotFound();

        var canManageSources = identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator);
        if (!canManageSources && source.Status != KnowledgeSourceStatuses.Reviewed)
            return ResourceNotFound();
        return Ok(new { data = SourceMetadata(source) });
    }

    [HttpPost("sources")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(55 * 1024 * 1024)]
    public async Task<IActionResult> UploadSource(
        [FromForm] IFormFile? file,
        [FromForm] string? title,
        [FromForm] string? sourceKind,
        [FromForm] string? processSpecificationId,
        [FromForm] string? siteCode,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (file is null || file.Length == 0) return InvalidRequest("请选择非空的知识来源文件。");
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 240)
            return InvalidRequest("资料标题不能为空且最长 240 个字符。");
        if (string.IsNullOrWhiteSpace(sourceKind) || sourceKind.Trim().Length > 80)
            return InvalidRequest("资料类型不能为空且最长 80 个字符。");

        var siteFailure = ResolveSiteScope(siteCode, out var authorizedSiteCode);
        if (siteFailure is not null) return siteFailure;
        var identity = ResolveIdentity();
        if (identity is null) return AuthenticationRequired("需要平台统一认证。");

        try
        {
            var normalizedProcessSpecificationId = string.IsNullOrWhiteSpace(processSpecificationId)
                ? null
                : processSpecificationId.Trim();
            if (normalizedProcessSpecificationId is { Length: > 120 })
                return InvalidRequest("配方编号最长 120 个字符。");

            var contextSelector = new Dictionary<string, string>();
            if (normalizedProcessSpecificationId is not null)
            {
                var scope = await recipeScopes.ReadAsync(
                    authorizedSiteCode, normalizedProcessSpecificationId, ct).ConfigureAwait(false);
                normalizedProcessSpecificationId = scope.ProcessSpecificationId;
                contextSelector[MechanismKnowledgeService.ProcessSpecificationDimension] = scope.ProcessSpecificationId;
            }

            string contentHash;
            await using (var hashingStream = file.OpenReadStream())
                contentHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(hashingStream, ct).ConfigureAwait(false));
            var existing = await assets.FindKnowledgeSourceByHashAsync(
                authorizedSiteCode!, contentHash, ct).ConfigureAwait(false);
            if (existing?.Status == KnowledgeSourceStatuses.Retired)
                return StateConflict("同一站点的相同内容来源已退役；请上传修订后的来源文件。");
            if (existing is not null && !IsSourceInExactProcessSpecificationScope(existing, normalizedProcessSpecificationId))
                return StateConflict("同一站点的相同内容已登记为不同适用范围；重复上传不能更改或扩大范围。请查看现有来源，或上传修订后的文件。");

            await using var content = file.OpenReadStream();
            var source = await assets.AddKnowledgeSourceAsync(
                content,
                title.Trim(),
                sourceKind.Trim(),
                file.FileName,
                file.ContentType,
                authorizedSiteCode!,
                contextSelector,
                identity.UserId,
                ct).ConfigureAwait(false);
            if (source.Status == KnowledgeSourceStatuses.Retired)
                return StateConflict("同一站点的相同内容来源已退役；请上传修订后的来源文件。");
            if (!IsSourceInExactProcessSpecificationScope(source, normalizedProcessSpecificationId))
                return StateConflict("同一站点的相同内容已登记为不同适用范围；重复上传不能更改或扩大范围。请查看现有来源，或上传修订后的文件。");
            return Ok(new
            {
                data = SourceMetadata(source),
                registrationStatus = existing is null ? "registered-or-existing" : "existing"
            });
        }
        catch (InvalidDataException exception)
        {
            if (exception.Message.Contains("上限", StringComparison.Ordinal))
                return PayloadTooLarge(exception.Message);
            return InvalidRequest(exception.Message);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpGet("sources/{sourceId:guid}/records")]
    public async Task<IActionResult> ListSourceRecords(Guid sourceId, CancellationToken ct)
    {
        var denied = DeniedKnowledgeRead();
        if (denied is not null) return denied;
        var source = await assets.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        var identity = ResolveIdentity();
        if (source is null || identity is null || !identity.CanAccessSite(source.SiteCode))
            return ResourceNotFound();

        var canManageSources = identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator);
        if (!canManageSources && source.Status != KnowledgeSourceStatuses.Reviewed)
            return ResourceNotFound();

        var records = (await assets.ListKnowledgeRecordsAsync(sourceId, ct).ConfigureAwait(false))
            .Where(record => canManageSources || record.HumanReviewed)
            .OrderBy(static record => record.PageOrSheet)
            .ThenBy(static record => record.Region);
        return Ok(new
        {
            data = records.Select(record => new
            {
                record.RecordId,
                record.SourceId,
                record.Category,
                record.PageOrSheet,
                record.Region,
                record.Content,
                record.Citation,
                record.HumanReviewed,
                record.ReviewedBy,
                record.ReviewedAt,
                ContentHash = record.Citation?.ContentHash ?? KnowledgeContentFingerprint.ComputeHash(record.Content)
            })
        });
    }

    [HttpPost("sources/{sourceId:guid}/extract")]
    public async Task<IActionResult> ExtractSource(Guid sourceId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var source = await assets.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (!CanAccessSite(source?.SiteCode)) return ResourceNotFound();
        if (source!.Status is not (KnowledgeSourceStatuses.Uploaded or KnowledgeSourceStatuses.Indexed) ||
            source.ExtractionStatus is not ("pending" or "failed"))
            return StateConflict("只有待解析或解析失败的来源可以启动或重试抽取。");

        try
        {
            var userId = ResolveUserId()!;
            await assets.EnqueueKnowledgeExtractionAsync(sourceId, userId, ct).ConfigureAwait(false);
            var updated = await assets.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
            return Ok(new { data = SourceMetadata(updated!) });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("sources/{sourceId:guid}/index")]
    public async Task<IActionResult> IndexSource(Guid sourceId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var source = await assets.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (!CanAccessSite(source?.SiteCode)) return ResourceNotFound();
        if (source!.ExtractionStatus != "completed")
            return StateConflict("來源抽取完成後才能進入索引狀態。");

        try
        {
            var updated = await assetWorkflow.ChangeKnowledgeSourceStatusAsync(
                sourceId, KnowledgeSourceStatuses.Indexed, ResolveUserId()!, ct).ConfigureAwait(false);
            return Ok(new { data = SourceMetadata(updated) });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("sources/{sourceId:guid}/review")]
    public async Task<IActionResult> ReviewSource(Guid sourceId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var source = await assets.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (!CanAccessSite(source?.SiteCode)) return ResourceNotFound();

        try
        {
            var updated = await assetWorkflow.ChangeKnowledgeSourceStatusAsync(
                sourceId, KnowledgeSourceStatuses.Reviewed, ResolveUserId()!, ct).ConfigureAwait(false);
            return Ok(new { data = SourceMetadata(updated) });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("sources/{sourceId:guid}/records/{recordId:guid}/review")]
    public async Task<IActionResult> ReviewSourceRecord(Guid sourceId, Guid recordId, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        var source = await assets.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (!CanAccessSite(source?.SiteCode)) return ResourceNotFound();
        if (source!.Status != KnowledgeSourceStatuses.Indexed || source.ExtractionStatus != "completed")
            return StateConflict("只有已完成抽取且处于 indexed 状态的来源片段可以复核。");
        var record = (await assets.ListKnowledgeRecordsAsync(sourceId, ct).ConfigureAwait(false))
            .FirstOrDefault(value => value.RecordId == recordId);
        if (record is null) return ResourceNotFound();

        try
        {
            var saved = await assetWorkflow.SaveKnowledgeRecordAsync(
                record with { HumanReviewed = true }, ResolveUserId()!, ct).ConfigureAwait(false);
            return Ok(new { data = saved });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("claims")]
    public async Task<IActionResult> SaveDraft([FromBody] MechanismClaimVersion? request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("机理声明不能为空。");
        var identity = ResolveIdentity();
        if (identity is null) return AuthenticationRequired("需要平台统一认证。");

        string authorizedSiteCode;
        if (request.ClaimId != Guid.Empty)
        {
            var existing = await queries.GetClaimAsync(request.ClaimId, null, ct).ConfigureAwait(false);
            if (existing is null || !identity.CanAccessSite(existing.SiteCode)) return ResourceNotFound();
            authorizedSiteCode = existing.SiteCode;
        }
        else
        {
            var siteFailure = ResolveSiteScope(request.SiteCode, out var resolvedSiteCode);
            if (siteFailure is not null) return siteFailure;
            authorizedSiteCode = resolvedSiteCode!;
        }

        try
        {
            return Ok(await knowledge.SaveDraftAsync(
                request with { SiteCode = authorizedSiteCode }, identity.UserId, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("claims/{claimId:guid}/review")]
    public async Task<IActionResult> Review(
        Guid claimId,
        [FromBody] MechanismClaimReviewRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("审核决定不能为空。");
        var claim = await queries.GetClaimAsync(claimId, null, ct).ConfigureAwait(false);
        if (!CanAccessSite(claim?.SiteCode)) return ResourceNotFound();

        try
        {
            return Ok(await knowledge.ReviewAsync(claimId, request, ResolveUserId()!, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("claims/{claimId:guid}/lifecycle")]
    public async Task<IActionResult> Transition(
        Guid claimId,
        [FromBody] MechanismClaimLifecycleRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("生命周期决定不能为空。");
        var claim = await queries.GetClaimAsync(claimId, null, ct).ConfigureAwait(false);
        if (!CanAccessSite(claim?.SiteCode)) return ResourceNotFound();

        try
        {
            return Ok(await knowledge.TransitionAsync(claimId, request, ResolveUserId()!, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("conflicts")]
    public async Task<IActionResult> AddConflict(
        [FromBody] MechanismClaimConflictRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("冲突说明不能为空。");
        var left = await queries.GetClaimAsync(request.LeftClaimId, request.LeftClaimVersion, ct).ConfigureAwait(false);
        var right = await queries.GetClaimAsync(request.RightClaimId, request.RightClaimVersion, ct).ConfigureAwait(false);
        if (!CanAccessSite(left?.SiteCode) || !CanAccessSite(right?.SiteCode)) return ResourceNotFound();

        try
        {
            return Ok(await knowledge.AddConflictAsync(request, ResolveUserId()!, ct).ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("conflicts/{conflictId:guid}/resolve")]
    public async Task<IActionResult> ResolveConflict(
        Guid conflictId,
        [FromBody] MechanismClaimConflictResolutionRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("冲突处理结论不能为空。");
        var conflict = await queries.GetConflictAsync(conflictId, ct).ConfigureAwait(false);
        if (!CanAccessSite(conflict?.SiteCode)) return ResourceNotFound();

        try
        {
            return Ok(await knowledge.ResolveConflictAsync(conflictId, request, ResolveUserId()!, ct)
                .ConfigureAwait(false));
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("draft-suggestions")]
    public async Task<IActionResult> GenerateDraftSuggestion(
        [FromBody] MechanismClaimDraftGenerationRequest? request,
        CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null) return denied;
        if (request is null) return InvalidRequest("草稿生成请求不能为空。");
        var source = await assets.GetKnowledgeSourceAsync(request.SourceId, ct).ConfigureAwait(false);
        if (!CanAccessSite(source?.SiteCode) || source?.Status != KnowledgeSourceStatuses.Reviewed)
            return ResourceNotFound();
        if (!IsSourceInProcessSpecificationScope(source!, request.ProcessSpecificationId))
            return StateConflict("当前资料限定于其他配方，不能为所选配方生成机理草稿。");
        var sourceRecords = await assets.ListKnowledgeRecordsAsync(request.SourceId, ct).ConfigureAwait(false);
        if (sourceRecords.Any(static record => !string.IsNullOrWhiteSpace(record.Content) && !record.HumanReviewed))
            return StateConflict("来源仍包含未复核的知识片段，请先完成片段复核。");

        try
        {
            var draft = await draftService.GenerateAsync(request, ResolveUserId()!, ct).ConfigureAwait(false);
            return Ok(new { draft, persisted = false });
        }
        catch (InvalidOperationException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    private IActionResult? ResolveSiteScope(string? requestedSiteCode, out string? siteCode)
    {
        var failure = PlatformSiteScope.Resolve(ResolveIdentity()!, requestedSiteCode, false, out siteCode);
        return failure switch
        {
            SiteScopeFailure.Forbidden => AuthorizationDenied("当前身份无权访问该站点。", ("siteCode", requestedSiteCode)),
            SiteScopeFailure.Missing => InvalidRequest("必须指定当前身份有权访问的 siteCode。"),
            _ => null
        };
    }

    private bool CanAccessSite(string? siteCode)
        => siteCode is not null && ResolveIdentity()?.CanAccessSite(siteCode) == true;

    private static object SourceMetadata(KnowledgeSource source)
        => new
        {
            source.SourceId,
            source.SiteCode,
            source.Title,
            source.SourceKind,
            source.Status,
            source.FileName,
            source.MediaType,
            source.SizeBytes,
            source.Sha256,
            source.ContextSelector,
            source.UploadedBy,
            source.UploadedAt,
            source.ReviewedBy,
            source.ReviewedAt,
            source.ExtractionStatus,
            source.ExtractionError,
            source.ExtractorVersion
        };

    private static bool IsSourceInProcessSpecificationScope(
        KnowledgeSource source,
        string? processSpecificationId)
    {
        if (!TryReadProcessSpecificationScope(source.ContextSelector, out var scopedProcessSpecification))
            return false;
        return scopedProcessSpecification is null ||
               !string.IsNullOrWhiteSpace(processSpecificationId) &&
               string.Equals(scopedProcessSpecification, processSpecificationId.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourceInExactProcessSpecificationScope(
        KnowledgeSource source,
        string? processSpecificationId)
    {
        if (!TryReadProcessSpecificationScope(source.ContextSelector, out var scopedProcessSpecification))
            return false;
        var requestedScope = string.IsNullOrWhiteSpace(processSpecificationId)
            ? null
            : processSpecificationId.Trim();
        return scopedProcessSpecification is null
            ? requestedScope is null
            : requestedScope is not null && string.Equals(
                scopedProcessSpecification,
                requestedScope,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadProcessSpecificationScope(
        IReadOnlyDictionary<string, string> contextSelector,
        out string? processSpecificationId)
    {
        var scopedValues = contextSelector
            .Where(pair => string.Equals(
                pair.Key.Trim(),
                MechanismKnowledgeService.ProcessSpecificationDimension,
                StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Value)
            .ToArray();
        if (scopedValues.Length == 0)
        {
            processSpecificationId = null;
            return true;
        }
        if (scopedValues.Length != 1 || string.IsNullOrWhiteSpace(scopedValues[0]))
        {
            processSpecificationId = null;
            return false;
        }

        processSpecificationId = scopedValues[0].Trim();
        return true;
    }

    private IActionResult? ValidateSourcePage(int limit, string? cursor)
    {
        if (limit is < 1 or > 100)
            return InvalidRequest("来源列表 limit 必须在 1 到 100 之间。");
        if (cursor?.Length > 1024)
            return InvalidRequest("来源列表 cursor 长度不能超过 1024 个字符。");
        return null;
    }
}
