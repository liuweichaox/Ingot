using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Api.Agents;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/training-datasets")]
public sealed class TrainingDatasetsController(
    ResearchAssetApplication store,
    ResearchAssetWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 200, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null) return denied;
        if (limit is < 1 or > 200) return InvalidRequest("limit 必须在 1 到 200 之间。");
        return Ok(await store.ListDatasetsPageAsync(limit, cursor, ct).ConfigureAwait(false));
    }

    [HttpGet("{datasetId}/{version:int}")]
    public async Task<IActionResult> Get(string datasetId, int version, CancellationToken ct)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null)
            return denied;
        var value = await store.GetDatasetAsync(datasetId.Trim().ToLowerInvariant(), version, ct)
            .ConfigureAwait(false);
        return value is null ? ResourceNotFound() : Ok(value);
    }

    [HttpPost]
    public async Task<IActionResult> Register([FromBody] TrainingDatasetVersion request, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await workflow.RegisterDatasetAsync(
                request,
                ResolveUserId()!,
                ct).ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
    }
}

[ApiController]
[Route("api/v1/process-models")]
public sealed class ProcessModelsController(
    ResearchAssetApplication store,
    ResearchAssetWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = 200, [FromQuery] string? cursor = null, CancellationToken ct = default)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null) return denied;
        if (limit is < 1 or > 200) return InvalidRequest("limit 必须在 1 到 200 之间。");
        return Ok(await store.ListModelsPageAsync(limit, cursor, ct).ConfigureAwait(false));
    }

    [HttpGet("{modelId}/{version:int}")]
    public async Task<IActionResult> Get(
        string modelId,
        int version,
        [FromQuery] int limit = 200,
        [FromQuery] string? evaluationCursor = null,
        [FromQuery] string? driftCursor = null,
        CancellationToken ct = default)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null)
            return denied;
        var normalizedId = modelId.Trim().ToLowerInvariant();
        var model = await store.GetModelAsync(normalizedId, version, ct).ConfigureAwait(false);
        if (model is null)
            return ResourceNotFound();
        if (limit is < 1 or > 200) return InvalidRequest("limit 必须在 1 到 200 之间。");
        var evaluations = await store.ListEvaluationsPageAsync(
            normalizedId, version, limit, evaluationCursor, ct).ConfigureAwait(false);
        var driftReadings = await store.ListDriftReadingsPageAsync(
            normalizedId, version, limit, driftCursor, ct).ConfigureAwait(false);
        var audit = await store.ListAuditEntriesAsync("model", $"{normalizedId}:{version}", ct)
            .ConfigureAwait(false);
        return Ok(new
        {
            model,
            evaluations = evaluations.Data,
            evaluationNextCursor = evaluations.NextCursor,
            driftReadings = driftReadings.Data,
            driftNextCursor = driftReadings.NextCursor,
            audit
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveDraft([FromBody] ProcessModelVersion request, CancellationToken ct)
        => await ExecuteWriteAsync(
            () => workflow.SaveModelDraftAsync(request, ResolveUserId()!, ct),
            ct).ConfigureAwait(false);

    [HttpPost("{modelId}/{version:int}/evaluations")]
    public async Task<IActionResult> AddEvaluation(
        string modelId,
        int version,
        [FromBody] ModelEvaluation request,
        CancellationToken ct)
        => await ExecuteWriteAsync(
            () => workflow.AddEvaluationAsync(
                request with
                {
                    ModelId = modelId.Trim().ToLowerInvariant(),
                    ModelVersion = version
                },
                ResolveUserId()!,
                ct),
            ct).ConfigureAwait(false);

    [HttpPost("{modelId}/{version:int}/drift")]
    public async Task<IActionResult> AddDrift(
        string modelId,
        int version,
        [FromBody] ModelDriftReading request,
        CancellationToken ct)
        => await ExecuteWriteAsync(
            () => workflow.RecordDriftAsync(
                request with
                {
                    ModelId = modelId.Trim().ToLowerInvariant(),
                    ModelVersion = version
                },
                ResolveUserId()!,
                ct),
            ct).ConfigureAwait(false);

    [HttpPost("{modelId}/{version:int}/status")]
    public async Task<IActionResult> ChangeStatus(
        string modelId,
        int version,
        [FromBody] StatusChangeRequest request,
        CancellationToken ct)
        => await ExecuteWriteAsync(
            () => workflow.ChangeModelStatusAsync(
                modelId,
                version,
                request.TargetStatus,
                ResolveUserId()!,
                ct),
            ct).ConfigureAwait(false);

    [HttpPost("{modelId}/rollback")]
    public async Task<IActionResult> Rollback(
        string modelId,
        [FromBody] ModelRollbackRequest request,
        CancellationToken ct)
        => await ExecuteWriteAsync(
            () => workflow.RollbackModelAsync(
                modelId,
                request.CurrentVersion,
                request.TargetVersion,
                ResolveUserId()!,
                ct),
            ct).ConfigureAwait(false);

    private async Task<IActionResult> ExecuteWriteAsync<T>(Func<Task<T>> operation, CancellationToken ct)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await operation().ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
    }
}

