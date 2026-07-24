using Ingot.Contracts.ProcessImprovement;
using Ingot.Platform.Infrastructure.ProcessConfiguration;

namespace Ingot.Platform.Infrastructure.ProcessImprovement;

public sealed class ProcessImprovementWorkflow(
    IProcessImprovementStore store,
    IProcessConfigurationStore processConfiguration,
    ScientificTrialResultCalculator? trialResultCalculator = null)
{
    public async Task<TrainingDatasetVersion> RegisterDatasetAsync(
        TrainingDatasetVersion request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ProcessImprovementValidator.TryValidate(
                request with { CreatedBy = userId },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        if (await store.GetDatasetAsync(value!.DatasetId, value.Version, ct).ConfigureAwait(false) is not null)
            throw new ProcessImprovementRuleException("训练数据版本已经存在；训练数据版本不可覆盖。");
        var plan = await processConfiguration.GetAnalysisPlanAsync(
            value.AnalysisPlanId,
            value.AnalysisPlanVersion,
            ct).ConfigureAwait(false);
        if (plan is null)
            throw new ProcessImprovementRuleException("引用的分析方案版本不存在。");
        var dataModel = await processConfiguration.GetDataModelAsync(
            value.DataModelId,
            value.DataModelVersion,
            ct).ConfigureAwait(false);
        if (dataModel is null)
            throw new ProcessImprovementRuleException("引用的工艺数据模型版本不存在。");
        if (plan.DataModelId != dataModel.ModelId || plan.DataModelVersion != dataModel.Version)
            throw new ProcessImprovementRuleException("分析方案与训练数据引用的工艺数据模型版本不一致。");
        if (!IncludesContext(value.ContextSelector, plan.ContextSelector))
            throw new ProcessImprovementRuleException("训练数据适用范围不能宽于分析方案适用范围。");
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
        if (!ProcessImprovementValidator.TryValidate(draft, out var value, out var error))
            throw new ProcessImprovementRuleException(error!);
        var dataset = await store.GetDatasetAsync(value!.DatasetId, value.DatasetVersion, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("引用的训练数据版本不存在。");
        if (!value.InputFeatureCodes.All(dataset.FeatureCodes.Contains))
            throw new ProcessImprovementRuleException("模型输入特征必须全部来自引用的训练数据版本。");
        if (!string.Equals(value.OutputCode, dataset.TargetCode, StringComparison.Ordinal))
            throw new ProcessImprovementRuleException("模型输出必须与训练数据版本的目标数据项一致。");
        if (!IncludesContext(value.ContextSelector, dataset.ContextSelector))
            throw new ProcessImprovementRuleException("模型适用范围不能宽于训练数据适用范围。");
        var existing = await store.GetModelAsync(value.ModelId, value.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != ProcessModelStatuses.Draft)
            throw new ProcessImprovementRuleException("只有草稿模型版本可以修改；请创建新版本。");
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
        if (!ProcessImprovementValidator.TryValidate(
                request with { EvaluatedBy = userId },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        var model = await store.GetModelAsync(value!.ModelId, value.ModelVersion, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("模型版本不存在。");
        if (model.Status is ProcessModelStatuses.Active or ProcessModelStatuses.Retired)
            throw new ProcessImprovementRuleException("运行中或已停用的模型版本不能追加评估。");
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
            throw new ProcessImprovementRuleException("目标模型状态不能为空。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        if (!ProcessModelStatuses.IsValid(targetStatus))
            throw new ProcessImprovementRuleException("目标模型状态无效。");
        var model = await store.GetModelAsync(modelId, version, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("模型版本不存在。");
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
            throw new ProcessImprovementRuleException($"不允许从 {model.Status} 转换到 {targetStatus}。");
        if (targetStatus is ProcessModelStatuses.Validated or ProcessModelStatuses.Active)
        {
            if (string.IsNullOrWhiteSpace(model.ArtifactRef) ||
                string.IsNullOrWhiteSpace(model.ArtifactSha256))
                throw new ProcessImprovementRuleException("模型进入验证或运行状态前必须登记模型产物位置和 SHA-256。");
            await RequirePassingEvaluationAsync(modelId, version, ct).ConfigureAwait(false);
        }
        if (targetStatus == ProcessModelStatuses.Active)
        {
            var active = (await store.ListModelsAsync(ct).ConfigureAwait(false))
                .FirstOrDefault(item => item.ModelId == modelId &&
                                        item.Version != version &&
                                        item.Status == ProcessModelStatuses.Active);
            if (active is not null)
                throw new ProcessImprovementRuleException(
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
        if (!ProcessImprovementValidator.TryValidate(
                request with { RecordedBy = userId },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        var model = await store.GetModelAsync(value!.ModelId, value.ModelVersion, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("模型版本不存在。");
        if (model.Status is not (ProcessModelStatuses.Active or ProcessModelStatuses.Suspended))
            throw new ProcessImprovementRuleException("只有运行中或已暂停的模型可以记录漂移。");
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
            throw new ProcessImprovementRuleException("回退目标必须是另一个模型版本。");
        var current = await store.GetModelAsync(modelId, currentVersion, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("当前模型版本不存在。");
        var target = await store.GetModelAsync(modelId, targetVersion, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("回退目标模型版本不存在。");
        if (current.Status is not (ProcessModelStatuses.Active or ProcessModelStatuses.Suspended))
            throw new ProcessImprovementRuleException("只有运行中或已暂停的模型可以发起回退。");
        if (target.Status is not (ProcessModelStatuses.Validated or ProcessModelStatuses.Suspended))
            throw new ProcessImprovementRuleException("回退目标必须是已验证或已暂停的模型版本。");
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

    public async Task<InvestigationCase> CreateInvestigationAsync(
        InvestigationCase request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ProcessImprovementValidator.TryValidate(
                request with { OwnerUserId = userId, Status = InvestigationStatuses.Open },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        if (await store.GetInvestigationAsync(value!.InvestigationId, ct).ConfigureAwait(false) is not null)
            throw new ProcessImprovementRuleException("调查记录已经存在。");
        var saved = await store.SaveInvestigationAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(
            "investigation",
            saved.InvestigationId.ToString(),
            "created",
            null,
            saved.Status,
            userId,
            ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<PossibleCause> AddCauseAsync(
        PossibleCause request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ProcessImprovementValidator.TryValidate(
                request with { CreatedBy = userId, Status = PossibleCauseStatuses.Proposed },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        var investigation = await store.GetInvestigationAsync(value!.InvestigationId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调查记录不存在。");
        if (investigation.Status is InvestigationStatuses.Closed or InvestigationStatuses.Concluded)
            throw new ProcessImprovementRuleException("已形成结论或已关闭的调查不能新增可能原因。");
        if (await store.GetCauseAsync(value.CauseId, ct).ConfigureAwait(false) is not null)
            throw new ProcessImprovementRuleException("可能原因已经存在。");
        var saved = await store.SaveCauseAsync(value, ct).ConfigureAwait(false);
        if (investigation.Status == InvestigationStatuses.Open)
            await store.SaveInvestigationAsync(
                investigation with
                {
                    Status = InvestigationStatuses.Investigating,
                    UpdatedAt = DateTimeOffset.UtcNow
                },
                ct).ConfigureAwait(false);
        await AuditAsync(
            "investigation",
            investigation.InvestigationId.ToString(),
            "cause-added",
            investigation.Status,
            InvestigationStatuses.Investigating,
            userId,
            ct,
            new Dictionary<string, string> { ["causeId"] = saved.CauseId.ToString() }).ConfigureAwait(false);
        return saved;
    }

    public async Task<ProcessTrial> CreateTrialAsync(
        ProcessTrial request,
        string userId,
        CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var prepared = request with
        {
            CreatedBy = userId,
            Status = ProcessTrialStatuses.Planned,
            Protocol = request.Protocol is null
                ? null
                : request.Protocol with
                {
                    PreRegisteredBy = userId,
                    PreRegisteredAt = now
                }
        };
        if (!ProcessImprovementValidator.TryValidate(
                prepared,
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        var investigation = await store.GetInvestigationAsync(value!.InvestigationId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调查记录不存在。");
        var cause = await store.GetCauseAsync(value.CauseId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("可能原因不存在。");
        if (cause.InvestigationId != investigation.InvestigationId)
            throw new ProcessImprovementRuleException("可能原因不属于当前调查。");
        if (investigation.Status is InvestigationStatuses.Concluded or InvestigationStatuses.Closed)
            throw new ProcessImprovementRuleException("已形成结论或已关闭的调查不能创建试验。");
        var saved = await store.SaveTrialAsync(value, ct).ConfigureAwait(false);
        await store.SaveCauseAsync(
            cause with { Status = PossibleCauseStatuses.Selected, UpdatedAt = DateTimeOffset.UtcNow },
            ct).ConfigureAwait(false);
        await store.SaveInvestigationAsync(
            investigation with { Status = InvestigationStatuses.Trialing, UpdatedAt = DateTimeOffset.UtcNow },
            ct).ConfigureAwait(false);
        await AuditAsync(
            "trial",
            saved.TrialId.ToString(),
            "planned",
            null,
            saved.Status,
            userId,
            ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ProcessTrial> ChangeTrialStatusAsync(
        Guid trialId,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetStatus))
            throw new ProcessImprovementRuleException("目标试验状态不能为空。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        var trial = await store.GetTrialAsync(trialId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调整试验不存在。");
        if (trial.Status == targetStatus)
            return trial;
        var allowed = trial.Status switch
        {
            ProcessTrialStatuses.Planned => targetStatus is ProcessTrialStatuses.Approved or ProcessTrialStatuses.Cancelled,
            ProcessTrialStatuses.Approved => targetStatus is ProcessTrialStatuses.Running or ProcessTrialStatuses.Cancelled,
            ProcessTrialStatuses.Running => targetStatus is ProcessTrialStatuses.Completed or ProcessTrialStatuses.Cancelled,
            _ => false
        };
        if (!allowed)
            throw new ProcessImprovementRuleException($"不允许从 {trial.Status} 转换到 {targetStatus}。");
        if (targetStatus == ProcessTrialStatuses.Approved && trial.CreatedBy == userId)
            throw new ProcessImprovementRuleException("试验创建人不能同时批准该试验。");
        if (targetStatus == ProcessTrialStatuses.Approved &&
            trial.RigorLevel == TrialRigorLevels.Confirmatory)
        {
            var protocol = trial.Protocol
                ?? throw new ProcessImprovementRuleException("验证性试验缺少预注册实验协议。");
            if (trial.ControlCycleIds.Count < protocol.MinimumControlSampleSize ||
                trial.TrialCycleIds.Count < protocol.MinimumTrialSampleSize)
            {
                throw new ProcessImprovementRuleException("验证性试验的预分配周期数低于协议计划样本量。");
            }
            if (trial.ControlCycleIds.Intersect(trial.TrialCycleIds, StringComparer.Ordinal).Any())
                throw new ProcessImprovementRuleException("验证性试验的基准组与试验组不能包含同一周期。");
        }
        if (targetStatus == ProcessTrialStatuses.Completed)
        {
            var results = await store.ListTrialResultsAsync(trialId, ct).ConfigureAwait(false);
            if (results.Count == 0)
                throw new ProcessImprovementRuleException("完成试验前必须记录至少一个结果。");
            if (results.Any(result => !result.SafetyPassed))
                throw new ProcessImprovementRuleException("存在未通过安全检查的试验结果，不能标记为完成。");
            if (trial.RigorLevel == TrialRigorLevels.Confirmatory &&
                results.Any(result => !result.CalculatedFromSource ||
                                      string.IsNullOrWhiteSpace(result.EvidenceHash)))
            {
                throw new ProcessImprovementRuleException("验证性试验只能使用由版本化源数据计算并带证据哈希的结果。");
            }
        }
        var now = DateTimeOffset.UtcNow;
        var updated = trial with
        {
            Status = targetStatus,
            ApprovedBy = targetStatus == ProcessTrialStatuses.Approved ? userId : trial.ApprovedBy,
            ApprovedAt = targetStatus == ProcessTrialStatuses.Approved ? now : trial.ApprovedAt,
            UpdatedAt = now
        };
        await store.SaveTrialAsync(updated, ct).ConfigureAwait(false);
        await AuditAsync(
            "trial",
            trialId.ToString(),
            "status-changed",
            trial.Status,
            targetStatus,
            userId,
            ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<TrialResult> AddTrialResultAsync(
        TrialResult request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ProcessImprovementValidator.TryValidate(
                request with { RecordedBy = userId },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        var trial = await store.GetTrialAsync(value!.TrialId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调整试验不存在。");
        if (trial.Status != ProcessTrialStatuses.Running)
            throw new ProcessImprovementRuleException("只有运行中的试验可以记录结果。");
        if (trial.RigorLevel == TrialRigorLevels.Confirmatory)
            throw new ProcessImprovementRuleException("验证性试验结果必须由系统从版本化周期特征计算。");
        var saved = await store.AddTrialResultAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(
            "trial",
            trial.TrialId.ToString(),
            "result-recorded",
            trial.Status,
            trial.Status,
            userId,
            ct,
            new Dictionary<string, string> { ["resultId"] = saved.ResultId.ToString() }).ConfigureAwait(false);
        return saved;
    }

    public async Task<TrialResult> CalculateTrialResultAsync(
        Guid trialId,
        string userId,
        CancellationToken ct = default)
    {
        var trial = await store.GetTrialAsync(trialId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调整试验不存在。");
        if (trial.Status != ProcessTrialStatuses.Running)
            throw new ProcessImprovementRuleException("只有运行中的试验可以计算结果。");
        if (trialResultCalculator is null)
            throw new ProcessImprovementRuleException("科研试验计算服务未配置。");
        var calculated = await trialResultCalculator.CalculateAsync(trial, userId, ct).ConfigureAwait(false);
        if (!ProcessImprovementValidator.TryValidate(calculated, out var normalized, out var error))
            throw new ProcessImprovementRuleException(error!);
        var saved = await store.AddTrialResultAsync(normalized!, ct).ConfigureAwait(false);
        await AuditAsync(
            "trial",
            trial.TrialId.ToString(),
            "result-calculated",
            trial.Status,
            trial.Status,
            userId,
            ct,
            new Dictionary<string, string>
            {
                ["resultId"] = saved.ResultId.ToString(),
                ["evidenceHash"] = saved.EvidenceHash!
            }).ConfigureAwait(false);
        return saved;
    }

    public async Task<InvestigationConclusion> AddConclusionAsync(
        InvestigationConclusion request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ProcessImprovementValidator.TryValidate(
                request with { ReviewedBy = userId },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        var investigation = await store.GetInvestigationAsync(value!.InvestigationId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调查记录不存在。");
        var cause = await store.GetCauseAsync(value.CauseId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("可能原因不存在。");
        var trial = await store.GetTrialAsync(value.TrialId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调整试验不存在。");
        if (trial.Status != ProcessTrialStatuses.Completed)
            throw new ProcessImprovementRuleException("只有已完成的试验才能形成调查结论。");
        if (trial.InvestigationId != investigation.InvestigationId ||
            trial.CauseId != cause.CauseId ||
            cause.InvestigationId != investigation.InvestigationId)
            throw new ProcessImprovementRuleException("调查、可能原因和试验的关联不一致。");
        var results = await store.ListTrialResultsAsync(trial.TrialId, ct).ConfigureAwait(false);
        if (!value.ResultIds.All(id => results.Any(result => result.ResultId == id)))
            throw new ProcessImprovementRuleException("调查结论引用了不属于该试验的结果。");
        if (!IncludesContext(value.ApplicableContext, investigation.ContextSelector))
            throw new ProcessImprovementRuleException("调查结论适用范围不能宽于调查记录适用范围。");
        var saved = await store.AddConclusionAsync(value, ct).ConfigureAwait(false);
        await store.SaveCauseAsync(
            cause with { Status = value.Decision, UpdatedAt = DateTimeOffset.UtcNow },
            ct).ConfigureAwait(false);
        await store.SaveInvestigationAsync(
            investigation with { Status = InvestigationStatuses.Concluded, UpdatedAt = DateTimeOffset.UtcNow },
            ct).ConfigureAwait(false);
        await AuditAsync(
            "investigation",
            investigation.InvestigationId.ToString(),
            "concluded",
            investigation.Status,
            InvestigationStatuses.Concluded,
            userId,
            ct,
            new Dictionary<string, string> { ["conclusionId"] = saved.ConclusionId.ToString() })
            .ConfigureAwait(false);
        return saved;
    }

    public async Task<KnowledgeSource> ChangeKnowledgeSourceStatusAsync(
        Guid sourceId,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetStatus))
            throw new ProcessImprovementRuleException("知识来源目标状态不能为空。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        if (!KnowledgeSourceStatuses.IsValid(targetStatus))
            throw new ProcessImprovementRuleException("知识来源目标状态无效。");
        var source = await store.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("知识来源不存在。");
        var allowed = source.Status switch
        {
            KnowledgeSourceStatuses.Uploaded => targetStatus is KnowledgeSourceStatuses.Indexed or KnowledgeSourceStatuses.Retired,
            KnowledgeSourceStatuses.Indexed => targetStatus is KnowledgeSourceStatuses.Reviewed or KnowledgeSourceStatuses.Retired,
            KnowledgeSourceStatuses.Reviewed => targetStatus == KnowledgeSourceStatuses.Retired,
            _ => false
        };
        if (source.Status != targetStatus && !allowed)
            throw new ProcessImprovementRuleException($"不允许从 {source.Status} 转换到 {targetStatus}。");
        if (source.Status == targetStatus)
            return source;
        if (targetStatus == KnowledgeSourceStatuses.Reviewed)
        {
            var records = await store.ListKnowledgeRecordsAsync(sourceId, ct).ConfigureAwait(false);
            if (records.Count == 0 || records.Any(record => !record.HumanReviewed))
                throw new ProcessImprovementRuleException("知识来源至少需要一条且全部知识记录完成人工复核。");
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
        if (!ProcessImprovementValidator.TryValidate(
                request with
                {
                    CreatedBy = string.IsNullOrWhiteSpace(request.CreatedBy) ? userId : request.CreatedBy,
                    ReviewedBy = request.HumanReviewed ? userId : null,
                    ReviewedAt = request.HumanReviewed ? DateTimeOffset.UtcNow : null
                },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        var source = await store.GetKnowledgeSourceAsync(value!.SourceId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("知识来源不存在。");
        if (source.Status == KnowledgeSourceStatuses.Retired)
            throw new ProcessImprovementRuleException("已停用的知识来源不能新增或修改知识记录。");
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

    public async Task<ParameterRecommendation> CreateRecommendationAsync(
        ParameterRecommendation request,
        string userId,
        CancellationToken ct = default)
    {
        if (!ProcessImprovementValidator.TryValidate(
                request with { CreatedBy = userId, Status = RecommendationStatuses.Draft },
                out var value,
                out var error))
            throw new ProcessImprovementRuleException(error!);
        var conclusion = await store.GetConclusionAsync(value!.ConclusionId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调查结论不存在。");
        if (conclusion.InvestigationId != value.InvestigationId)
            throw new ProcessImprovementRuleException("参数建议与调查结论的关联不一致。");
        if (conclusion.Decision != PossibleCauseStatuses.Confirmed)
            throw new ProcessImprovementRuleException("只有已确认的调查结论才能形成参数建议。");
        if (!IncludesContext(value.ApplicableContext, conclusion.ApplicableContext))
            throw new ProcessImprovementRuleException("参数建议适用范围不能宽于调查结论适用范围。");
        var trial = await store.GetTrialAsync(conclusion.TrialId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("调查结论关联的调整试验不存在。");
        var trialResults = await store.ListTrialResultsAsync(trial.TrialId, ct).ConfigureAwait(false);
        foreach (var setting in value.ParameterSettings)
        {
            var change = trial.ParameterChanges.FirstOrDefault(item =>
                item.ParameterCode == setting.ParameterCode &&
                string.Equals(item.PhaseCode, setting.PhaseCode, StringComparison.Ordinal));
            if (change is null)
                throw new ProcessImprovementRuleException(
                    $"参数建议包含未在受控试验中调整的参数：{setting.ParameterCode}。");
            if (Math.Abs(setting.RecommendedValue - change.TrialValue) > 1e-9)
                throw new ProcessImprovementRuleException(
                    $"参数 {setting.ParameterCode} 的建议值必须等于已完成试验值；其他值需要新的受控试验。");
            if (setting.AllowedMinimum < change.AllowedMinimum ||
                setting.AllowedMaximum > change.AllowedMaximum)
                throw new ProcessImprovementRuleException(
                    $"参数 {setting.ParameterCode} 的建议允许范围不能超出试验允许范围。");
        }
        foreach (var expected in value.ExpectedOutcomes)
        {
            var result = trialResults.FirstOrDefault(item => item.MetricCode == expected.MetricCode);
            if (result is null)
                throw new ProcessImprovementRuleException(
                    $"预期结果 {expected.MetricCode} 没有对应的受控试验结果。");
            if (Math.Abs(expected.BaselineValue - result.BaselineValue) > 1e-9 ||
                Math.Abs(expected.ExpectedValue - result.TrialValue) > 1e-9 ||
                !string.Equals(expected.Unit, result.Unit, StringComparison.OrdinalIgnoreCase))
                throw new ProcessImprovementRuleException(
                    $"预期结果 {expected.MetricCode} 必须与已完成试验的基准值、试验值和单位一致。");
        }
        foreach (var trialConstraint in trial.SafetyConstraints)
        {
            var recommendationConstraint = value.Constraints.FirstOrDefault(item =>
                item.Code == trialConstraint.Code &&
                item.Operator == trialConstraint.Operator &&
                string.Equals(item.Unit, trialConstraint.Unit, StringComparison.OrdinalIgnoreCase));
            if (recommendationConstraint is null ||
                !IsAtLeastAsStrict(recommendationConstraint, trialConstraint))
                throw new ProcessImprovementRuleException(
                    $"参数建议缺少与受控试验同等或更严格的约束：{trialConstraint.Code}。");
        }
        if (value.ModelId is not null)
        {
            if (value.ModelVersion is null)
                throw new ProcessImprovementRuleException("引用模型时必须提供模型版本。");
            var model = await store.GetModelAsync(value.ModelId, value.ModelVersion.Value, ct).ConfigureAwait(false)
                ?? throw new ProcessImprovementRuleException("引用的模型版本不存在。");
            if (model.Status != ProcessModelStatuses.Active)
                throw new ProcessImprovementRuleException("参数建议只能引用运行中的模型版本。");
            if (!IncludesContext(value.ApplicableContext, model.ContextSelector))
                throw new ProcessImprovementRuleException("参数建议适用范围不能宽于引用模型适用范围。");
        }
        if (await store.GetRecommendationAsync(value.RecommendationId, ct).ConfigureAwait(false) is not null)
            throw new ProcessImprovementRuleException("参数建议已经存在。");
        var saved = await store.SaveRecommendationAsync(value, ct).ConfigureAwait(false);
        await AuditAsync(
            "recommendation",
            saved.RecommendationId.ToString(),
            "created",
            null,
            saved.Status,
            userId,
            ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<ParameterRecommendation> ChangeRecommendationStatusAsync(
        Guid recommendationId,
        string targetStatus,
        string userId,
        string? executionReference = null,
        RecommendationVerification? verification = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(targetStatus))
            throw new ProcessImprovementRuleException("参数建议目标状态不能为空。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        var recommendation = await store.GetRecommendationAsync(recommendationId, ct).ConfigureAwait(false)
            ?? throw new ProcessImprovementRuleException("参数建议不存在。");
        if (recommendation.Status == targetStatus)
            return recommendation;
        var allowed = recommendation.Status switch
        {
            RecommendationStatuses.Draft => targetStatus is RecommendationStatuses.Reviewed or RecommendationStatuses.Withdrawn,
            RecommendationStatuses.Reviewed => targetStatus is RecommendationStatuses.Approved or RecommendationStatuses.Rejected,
            RecommendationStatuses.Approved => targetStatus is RecommendationStatuses.Executed or RecommendationStatuses.Withdrawn,
            RecommendationStatuses.Executed => targetStatus == RecommendationStatuses.Verified,
            RecommendationStatuses.RollbackRequired => targetStatus == RecommendationStatuses.RolledBack,
            _ => false
        };
        if (!allowed)
            throw new ProcessImprovementRuleException(
                $"不允许从 {recommendation.Status} 转换到 {targetStatus}。");
        var now = DateTimeOffset.UtcNow;
        var updated = recommendation with { Status = targetStatus, UpdatedAt = now };
        if (targetStatus == RecommendationStatuses.Reviewed)
        {
            if (recommendation.CreatedBy == userId)
                throw new ProcessImprovementRuleException("参数建议创建人不能同时完成复核。");
            updated = updated with { ReviewedBy = userId, ReviewedAt = now };
        }
        else if (targetStatus == RecommendationStatuses.Approved)
        {
            if (recommendation.CreatedBy == userId || recommendation.ReviewedBy == userId)
                throw new ProcessImprovementRuleException("参数建议批准人必须独立于创建人和复核人。");
            updated = updated with { ApprovedBy = userId, ApprovedAt = now };
        }
        else if (targetStatus == RecommendationStatuses.Executed)
        {
            if (string.IsNullOrWhiteSpace(executionReference))
                throw new ProcessImprovementRuleException("记录执行时必须提供外部执行编号。");
            updated = updated with
            {
                ExecutionReference = executionReference.Trim(),
                ExecutedAt = now
            };
        }
        else if (targetStatus == RecommendationStatuses.Verified)
        {
            if (verification is null || verification.Outcomes.Count == 0)
                throw new ProcessImprovementRuleException("完成效果确认前必须记录实际结果。");
            if (verification.RealizedValue is null)
                throw new ProcessImprovementRuleException("完成效果确认前必须记录实际经济价值。");
            var normalizedOutcomes = NormalizeRecommendationOutcomes(
                verification.Outcomes,
                recommendation.ExpectedOutcomes);
            var realizedValue = NormalizeRealizedValue(
                verification.RealizedValue,
                recommendation.ValueEstimate!);
            var safetyPassed = normalizedOutcomes.All(static outcome => outcome.SafetyPassed);
            var objectivesMet = recommendation.ExpectedOutcomes.All(expected =>
            {
                var actual = normalizedOutcomes.Single(outcome => outcome.MetricCode == expected.MetricCode);
                return ReachesExpectedOutcome(expected, actual.ActualValue);
            });
            var finalStatus = safetyPassed && objectivesMet
                ? RecommendationStatuses.Verified
                : RecommendationStatuses.RollbackRequired;
            updated = updated with
            {
                Status = finalStatus,
                Verification = verification with
                {
                    Outcomes = normalizedOutcomes,
                    RealizedValue = realizedValue,
                    ObjectivesMet = objectivesMet,
                    SafetyPassed = safetyPassed,
                    VerifiedBy = userId,
                    VerifiedAt = now
                }
            };
        }
        else if (targetStatus == RecommendationStatuses.RolledBack)
        {
            if (string.IsNullOrWhiteSpace(executionReference))
                throw new ProcessImprovementRuleException("记录回退时必须提供外部回退执行编号。");
            updated = updated with
            {
                RollbackExecutionReference = executionReference.Trim(),
                RolledBackAt = now
            };
        }
        await store.SaveRecommendationAsync(updated, ct).ConfigureAwait(false);
        await AuditAsync(
            "recommendation",
            recommendationId.ToString(),
            "status-changed",
            recommendation.Status,
            updated.Status,
            userId,
            ct,
            string.IsNullOrWhiteSpace(executionReference)
                ? null
                : new Dictionary<string, string> { ["executionReference"] = executionReference.Trim() })
            .ConfigureAwait(false);
        return updated;
    }

    private static IReadOnlyList<RecommendationOutcome> NormalizeRecommendationOutcomes(
        IReadOnlyList<RecommendationOutcome> outcomes,
        IReadOnlyList<ExpectedOutcome> expectedOutcomes)
    {
        var expectedCodes = expectedOutcomes
            .Select(static item => item.MetricCode)
            .ToHashSet(StringComparer.Ordinal);
        var normalized = new List<RecommendationOutcome>();
        foreach (var outcome in outcomes)
        {
            var code = outcome.MetricCode?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(code) || !expectedCodes.Contains(code))
                throw new ProcessImprovementRuleException($"实际结果指标不在参数建议预期结果中：{outcome.MetricCode}。");
            if (normalized.Any(item => item.MetricCode == code))
                throw new ProcessImprovementRuleException($"实际结果指标重复：{code}。");
            if (!double.IsFinite(outcome.BaselineValue) ||
                !double.IsFinite(outcome.ActualValue) ||
                !double.IsFinite(outcome.EffectValue) ||
                Math.Abs(outcome.EffectValue - (outcome.ActualValue - outcome.BaselineValue)) > 1e-9)
                throw new ProcessImprovementRuleException($"实际结果 {code} 的数值或变化量无效。");
            if (outcome.BaselineSampleCount <= 0 || outcome.ActualSampleCount <= 0)
                throw new ProcessImprovementRuleException($"实际结果 {code} 的样本数必须大于 0。");
            if (string.IsNullOrWhiteSpace(outcome.Unit) || outcome.Unit.Length > 40)
                throw new ProcessImprovementRuleException($"实际结果 {code} 的单位无效。");
            normalized.Add(outcome with
            {
                OutcomeId = outcome.OutcomeId == Guid.Empty ? Guid.CreateVersion7() : outcome.OutcomeId,
                MetricCode = code,
                Unit = outcome.Unit.Trim()
            });
        }
        var missing = expectedCodes.FirstOrDefault(code => normalized.All(item => item.MetricCode != code));
        if (missing is not null)
            throw new ProcessImprovementRuleException($"缺少预期指标 {missing} 的实际结果。");
        return normalized;
    }

    private static bool ReachesExpectedOutcome(ExpectedOutcome expected, double actualValue)
    {
        if (expected.ExpectedValue > expected.BaselineValue)
            return actualValue >= expected.ExpectedValue;
        if (expected.ExpectedValue < expected.BaselineValue)
            return actualValue <= expected.ExpectedValue;
        var tolerance = Math.Max(Math.Abs(expected.ExpectedValue) * 1e-6, 1e-9);
        return Math.Abs(actualValue - expected.ExpectedValue) <= tolerance;
    }

    private static RealizedRecommendationValue NormalizeRealizedValue(
        RealizedRecommendationValue value,
        RecommendationValueEstimate estimate)
    {
        if (value.WindowStart == default || value.WindowEnd <= value.WindowStart)
            throw new ProcessImprovementRuleException("实际价值测量窗口无效。");
        var currency = value.Currency?.Trim();
        if (!string.Equals(currency, estimate.Currency, StringComparison.OrdinalIgnoreCase))
            throw new ProcessImprovementRuleException("实际价值币种必须与预期价值币种一致。");
        if (!double.IsFinite(value.GrossValue) ||
            !double.IsFinite(value.ImplementationCost) ||
            value.ImplementationCost < 0)
            throw new ProcessImprovementRuleException("实际价值金额无效。");
        if (string.IsNullOrWhiteSpace(value.CalculationNote) || value.CalculationNote.Length > 2000)
            throw new ProcessImprovementRuleException("实际价值计算说明不能为空且最长 2000 个字符。");
        return value with
        {
            Currency = currency!.ToUpperInvariant(),
            NetValue = value.GrossValue - value.ImplementationCost,
            CalculationNote = value.CalculationNote.Trim()
        };
    }

    private static bool IsAtLeastAsStrict(
        OperatingConstraint candidate,
        OperatingConstraint baseline)
        => baseline.Operator switch
        {
            "<" or "<=" => candidate.Limit <= baseline.Limit,
            ">" or ">=" => candidate.Limit >= baseline.Limit,
            "=" => Math.Abs(candidate.Limit - baseline.Limit) <= 1e-9,
            _ => false
        };

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
            throw new ProcessImprovementRuleException("模型版本没有通过门槛的评估记录。");
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
            new ImprovementAuditEntry
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

public sealed class ProcessImprovementRuleException(string message) : InvalidOperationException(message);
