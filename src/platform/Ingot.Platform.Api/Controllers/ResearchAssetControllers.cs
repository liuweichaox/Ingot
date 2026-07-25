using Ingot.Contracts.ResearchAssets;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/training-datasets")]
public sealed class TrainingDatasetsController(
    IResearchAssetStore store,
    ResearchAssetWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedResearchAssetRead() ??
           Ok(new { data = await store.ListDatasetsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{datasetId}/{version:int}")]
    public async Task<IActionResult> Get(string datasetId, int version, CancellationToken ct)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null)
            return denied;
        var value = await store.GetDatasetAsync(datasetId.Trim().ToLowerInvariant(), version, ct)
            .ConfigureAwait(false);
        return value is null ? NotFound() : Ok(value);
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
            return Conflict(new { error = exception.Message });
        }
    }
}

[ApiController]
[Route("api/v1/process-models")]
public sealed class ProcessModelsController(
    IResearchAssetStore store,
    ResearchAssetWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedResearchAssetRead() ??
           Ok(new { data = await store.ListModelsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{modelId}/{version:int}")]
    public async Task<IActionResult> Get(string modelId, int version, CancellationToken ct)
    {
        var denied = DeniedResearchAssetRead();
        if (denied is not null)
            return denied;
        var normalizedId = modelId.Trim().ToLowerInvariant();
        var model = await store.GetModelAsync(normalizedId, version, ct).ConfigureAwait(false);
        if (model is null)
            return NotFound();
        var evaluations = await store.ListEvaluationsAsync(normalizedId, version, ct).ConfigureAwait(false);
        var driftReadings = await store.ListDriftReadingsAsync(normalizedId, version, ct).ConfigureAwait(false);
        var audit = await store.ListAuditEntriesAsync("model", $"{normalizedId}:{version}", ct)
            .ConfigureAwait(false);
        return Ok(new { model, evaluations, driftReadings, audit });
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
            return Conflict(new { error = exception.Message });
        }
    }
}

[ApiController]
[Route("api/v1/process-knowledge")]
public sealed class ProcessKnowledgeController(
    IResearchAssetStore store,
    IProcessResearchStore researchStore,
    ResearchAssetWorkflow workflow,
    KnowledgeExtractionService extractionService,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    private static readonly HashSet<string> AllowedExtensions = new(
        [".pdf", ".xlsx", ".xlsm", ".csv", ".txt", ".md", ".png", ".jpg", ".jpeg", ".webp", ".tif", ".tiff"],
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AllowedSourceKinds = new(
        ["document", "spreadsheet", "image", "field-note"],
        StringComparer.Ordinal);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid projectId, CancellationToken ct)
    {
        var access = await ResolveProjectAccessAsync(projectId, false, ct).ConfigureAwait(false);
        if (access.Result is not null)
            return access.Result;
        var projectKey = projectId.ToString();
        var sources = (await store.ListKnowledgeSourcesAsync(ct).ConfigureAwait(false))
            .Where(source =>
                source.ContextSelector.TryGetValue("research-project-id", out var value) &&
                string.Equals(value, projectKey, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return Ok(new { data = sources });
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
            ? NotFound(new { error = "知识来源文件不可用。" })
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
            return BadRequest(new { error = "仅支持文档、表格、文本和常见现场图片格式。" });
        sourceKind = sourceKind?.Trim().ToLowerInvariant() ?? "";
        if (!AllowedSourceKinds.Contains(sourceKind))
            return BadRequest(new { error = "来源类型仅支持 document、spreadsheet、image 或 field-note。" });
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
            return BadRequest(new { error = "知识来源标题不能为空且最长 240 个字符。" });
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
            var indexed = await extractionService.ExtractAsync(
                saved.SourceId,
                currentUser,
                ct).ConfigureAwait(false);
            return Ok(indexed);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { error = exception.Message });
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
            return Ok(await extractionService.ExtractAsync(
                sourceId,
                access.Identity!.UserId,
                ct).ConfigureAwait(false));
        }
        catch (ResearchAssetRuleException exception)
        {
            return Conflict(new { error = exception.Message });
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
            return Conflict(new { error = exception.Message });
        }
    }

    private async Task<(KnowledgeSource? Source, PlatformIdentity? Identity, IActionResult? Result)>
        ResolveSourceAccessAsync(Guid sourceId, bool requireWrite, CancellationToken ct)
    {
        var source = await store.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
            return (null, null, NotFound(new { error = "知识来源不存在。" }));
        if (!source.ContextSelector.TryGetValue("research-project-id", out var projectIdText) ||
            !Guid.TryParse(projectIdText, out var projectId))
            return (null, null, Forbid());
        var access = await ResolveProjectAccessAsync(projectId, requireWrite, ct).ConfigureAwait(false);
        return access.Result is null
            ? (source, access.Identity, null)
            : (null, null, access.Result);
    }

    private async Task<(ResearchProject? Project, PlatformIdentity? Identity, IActionResult? Result)>
        ResolveProjectAccessAsync(Guid projectId, bool requireWrite, CancellationToken ct)
    {
        var identity = ResolveIdentity();
        if (identity is null)
            return (null, null, Unauthorized(new { error = "需要平台统一认证。" }));
        if (!identity.HasAnyRole(PlatformRoles.ProcessEngineer, PlatformRoles.PlatformAdministrator))
            return (null, null, Forbid());
        if (projectId == Guid.Empty)
            return (null, null, BadRequest(new { error = "必须指定研发项目。" }));
        var project = await researchStore.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        if (project is null)
            return (null, null, NotFound(new { error = "研发项目不存在。" }));
        var canAccess = identity.HasAnyRole(PlatformRoles.PlatformAdministrator) ||
                        string.Equals(project.OwnerUserId, identity.UserId, StringComparison.Ordinal) ||
                        project.MemberUserIds.Contains(identity.UserId, StringComparer.Ordinal);
        if (!canAccess || requireWrite && project.Status == ResearchProjectStatuses.Archived)
            return (null, null, Forbid());
        return (project, identity, null);
    }
}

public sealed record StatusChangeRequest(string TargetStatus);
public sealed record ModelRollbackRequest(int CurrentVersion, int TargetVersion);
