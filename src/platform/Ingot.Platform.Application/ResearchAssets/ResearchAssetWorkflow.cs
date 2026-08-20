using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

public sealed class ResearchAssetWorkflow(
    IResearchAssetStore store,
    IProcessConfigurationStore processConfiguration)
{
    public async Task<TrainingDatasetVersion> RegisterDatasetAsync(
        TrainingDatasetVersion request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ResearchAssetValidator.TryValidate(
                request with { CreatedBy = userId },
                out var value,
                out var error))
            throw new ResearchAssetRuleException(error!);
        if (await store.GetDatasetAsync(value!.DatasetId, value.Version, ct).ConfigureAwait(false) is not null)
            throw new ResearchAssetRuleException("训练数据版本已经存在；训练数据版本不可覆盖。");
        var plan = await processConfiguration.GetAnalysisPlanAsync(
            value.AnalysisPlanId,
            value.AnalysisPlanVersion,
            ct).ConfigureAwait(false);
        if (plan is null)
            throw new ResearchAssetRuleException("引用的分析方案版本不存在。");
        var dataModel = await processConfiguration.GetDataModelAsync(
            value.DataModelId,
            value.DataModelVersion,
            ct).ConfigureAwait(false);
        if (dataModel is null)
            throw new ResearchAssetRuleException("引用的工艺数据模型版本不存在。");
        if (plan.DataModelId != dataModel.ModelId || plan.DataModelVersion != dataModel.Version)
            throw new ResearchAssetRuleException("分析方案与训练数据引用的工艺数据模型版本不一致。");
        if (!IncludesContext(value.ContextSelector, plan.ContextSelector))
            throw new ResearchAssetRuleException("训练数据适用范围不能宽于分析方案适用范围。");
        var saved = await store.AddDatasetAsync(value, ct).ConfigureAwait(false);
        await AuditAsync("dataset", $"{saved.DatasetId}:{saved.Version}", "registered", null, null, userId, ct)
            .ConfigureAwait(false);
        return saved;
    }

    public async Task<ProcessModelVersion> SaveModelDraftAsync(
        ProcessModelVersion request,
        string userId,
        CancellationToken ct = default)
    {
        var draft = request with
        {
            Status = ProcessModelStatuses.Draft,
            CreatedBy = userId
        };
        if (!ResearchAssetValidator.TryValidate(draft, out var value, out var error))
            throw new ResearchAssetRuleException(error!);
        var dataset = await store.GetDatasetAsync(value!.DatasetId, value.DatasetVersion, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("引用的训练数据版本不存在。");
        if (!value.InputFeatureCodes.All(dataset.FeatureCodes.Contains))
            throw new ResearchAssetRuleException("模型输入特征必须全部来自引用的训练数据版本。");
        if (!string.Equals(value.OutputCode, dataset.TargetCode, StringComparison.Ordinal))
            throw new ResearchAssetRuleException("模型输出必须与训练数据版本的目标数据项一致。");
        if (!IncludesContext(value.ContextSelector, dataset.ContextSelector))
            throw new ResearchAssetRuleException("模型适用范围不能宽于训练数据适用范围。");
        var existing = await store.GetModelAsync(value.ModelId, value.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ProcessModelStatuses.Draft)
            throw new ResearchAssetRuleException("只有草稿模型版本可以修改；请创建新版本。");
        if (existing is not null)
        {
            value = value with
            {
                CreatedBy = existing.CreatedBy,
                CreatedAt = existing.CreatedAt
            };
        }
        var saved = await store.SaveModelAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(
            "model",
            $"{saved.ModelId}:{saved.Version}",
            existing is null ? "registered" : "draft-updated",
            existing?.Status,
            saved.Status,
            userId,
            ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ModelEvaluation> AddEvaluationAsync(
        ModelEvaluation request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ResearchAssetValidator.TryValidate(
                request with { EvaluatedBy = userId },
                out var value,
                out var error))
            throw new ResearchAssetRuleException(error!);
        var model = await store.GetModelAsync(value!.ModelId, value.ModelVersion, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("模型版本不存在。");
        if (model.Status is ProcessModelStatuses.Active or ProcessModelStatuses.Retired)
            throw new ResearchAssetRuleException("运行中或已停用的模型版本不能追加评估。");
        var saved = await store.AddEvaluationAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(
            "model",
            $"{model.ModelId}:{model.Version}",
            saved.Passed ? "evaluation-passed" : "evaluation-failed",
            model.Status,
            model.Status,
            userId,
            ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ProcessModelVersion> ChangeModelStatusAsync(
        string modelId,
        int version,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        modelId = NormalizeId(modelId);
        if (string.IsNullOrWhiteSpace(targetStatus))
            throw new ResearchAssetRuleException("目标模型状态不能为空。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        if (!ProcessModelStatuses.IsValid(targetStatus))
            throw new ResearchAssetRuleException("目标模型状态无效。");
        var model = await store.GetModelAsync(modelId, version, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("模型版本不存在。");
        if (model.Status == targetStatus)
            return model;
        var allowed = model.Status switch
        {
            ProcessModelStatuses.Draft => targetStatus is ProcessModelStatuses.Validated or ProcessModelStatuses.Retired,
            ProcessModelStatuses.Validated => targetStatus is ProcessModelStatuses.Active or ProcessModelStatuses.Retired,
            ProcessModelStatuses.Active => targetStatus is ProcessModelStatuses.Suspended or ProcessModelStatuses.Retired,
            ProcessModelStatuses.Suspended => targetStatus is ProcessModelStatuses.Validated or ProcessModelStatuses.Retired,
            _ => false
        };
        if (!allowed)
            throw new ResearchAssetRuleException($"不允许从 {model.Status} 转换到 {targetStatus}。");
        if (targetStatus is ProcessModelStatuses.Validated or ProcessModelStatuses.Active)
        {
            if (string.IsNullOrWhiteSpace(model.ArtifactRef) ||
                string.IsNullOrWhiteSpace(model.ArtifactSha256))
                throw new ResearchAssetRuleException("模型进入验证或运行状态前必须登记模型产物位置和 SHA-256。");
            await RequirePassingEvaluationAsync(modelId, version, ct).ConfigureAwait(false);
        }
        if (targetStatus == ProcessModelStatuses.Active)
        {
            var active = (await store.ListModelsAsync(ct).ConfigureAwait(false))
                .FirstOrDefault(item => item.ModelId == modelId &&
                                        item.Version != version &&
                                        item.Status == ProcessModelStatuses.Active);
            if (active is not null)
                throw new ResearchAssetRuleException(
                    $"模型 {modelId} 的版本 {active.Version} 正在运行；请先停用或使用回退操作。");
        }
        var updated = model with { Status = targetStatus, UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveModelAsync(updated, ct).ConfigureAwait(false);
        await AuditAsync(
            "model",
            $"{modelId}:{version}",
            "status-changed",
            model.Status,
            targetStatus,
            userId,
            ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<ModelDriftReading> RecordDriftAsync(
        ModelDriftReading request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ResearchAssetValidator.TryValidate(
                request with { RecordedBy = userId },
                out var value,
                out var error))
            throw new ResearchAssetRuleException(error!);
        var model = await store.GetModelAsync(value!.ModelId, value.ModelVersion, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("模型版本不存在。");
        if (model.Status is not (ProcessModelStatuses.Active or ProcessModelStatuses.Suspended))
            throw new ResearchAssetRuleException("只有运行中或已暂停的模型可以记录漂移。");
        var saved = await store.AddDriftReadingAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(
            "model",
            $"{model.ModelId}:{model.Version}",
            saved.Value >= saved.StopThreshold ? "drift-stop-threshold" :
            saved.Value >= saved.WarningThreshold ? "drift-warning" : "drift-normal",
            model.Status,
            model.Status,
            userId,
            ct).ConfigureAwait(false);
        if (model.Status == ProcessModelStatuses.Active && saved.Value >= saved.StopThreshold)
        {
            var suspended = model with
            {
                Status = ProcessModelStatuses.Suspended,
                UpdatedAt = DateTimeOffset.UtcNow,
                ChangeNote = $"漂移指标 {saved.MetricCode} 达到停用门槛。"
            };
            await store.SaveModelAsync(suspended, ct).ConfigureAwait(false);
            await AuditAsync(
                "model",
                $"{model.ModelId}:{model.Version}",
                "auto-suspended",
                model.Status,
                suspended.Status,
                userId,
                ct).ConfigureAwait(false);
        }
        return saved;
    }

    public async Task<ProcessModelVersion> RollbackModelAsync(
        string modelId,
        int currentVersion,
        int targetVersion,
        string userId,
        CancellationToken ct = default)
    {
        modelId = NormalizeId(modelId);
        if (currentVersion == targetVersion)
            throw new ResearchAssetRuleException("回退目标必须是另一个模型版本。");
        var current = await store.GetModelAsync(modelId, currentVersion, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("当前模型版本不存在。");
        var target = await store.GetModelAsync(modelId, targetVersion, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("回退目标模型版本不存在。");
        if (current.Status is not (ProcessModelStatuses.Active or ProcessModelStatuses.Suspended))
            throw new ResearchAssetRuleException("只有运行中或已暂停的模型可以发起回退。");
        if (target.Status is not (ProcessModelStatuses.Validated or ProcessModelStatuses.Suspended))
            throw new ResearchAssetRuleException("回退目标必须是已验证或已暂停的模型版本。");
        await RequirePassingEvaluationAsync(modelId, targetVersion, ct).ConfigureAwait(false);
        var retired = current with
        {
            Status = ProcessModelStatuses.Retired,
            UpdatedAt = DateTimeOffset.UtcNow,
            ChangeNote = $"已回退到版本 {targetVersion}。"
        };
        await store.SaveModelAsync(retired, ct).ConfigureAwait(false);
        var activated = target with
        {
            Status = ProcessModelStatuses.Active,
            UpdatedAt = DateTimeOffset.UtcNow,
            ChangeNote = $"从版本 {currentVersion} 回退启用。"
        };
        await store.SaveModelAsync(activated, ct).ConfigureAwait(false);
        await AuditAsync(
            "model",
            $"{modelId}:{currentVersion}",
            "rolled-back-from",
            current.Status,
            retired.Status,
            userId,
            ct,
            new Dictionary<string, string> { ["targetVersion"] = targetVersion.ToString() }).ConfigureAwait(false);
        await AuditAsync(
            "model",
            $"{modelId}:{targetVersion}",
            "rolled-back-to",
            target.Status,
            activated.Status,
            userId,
            ct,
            new Dictionary<string, string> { ["currentVersion"] = currentVersion.ToString() }).ConfigureAwait(false);
        return activated;
    }

    public async Task<KnowledgeSource> ChangeKnowledgeSourceStatusAsync(
        Guid sourceId,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetStatus))
            throw new ResearchAssetRuleException("知识来源目标状态不能为空。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        if (!KnowledgeSourceStatuses.IsValid(targetStatus))
            throw new ResearchAssetRuleException("知识来源目标状态无效。");
        var source = await store.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("知识来源不存在。");
        var allowed = source.Status switch
        {
            KnowledgeSourceStatuses.Uploaded => targetStatus is KnowledgeSourceStatuses.Indexed or KnowledgeSourceStatuses.Retired,
            KnowledgeSourceStatuses.Indexed => targetStatus is KnowledgeSourceStatuses.Reviewed or KnowledgeSourceStatuses.Retired,
            KnowledgeSourceStatuses.Reviewed => targetStatus == KnowledgeSourceStatuses.Retired,
            _ => false
        };
        if (source.Status != targetStatus && !allowed)
            throw new ResearchAssetRuleException($"不允许从 {source.Status} 转换到 {targetStatus}。");
        if (source.Status == targetStatus)
            return source;
        if (targetStatus == KnowledgeSourceStatuses.Reviewed)
        {
            var records = await store.ListKnowledgeRecordsAsync(sourceId, ct).ConfigureAwait(false);
            if (records.Count == 0 || records.Any(record => !record.HumanReviewed))
                throw new ResearchAssetRuleException("知识来源至少需要一条且全部知识记录完成人工复核。");
        }
        var now = DateTimeOffset.UtcNow;
        var updated = source with
        {
            Status = targetStatus,
            ReviewedBy = targetStatus == KnowledgeSourceStatuses.Reviewed ? userId : source.ReviewedBy,
            ReviewedAt = targetStatus == KnowledgeSourceStatuses.Reviewed ? now : source.ReviewedAt
        };
        await store.SaveKnowledgeSourceMetadataAsync(updated, ct).ConfigureAwait(false);
        await AuditAsync(
            "knowledge-source",
            sourceId.ToString(),
            "status-changed",
            source.Status,
            targetStatus,
            userId,
            ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<KnowledgeRecord> SaveKnowledgeRecordAsync(
        KnowledgeRecord request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ResearchAssetValidator.TryValidate(
                request with
                {
                    CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? userId : request.CreatedBy,
                    ReviewedBy = request.HumanReviewed ? userId : null,
                    ReviewedAt = request.HumanReviewed ? DateTimeOffset.UtcNow : null
                },
                out var value,
                out var error))
            throw new ResearchAssetRuleException(error!);
        var source = await store.GetKnowledgeSourceAsync(value!.SourceId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("知识来源不存在。");
        if (source.Status == KnowledgeSourceStatuses.Retired)
            throw new ResearchAssetRuleException("已停用的知识来源不能新增或修改知识记录。");
        var saved = await store.SaveKnowledgeRecordAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(
            "knowledge-source",
            source.SourceId.ToString(),
            saved.HumanReviewed ? "record-reviewed" : "record-saved",
            source.Status,
            source.Status,
            userId,
            ct,
            new Dictionary<string, string> { ["recordId"] = saved.RecordId.ToString() }).ConfigureAwait(false);
        return saved;
    }

    private static bool IncludesContext(
        IReadOnlyDictionary<string, string> candidate,
        IReadOnlyDictionary<string, string> required)
        => required.All(pair =>
            candidate.TryGetValue(pair.Key, out var value) &&
            string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));

    private async Task RequirePassingEvaluationAsync(
        string modelId,
        int version,
        CancellationToken ct)
    {
        var evaluations = await store.ListEvaluationsAsync(modelId, version, ct).ConfigureAwait(false);
        if (!evaluations.Any(evaluation => evaluation.Passed))
            throw new ResearchAssetRuleException("模型版本没有通过门槛的评估记录。");
    }

    private Task AuditAsync(
        string resourceType,
        string resourceId,
        string action,
        string? fromStatus,
        string? toStatus,
        string userId,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? details = null)
        => store.AddAuditEntryAsync(
            new ResearchAssetAuditEntry
            {
                EntryId = Guid.CreateVersion7(),
                ResourceType = resourceType,
                ResourceId = resourceId,
                Action = action,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                UserId = userId,
                Details = details ?? new Dictionary<string, string>(),
                CreatedAt = DateTimeOffset.UtcNow
            },
            ct);

    private static string NormalizeId(string value)
        => value.Trim().ToLowerInvariant();
}

public sealed class ResearchAssetRuleException(string message) : InvalidOperationException(message);
