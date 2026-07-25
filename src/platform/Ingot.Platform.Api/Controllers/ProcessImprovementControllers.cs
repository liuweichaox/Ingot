using Ingot.Contracts.ProcessImprovement;
using Ingot.Platform.Api.Agents;
using Ingot.Platform.Infrastructure.ProcessImprovement;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Microsoft.AspNetCore.Mvc;

namespace Ingot.Platform.Api.Controllers;

[ApiController]
[Route("api/v1/training-datasets")]
public sealed class TrainingDatasetsController(
    IProcessImprovementStore store,
    ProcessImprovementWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await store.ListDatasetsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{datasetId}/{version:int}")]
    public async Task<IActionResult> Get(string datasetId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
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
        catch (ProcessImprovementRuleException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}

[ApiController]
[Route("api/v1/process-models")]
public sealed class ProcessModelsController(
    IProcessImprovementStore store,
    ProcessImprovementWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await store.ListModelsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{modelId}/{version:int}")]
    public async Task<IActionResult> Get(string modelId, int version, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
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
        catch (ProcessImprovementRuleException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}

[ApiController]
[Route("api/v1/process-investigations")]
[NonController]
public sealed class ProcessInvestigationsController(
    IProcessImprovementStore store,
    ProcessImprovementWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await store.ListInvestigationsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{investigationId:guid}")]
    public async Task<IActionResult> Get(Guid investigationId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var investigation = await store.GetInvestigationAsync(investigationId, ct).ConfigureAwait(false);
        if (investigation is null)
            return NotFound();
        var causes = await store.ListCausesAsync(investigationId, ct).ConfigureAwait(false);
        var trials = await store.ListTrialsAsync(investigationId, ct).ConfigureAwait(false);
        var conclusions = await store.ListConclusionsAsync(investigationId, ct).ConfigureAwait(false);
        var results = new Dictionary<Guid, IReadOnlyList<TrialResult>>();
        foreach (var trial in trials)
            results[trial.TrialId] = await store.ListTrialResultsAsync(trial.TrialId, ct).ConfigureAwait(false);
        var audit = await store.ListAuditEntriesAsync(
            "investigation",
            investigationId.ToString(),
            ct).ConfigureAwait(false);
        return Ok(new { investigation, causes, trials, results, conclusions, audit });
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] InvestigationCase request, CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.CreateInvestigationAsync(request, ResolveUserId()!, ct));

    [HttpPost("{investigationId:guid}/causes")]
    public Task<IActionResult> AddCause(
        Guid investigationId,
        [FromBody] PossibleCause request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.AddCauseAsync(
                request with { InvestigationId = investigationId },
                ResolveUserId()!,
                ct));

    [HttpPost("{investigationId:guid}/trials")]
    public Task<IActionResult> CreateTrial(
        Guid investigationId,
        [FromBody] ProcessTrial request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.CreateTrialAsync(
                request with { InvestigationId = investigationId },
                ResolveUserId()!,
                ct));

    [HttpPost("trials/{trialId:guid}/status")]
    public Task<IActionResult> ChangeTrialStatus(
        Guid trialId,
        [FromBody] StatusChangeRequest request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.ChangeTrialStatusAsync(
                trialId,
                request.TargetStatus,
                ResolveUserId()!,
                ct));

    [HttpPost("trials/{trialId:guid}/results")]
    public Task<IActionResult> AddTrialResult(
        Guid trialId,
        [FromBody] TrialResult request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.AddTrialResultAsync(
                request with { TrialId = trialId },
                ResolveUserId()!,
                ct));

    [HttpPost("trials/{trialId:guid}/results/calculate")]
    public Task<IActionResult> CalculateTrialResult(
        Guid trialId,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.CalculateTrialResultAsync(
                trialId,
                ResolveUserId()!,
                ct));

    [HttpPost("{investigationId:guid}/conclusions")]
    public Task<IActionResult> AddConclusion(
        Guid investigationId,
        [FromBody] InvestigationConclusion request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.AddConclusionAsync(
                request with { InvestigationId = investigationId },
                ResolveUserId()!,
                ct));

    private async Task<IActionResult> ExecuteWriteAsync<T>(Func<Task<T>> operation)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await operation().ConfigureAwait(false));
        }
        catch (ProcessImprovementRuleException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}

[ApiController]
[Route("api/v1/process-knowledge")]
public sealed class ProcessKnowledgeController(
    IProcessImprovementStore store,
    IProcessResearchStore researchStore,
    ProcessImprovementWorkflow workflow,
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
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await store.ListKnowledgeSourcesAsync(ct).ConfigureAwait(false) });

    [HttpGet("{sourceId:guid}")]
    public async Task<IActionResult> Get(Guid sourceId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var source = await store.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
            return NotFound();
        var records = await store.ListKnowledgeRecordsAsync(sourceId, ct).ConfigureAwait(false);
        var audit = await store.ListAuditEntriesAsync("knowledge-source", sourceId.ToString(), ct)
            .ConfigureAwait(false);
        return Ok(new { source, records, audit });
    }

    [HttpGet("{sourceId:guid}/content")]
    public async Task<IActionResult> Download(Guid sourceId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var source = await store.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
            return NotFound();
        var content = await store.OpenKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        return content is null
            ? NotFound(new { error = "知识来源文件不可用。" })
            : File(content, source.MediaType, source.FileName, enableRangeProcessing: true);
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
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        if (file.Length <= 0 || !AllowedExtensions.Contains(Path.GetExtension(file.FileName)))
            return BadRequest(new { error = "仅支持文档、表格、文本和常见现场图片格式。" });
        sourceKind = sourceKind?.Trim().ToLowerInvariant() ?? "";
        if (!AllowedSourceKinds.Contains(sourceKind))
            return BadRequest(new { error = "来源类型仅支持 document、spreadsheet、image 或 field-note。" });
        var project = await researchStore.GetProjectAsync(projectId, ct).ConfigureAwait(false);
        var currentUser = ResolveUserId()!;
        if (project is null ||
            !(string.Equals(project.OwnerUserId, currentUser, StringComparison.Ordinal) ||
              project.MemberUserIds.Contains(currentUser, StringComparer.Ordinal)))
            return Forbid();
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
                ResolveUserId()!,
                ct).ConfigureAwait(false);
            await store.AddAuditEntryAsync(
                new ImprovementAuditEntry
                {
                    EntryId = Guid.CreateVersion7(),
                    ResourceType = "knowledge-source",
                    ResourceId = saved.SourceId.ToString(),
                    Action = "uploaded",
                    ToStatus = saved.Status,
                    UserId = ResolveUserId()!,
                    CreatedAt = DateTimeOffset.UtcNow
                },
                ct).ConfigureAwait(false);
            var indexed = await extractionService.ExtractAsync(
                saved.SourceId,
                ResolveUserId()!,
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
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await extractionService.ExtractAsync(
                sourceId,
                ResolveUserId()!,
                ct).ConfigureAwait(false));
        }
        catch (ProcessImprovementRuleException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }

    [HttpPost("{sourceId:guid}/records")]
    public async Task<IActionResult> SaveRecord(
        Guid sourceId,
        [FromBody] KnowledgeRecord request,
        CancellationToken ct)
        => await ExecuteWriteAsync(
            () => workflow.SaveKnowledgeRecordAsync(
                request with { SourceId = sourceId },
                ResolveUserId()!,
                ct)).ConfigureAwait(false);

    [HttpPost("{sourceId:guid}/status")]
    public async Task<IActionResult> ChangeStatus(
        Guid sourceId,
        [FromBody] StatusChangeRequest request,
        CancellationToken ct)
        => await ExecuteWriteAsync(
            () => workflow.ChangeKnowledgeSourceStatusAsync(
                sourceId,
                request.TargetStatus,
                ResolveUserId()!,
                ct)).ConfigureAwait(false);

    private async Task<IActionResult> ExecuteWriteAsync<T>(Func<Task<T>> operation)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await operation().ConfigureAwait(false));
        }
        catch (ProcessImprovementRuleException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}