[ApiController]
[Route("api/v1/process-knowledge")]
public sealed class ProcessKnowledgeController(
    ResearchAssetApplication store,
    ProcessResearchQueries researchStore,
    ResearchAssetWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    private static readonly HashSet<string> AllowedExtensions = new(
        [".pdf", ".xlsx", ".xlsm", ".csv", ".txt", ".md", ".png", ".jpg", ".jpeg", ".webp", ".tif", ".tiff"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AllowedSourceKinds = new(
        ["document", "spreadsheet", "image", "field-note"],
        StringComparer.Ordinal);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid projectId,
        [FromQuery] int limit = 200,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var access = await ResolveProjectAccessAsync(projectId, false, ct).ConfigureAwait(false);
        if (access.Result is not null)
            return access.Result;
        if (limit is < 1 or > 200) return InvalidRequest("limit 必须在 1 到 200 之间。");
        return Ok(await store.ListKnowledgeSourcesPageAsync(projectId, limit, cursor, ct).ConfigureAwait(false));
    }

    [HttpGet("{sourceId:guid}")]
    public async Task<IActionResult> Get(Guid sourceId, CancellationToken ct)
    {
        var access = await ResolveSourceAccessAsync(sourceId, false, ct).ConfigureAwait(false);
        if (access.Result is not null)
            return access.Result;
        var records = await store.ListKnowledgeRecordsAsync(sourceId, ct).ConfigureAwait(false);
        var audit = await store.ListAuditEntriesAsync("knowledge-source", sourceId.ToString(), ct)
            .ConfigureAwait(false);
        return Ok(new { source = access.Source, records, audit });
    }

    [HttpGet("{sourceId:guid}/content")]
    public async Task<IActionResult> Download(Guid sourceId, CancellationToken ct)
    {
        var access = await ResolveSourceAccessAsync(sourceId, false, ct).ConfigureAwait(false);
        if (access.Result is not null)
            return access.Result;
        var content = await store.OpenKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        return content is null
            ? ResourceNotFound("知识来源文件不可用。")
            : File(content, access.Source!.MediaType, access.Source.FileName, enableRangeProcessing: true);
    }

    [HttpPost]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        [FromForm] Guid projectId,
        [FromForm] string title,
        [FromForm] string sourceKind = "document",
        CancellationToken ct = default)
    {
        var access = await ResolveProjectAccessAsync(projectId, true, ct).ConfigureAwait(false);
        if (access.Result is not null)
            return access.Result;
        if (file.Length <= 0 || !AllowedExtensions.Contains(Path.GetExtension(file.FileName)))
            return InvalidRequest("仅支持文档、表格、文本和常见现场图片格式。");
        if (!await HasExpectedFileSignatureAsync(file, ct).ConfigureAwait(false))
            return InvalidRequest("文件内容与扩展名不一致，已拒绝解析。");
        sourceKind = sourceKind?.Trim().ToLowerInvariant() ?? "";
        if (!AllowedSourceKinds.Contains(sourceKind))
            return InvalidRequest("来源类型仅支持 document、spreadsheet、image 或 field-note。");
        var project = access.Project!;
        var currentUser = access.Identity!.UserId;
        IReadOnlyDictionary<string, string> contextSelector =
            new Dictionary<string, string>(project.Context, StringComparer.Ordinal)
            {
                ["research-project-id"] = project.ProjectId.ToString(),
                ["process-name"] = project.ProcessName,
                ["product-name"] = project.ProductName ?? "",
                ["material-name"] = project.MaterialName ?? "",
                ["site-code"] = project.SiteCode ?? ""
            };
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 240)
            return InvalidRequest("知识来源标题不能为空且最长 240 个字符。");
        try
        {
            await using var stream = file.OpenReadStream();
            var saved = await store.AddKnowledgeSourceAsync(
                stream,
                title,
                sourceKind,
                file.FileName,
                file.ContentType,
                contextSelector,
                currentUser,
                ct).ConfigureAwait(false);
            await store.AddAuditEntryAsync(
                new ResearchAssetAuditEntry
                {
                    EntryId = Guid.CreateVersion7(),
                    ResourceType = "knowledge-source",
                    ResourceId = saved.SourceId.ToString(),
                    Action = "uploaded",
                    ToStatus = saved.Status,
                    UserId = currentUser,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                ct).ConfigureAwait(false);
            return Accepted(saved);
        }
        catch (InvalidDataException exception)
        {
            return InvalidRequest(exception.Message);
        }
    }

    [HttpPost("{sourceId:guid}/extract")]
    public async Task<IActionResult> Extract(Guid sourceId, CancellationToken ct)
    {
        var access = await ResolveSourceAccessAsync(sourceId, true, ct).ConfigureAwait(false);
        if (access.Result is not null)
            return access.Result;
        try
        {
            await store.EnqueueKnowledgeExtractionAsync(sourceId, access.Identity!.UserId, ct)
                .ConfigureAwait(false);
            return Accepted(new { sourceId, extractionStatus = "pending" });
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    [HttpPost("{sourceId:guid}/records")]
    public async Task<IActionResult> SaveRecord(
        Guid sourceId,
        [FromBody] KnowledgeRecord request,
        CancellationToken ct)
    {
        var access = await ResolveSourceAccessAsync(sourceId, true, ct).ConfigureAwait(false);
        if (access.Result is not null)
            return access.Result;
        return await ExecuteWriteAsync(
            () => workflow.SaveKnowledgeRecordAsync(
                request with { SourceId = sourceId },
                access.Identity!.UserId,
                ct)).ConfigureAwait(false);
    }

    [HttpPost("{sourceId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(
        Guid sourceId,
        [FromBody] StatusChangeRequest request,
        CancellationToken ct)
    {
        var access = await ResolveSourceAccessAsync(sourceId, true, ct).ConfigureAwait(false);
        if (access.Result is not null)
            return access.Result;
        return await ExecuteWriteAsync(
            () => workflow.ChangeKnowledgeSourceStatusAsync(
                sourceId,
                request.TargetStatus,
                access.Identity!.UserId,
                ct)).ConfigureAwait(false);
    }

    private async Task<IActionResult> ExecuteWriteAsync<T>(Func<Task<T>> operation)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await operation().ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return StateConflict(exception.Message);
        }
    }

    private async Task<(KnowledgeSource? Source, PlatformIdentity? Identity, IActionResult? Result)>
        ResolveSourceAccessAsync(Guid sourceId, bool requireWrite, CancellationToken ct)
    {
        var source = await store.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
            return (null, null, ResourceNotFound("知识来源不存在。"));
        if (!source.ContextSelector.TryGetValue("research-project-id", out var projectIdText) ||
            !Guid.TryParse(projectIdText, out var projectId))
            return (null, null, AuthorizationDenied());
        var access = await ResolveProjectAccessAsync(projectId, requireWrite, ct).ConfigureAwait(false);
        return access.Result is null
            ? (source, access.Identity, null)
            : (null, null, access.Result);
    }

    private static async Task<bool> HasExpectedFileSignatureAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var count = await stream.ReadAsync(header, ct).ConfigureAwait(false);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => count >= 5 && header.AsSpan(0, 5).SequenceEqual("%PDF-"u8),
            ".xlsx" or ".xlsm" => count >= 4 && header[0] == 0x50 && header[1] == 0x4b &&
                header[2] is 0x03 or 0x05 or 0x07 && header[3] is 0x04 or 0x06 or 0x08,
            ".png" => count >= 8 && header.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            ".jpg" or ".jpeg" => count >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
            ".webp" => count >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
                header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".tif" or ".tiff" => count >= 4 &&
                (header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x49, 0x49, 0x2a, 0x00 }) ||
                 header.AsSpan(0, 4).SequenceEqual(new byte[] { 0x4d, 0x4d, 0x00, 0x2a })),
            ".txt" or ".md" or ".csv" => !header.AsSpan(0, count).Contains((byte)0),
            _ => false
        };
    }

    private async Task<(ResearchProject? Project, PlatformIdentity? Identity, IActionResult? Result)>
        ResolveProjectAccessAsync(Guid projectId, bool requireWrite, CancellationToken ct)
    {
        var identity = ResolveIdentity();
        if (identity is null)
            return (null, null, AuthenticationRequired("需要平台统一认证。"));
        if (!identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator))
            return (null, null, AuthorizationDenied());
        if (projectId == Guid.Empty)
            return (null, null, InvalidRequest("必须指定研发项目。"));
        var project = await researchStore.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
            return (null, null, ResourceNotFound("研发项目不存在。"));
        var canAccess = identity.HasAnyRole(PlatformRoles.PlatformAdministrator) ||
                        string.Equals(project.OwnerUserId, identity.UserId, StringComparison.Ordinal) ||
                        project.MemberUserIds.Contains(identity.UserId, StringComparer.Ordinal);
        if (!canAccess || requireWrite && (project.Status is
                ResearchProjectStatuses.Completed or ResearchProjectStatuses.Archived))
            return (null, null, AuthorizationDenied());
        return (project, identity, null);
    }
}

public sealed record StatusChangeRequest(string TargetStatus);
public sealed record ModelRollbackRequest(int CurrentVersion, int TargetVersion);