[ApiController]
[Route("api/v1/parameter-recommendations")]
[NonController]
public sealed class ParameterRecommendationsController(
    IProcessImprovementStore store,
    ProcessImprovementWorkflow workflow,
    PlatformUserResolver userResolver) : PlatformConfigurationControllerBase(userResolver)
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => DeniedConfigurationRead() ??
           Ok(new { data = await store.ListRecommendationsAsync(ct).ConfigureAwait(false) });

    [HttpGet("{recommendationId:guid}")]
    public async Task<IActionResult> Get(Guid recommendationId, CancellationToken ct)
    {
        var denied = DeniedConfigurationRead();
        if (denied is not null)
            return denied;
        var recommendation = await store.GetRecommendationAsync(recommendationId, ct).ConfigureAwait(false);
        if (recommendation is null)
            return NotFound();
        var audit = await store.ListAuditEntriesAsync(
            "recommendation",
            recommendationId.ToString(),
            ct).ConfigureAwait(false);
        return Ok(new { recommendation, audit });
    }

    [HttpPost]
    public Task<IActionResult> Create([FromBody] ParameterRecommendation request, CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.CreateRecommendationAsync(request, ResolveUserId()!, ct));

    [HttpPost("{recommendationId:guid}/status")]
    public Task<IActionResult> ChangeStatus(
        Guid recommendationId,
        [FromBody] RecommendationStatusRequest request,
        CancellationToken ct)
        => ExecuteWriteAsync(
            () => workflow.ChangeRecommendationStatusAsync(
                recommendationId,
                request.TargetStatus,
                ResolveUserId()!,
                request.ExecutionReference,
                request.Verification,
                ct));

    private async Task<IActionResult> ExecuteWriteAsync<T>(Func<Task<T>> operation)
    {
        var denied = DeniedConfigurationWrite();
        if (denied is not null)
            return denied;
        try
        {
            return Ok(await operation().ConfigureAwait(false));
        }
        catch (ProcessImprovementRuleException exception)
        {
            return Conflict(new { error = exception.Message });
        }
    }
}

public sealed record StatusChangeRequest(string TargetStatus);
public sealed record ModelRollbackRequest(int CurrentVersion, int TargetVersion);
public sealed record RecommendationStatusRequest(
    string TargetStatus,
    string? ExecutionReference,
    RecommendationVerification? Verification);
