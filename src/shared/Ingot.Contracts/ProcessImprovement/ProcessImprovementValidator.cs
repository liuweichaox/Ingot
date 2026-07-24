using System.Text.RegularExpressions;

namespace Ingot.Contracts.ProcessImprovement;

public static partial class ProcessImprovementValidator
{
    public static bool TryValidate(
        TrainingDatasetVersion? value,
        out TrainingDatasetVersion? normalized,
        out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("训练数据版本不能为空。", out error);
        if (!TryId(value.DatasetId, "训练数据集标识", out var datasetId, out error) ||
            !TryId(value.AnalysisPlanId, "分析方案标识", out var planId, out error) ||
            !TryId(value.DataModelId, "工艺数据模型标识", out var dataModelId, out error) ||
            !TryCode(value.TargetCode, "目标数据项", out var targetCode, out error))
            return false;
        if (value.Version <= 0 || value.AnalysisPlanVersion <= 0 || value.DataModelVersion <= 0)
            return Fail("所有版本号必须大于 0。", out error);
        if (!TryText(value.Name, "训练数据版本名称", 200, out var name, out error) ||
            !TryText(value.ContentHash, "训练数据内容哈希", 128, out var contentHash, out error) ||
            !TryText(value.CreatedBy, "创建人", 200, out var createdBy, out error))
            return false;
        if (!Sha256Pattern().IsMatch(contentHash!))
            return Fail("训练数据内容哈希必须是 64 位 SHA-256 十六进制字符串。", out error);
        if (value.WindowStart == default || value.WindowEnd <= value.WindowStart)
            return Fail("训练数据时间窗口无效。", out error);
        if (value.RowCount <= 0)
            return Fail("训练数据行数必须大于 0。", out error);
        if (value.FeatureCodes.Count == 0)
            return Fail("训练数据至少需要一个输入特征。", out error);
        normalized = value with
        {
            DatasetId = datasetId!,
            AnalysisPlanId = planId!,
            DataModelId = dataModelId!,
            TargetCode = targetCode!,
            Name = name!,
            ContentHash = contentHash!.ToLowerInvariant(),
            CreatedBy = createdBy!,
            FeatureCodes = NormalizeCodes(value.FeatureCodes),
            CycleIds = NormalizeValues(value.CycleIds),
            ContextSelector = NormalizeMap(value.ContextSelector),
            CreatedAt = value.CreatedAt == default ? DateTimeOffset.UtcNow : value.CreatedAt
        };
        error = null;
        return true;
    }

    public static bool TryValidate(
        ProcessModelVersion? value,
        out ProcessModelVersion? normalized,
        out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("模型版本不能为空。", out error);
        if (!TryId(value.ModelId, "模型标识", out var modelId, out error) ||
            !TryId(value.DatasetId, "训练数据集标识", out var datasetId, out error) ||
            !TryCode(value.ProblemCode, "问题编码", out var problemCode, out error) ||
            !TryCode(value.OutputCode, "输出编码", out var outputCode, out error))
            return false;
        if (value.Version <= 0 || value.DatasetVersion <= 0)
            return Fail("模型和训练数据版本号必须大于 0。", out error);
        if (!ProcessModelStatuses.IsValid(value.Status))
            return Fail("模型状态无效。", out error);
        if (!TryText(value.Name, "模型名称", 200, out var name, out error) ||
            !TryText(value.ModelKind, "模型类型", 80, out var modelKind, out error) ||
            !TryText(value.Algorithm, "算法", 200, out var algorithm, out error) ||
            !TryText(value.CreatedBy, "创建人", 200, out var createdBy, out error))
            return false;
        if (value.InputFeatureCodes.Count == 0)
            return Fail("模型至少需要一个输入特征。", out error);
        var artifactSha256 = NormalizeOptional(value.ArtifactSha256)?.ToLowerInvariant();
        if (artifactSha256 is not null && !Sha256Pattern().IsMatch(artifactSha256))
            return Fail("模型产物哈希必须是 64 位 SHA-256 十六进制字符串。", out error);
        var now = DateTimeOffset.UtcNow;
        normalized = value with
        {
            ModelId = modelId!,
            DatasetId = datasetId!,
            ProblemCode = problemCode!,
            OutputCode = outputCode!,
            Name = name!,
            ModelKind = modelKind!.ToLowerInvariant(),
            Algorithm = algorithm!,
            ArtifactRef = NormalizeOptional(value.ArtifactRef),
            ArtifactSha256 = artifactSha256,
            CreatedBy = createdBy!,
            Status = value.Status.ToLowerInvariant(),
            ContextSelector = NormalizeMap(value.ContextSelector),
            InputFeatureCodes = NormalizeCodes(value.InputFeatureCodes),
            CreatedAt = value.CreatedAt == default ? now : value.CreatedAt,
            UpdatedAt = now
        };
        error = null;
        return true;
    }

    public static bool TryValidate(ModelEvaluation? value, out ModelEvaluation? normalized, out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("模型评估不能为空。", out error);
        if (!TryId(value.ModelId, "模型标识", out var modelId, out error) ||
            !TryText(value.EvaluatedBy, "评估人", 200, out var evaluatedBy, out error))
            return false;
        if (value.ModelVersion <= 0 || value.SampleCount <= 0)
            return Fail("模型版本和评估样本数必须大于 0。", out error);
        if (value.Metrics.Count == 0)
            return Fail("模型评估至少需要一个指标。", out error);
        foreach (var metric in value.Metrics)
        {
            if (!TryCode(metric.Code, "评估指标编码", out _, out error))
                return false;
            if (!double.IsFinite(metric.Value) ||
                metric.RequiredMinimum is { } minimum && !double.IsFinite(minimum) ||
                metric.RequiredMaximum is { } maximum && !double.IsFinite(maximum) ||
                metric.RequiredMinimum > metric.RequiredMaximum)
                return Fail($"评估指标 {metric.Code} 的数值或门槛无效。", out error);
        }
        var thresholdsPassed = value.Metrics.All(metric =>
            (metric.RequiredMinimum is null || metric.Value >= metric.RequiredMinimum) &&
            (metric.RequiredMaximum is null || metric.Value <= metric.RequiredMaximum));
        if (value.Passed != thresholdsPassed)
            return Fail("评估结论必须与所有指标门槛的判定一致。", out error);
        normalized = value with
        {
            EvaluationId = value.EvaluationId == Guid.Empty ? Guid.CreateVersion7() : value.EvaluationId,
            ModelId = modelId!,
            EvaluatedBy = evaluatedBy!,
            Split = NormalizeOptional(value.Split)?.ToLowerInvariant() ?? "holdout",
            Metrics = value.Metrics.Select(metric => metric with
            {
                Code = metric.Code.Trim().ToLowerInvariant(),
                Unit = NormalizeOptional(metric.Unit)
            }).ToArray(),
            Notes = NormalizeOptional(value.Notes),
            EvaluatedAt = value.EvaluatedAt == default ? DateTimeOffset.UtcNow : value.EvaluatedAt
        };
        error = null;
        return true;
    }

    public static bool TryValidate(ModelDriftReading? value, out ModelDriftReading? normalized, out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("漂移记录不能为空。", out error);
        if (!TryId(value.ModelId, "模型标识", out var modelId, out error) ||
            !TryCode(value.MetricCode, "漂移指标编码", out var metricCode, out error) ||
            !TryText(value.RecordedBy, "记录人", 200, out var recordedBy, out error))
            return false;
        if (value.ModelVersion <= 0 || value.SampleCount <= 0)
            return Fail("模型版本和漂移样本数必须大于 0。", out error);
        if (!double.IsFinite(value.Value) || !double.IsFinite(value.WarningThreshold) ||
            !double.IsFinite(value.StopThreshold) || value.WarningThreshold < 0 ||
            value.StopThreshold <= value.WarningThreshold)
            return Fail("漂移值或门槛无效，停用门槛必须高于预警门槛。", out error);
        if (value.WindowStart == default || value.WindowEnd <= value.WindowStart)
            return Fail("漂移观察窗口无效。", out error);
        normalized = value with
        {
            ReadingId = value.ReadingId == Guid.Empty ? Guid.CreateVersion7() : value.ReadingId,
            ModelId = modelId!,
            MetricCode = metricCode!,
            RecordedBy = recordedBy!,
            RecordedAt = value.RecordedAt == default ? DateTimeOffset.UtcNow : value.RecordedAt
        };
        error = null;
        return true;
    }

    public static bool TryValidate(InvestigationCase? value, out InvestigationCase? normalized, out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("调查记录不能为空。", out error);
        if (!TryText(value.Title, "调查标题", 240, out var title, out error) ||
            !TryCode(value.ProblemCode, "问题编码", out var problemCode, out error) ||
            !TryText(value.OwnerUserId, "负责人", 200, out var owner, out error))
            return false;
        if (!InvestigationStatuses.IsValid(value.Status))
            return Fail("调查状态无效。", out error);
        var now = DateTimeOffset.UtcNow;
        normalized = value with
        {
            InvestigationId = value.InvestigationId == Guid.Empty ? Guid.CreateVersion7() : value.InvestigationId,
            Title = title!,
            ProblemCode = problemCode!,
            OwnerUserId = owner!,
            Description = NormalizeOptional(value.Description),
            Status = value.Status.ToLowerInvariant(),
            ContextSelector = NormalizeMap(value.ContextSelector),
            CycleIds = NormalizeValues(value.CycleIds),
            CreatedAt = value.CreatedAt == default ? now : value.CreatedAt,
            UpdatedAt = now
        };
        error = null;
        return true;
    }

    public static bool TryValidate(PossibleCause? value, out PossibleCause? normalized, out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("可能原因不能为空。", out error);
        if (value.InvestigationId == Guid.Empty)
            return Fail("调查记录标识不能为空。", out error);
        if (!TryText(value.Title, "可能原因标题", 240, out var title, out error) ||
            !TryText(value.Reasoning, "原因说明", 4000, out var reasoning, out error) ||
            !TryText(value.CreatedBy, "创建人", 200, out var createdBy, out error))
            return false;
        if (!PossibleCauseStatuses.IsValid(value.Status))
            return Fail("可能原因状态无效。", out error);
        if (NormalizeOptional(value.ParameterCode) is null && NormalizeOptional(value.SignalCode) is null)
            return Fail("可能原因必须关联参数或信号。", out error);
        var now = DateTimeOffset.UtcNow;
        normalized = value with
        {
            CauseId = value.CauseId == Guid.Empty ? Guid.CreateVersion7() : value.CauseId,
            Title = title!,
            Reasoning = reasoning!,
            CreatedBy = createdBy!,
            Status = value.Status.ToLowerInvariant(),
            ParameterCode = NormalizeOptional(value.ParameterCode)?.ToLowerInvariant(),
            SignalCode = NormalizeOptional(value.SignalCode)?.ToLowerInvariant(),
            PhaseCode = NormalizeOptional(value.PhaseCode)?.ToLowerInvariant(),
            Direction = NormalizeOptional(value.Direction)?.ToLowerInvariant() ?? "unknown",
            RelatedCycleIds = NormalizeValues(value.RelatedCycleIds),
            CreatedAt = value.CreatedAt == default ? now : value.CreatedAt,
            UpdatedAt = now
        };
        error = null;
        return true;
    }

    public static bool TryValidate(ProcessTrial? value, out ProcessTrial? normalized, out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("调整试验不能为空。", out error);
        if (value.InvestigationId == Guid.Empty || value.CauseId == Guid.Empty)
            return Fail("调整试验必须关联调查记录和可能原因。", out error);
        if (!ProcessTrialStatuses.IsValid(value.Status))
            return Fail("调整试验状态无效。", out error);
        if (!TrialRigorLevels.IsValid(value.RigorLevel))
            return Fail("试验严谨度无效。", out error);
        if (!TryText(value.Name, "调整试验名称", 240, out var name, out error) ||
            !TryText(value.StopRule, "停止规则", 2000, out var stopRule, out error) ||
            !TryText(value.RollbackPlan, "回退方案", 2000, out var rollbackPlan, out error) ||
            !TryText(value.CreatedBy, "创建人", 200, out var createdBy, out error))
            return false;
        if (value.ParameterChanges.Count == 0)
            return Fail("调整试验至少需要一个参数变化。", out error);
        if (value.SafetyConstraints.Count == 0)
            return Fail("调整试验至少需要一个安全约束。", out error);
        foreach (var change in value.ParameterChanges)
        {
            if (!TryCode(change.ParameterCode, "参数编码", out _, out error) ||
                !double.IsFinite(change.BaselineValue) || !double.IsFinite(change.TrialValue) ||
                !double.IsFinite(change.AllowedMinimum) || !double.IsFinite(change.AllowedMaximum) ||
                change.AllowedMinimum > change.AllowedMaximum ||
                change.TrialValue < change.AllowedMinimum || change.TrialValue > change.AllowedMaximum)
                return Fail($"参数 {change.ParameterCode} 的数值或允许范围无效。", out error);
        }
        if (!TryValidateConstraints(value.SafetyConstraints, out error))
            return false;
        if (value.RigorLevel == TrialRigorLevels.Confirmatory && value.Protocol is null)
            return Fail("验证性试验必须提供预注册实验协议。", out error);
        ExperimentalProtocol? protocol = null;
        if (value.Protocol is not null &&
            !TryValidateProtocol(value.Protocol, value.SafetyConstraints, out protocol, out error))
        {
            return false;
        }
        var now = DateTimeOffset.UtcNow;
        normalized = value with
        {
            TrialId = value.TrialId == Guid.Empty ? Guid.CreateVersion7() : value.TrialId,
            Name = name!,
            StopRule = stopRule!,
            RollbackPlan = rollbackPlan!,
            CreatedBy = createdBy!,
            Status = value.Status.ToLowerInvariant(),
            TrialKind = NormalizeOptional(value.TrialKind)?.ToLowerInvariant() ?? "controlled-field-trial",
            RigorLevel = value.RigorLevel.ToLowerInvariant(),
            Protocol = protocol,
            ParameterChanges = value.ParameterChanges.Select(change => change with
            {
                ParameterCode = change.ParameterCode.Trim().ToLowerInvariant(),
                PhaseCode = NormalizeOptional(change.PhaseCode)?.ToLowerInvariant(),
                Unit = change.Unit.Trim()
            }).ToArray(),
            SafetyConstraints = NormalizeConstraints(value.SafetyConstraints),
            ControlCycleIds = NormalizeValues(value.ControlCycleIds),
            TrialCycleIds = NormalizeValues(value.TrialCycleIds),
            CreatedAt = value.CreatedAt == default ? now : value.CreatedAt,
            UpdatedAt = now
        };
        error = null;
        return true;
    }

    public static bool TryValidate(TrialResult? value, out TrialResult? normalized, out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("试验结果不能为空。", out error);
        if (value.TrialId == Guid.Empty)
            return Fail("试验标识不能为空。", out error);
        if (!TryCode(value.MetricCode, "结果指标编码", out var metricCode, out error) ||
            !TryText(value.Unit, "结果单位", 40, out var unit, out error) ||
            !TryText(value.RecordedBy, "记录人", 200, out var recordedBy, out error))
            return false;
        if (!double.IsFinite(value.BaselineValue) || !double.IsFinite(value.TrialValue) ||
            !double.IsFinite(value.EffectValue) ||
            value.LowerConfidenceBound is { } lower && !double.IsFinite(lower) ||
            value.UpperConfidenceBound is { } upper && !double.IsFinite(upper) ||
            value.LowerConfidenceBound > value.UpperConfidenceBound)
            return Fail("试验结果数值或区间无效。", out error);
        if (Math.Abs(value.EffectValue - (value.TrialValue - value.BaselineValue)) > 1e-9)
            return Fail("试验结果变化量必须等于试验值减去基准值。", out error);
        if (value.BaselineSampleCount <= 0 || value.TrialSampleCount <= 0)
            return Fail("试验结果的基准和试验样本数必须大于 0。", out error);
        var evidenceHash = NormalizeOptional(value.EvidenceHash)?.ToLowerInvariant();
        if (evidenceHash is not null && !Sha256Pattern().IsMatch(evidenceHash))
            return Fail("试验结果证据哈希必须是 64 位 SHA-256 十六进制字符串。", out error);
        if (value.CalculatedFromSource &&
            (evidenceHash is null || string.Equals(value.ComputationMethod, "manual", StringComparison.OrdinalIgnoreCase)))
        {
            return Fail("源数据计算结果必须包含证据哈希和计算方法。", out error);
        }
        if (value.StandardError is { } standardError &&
            (!double.IsFinite(standardError) || standardError < 0) ||
            value.DegreesOfFreedom is { } degreesOfFreedom &&
            (!double.IsFinite(degreesOfFreedom) || degreesOfFreedom <= 0))
        {
            return Fail("试验结果标准误或自由度无效。", out error);
        }
        normalized = value with
        {
            ResultId = value.ResultId == Guid.Empty ? Guid.CreateVersion7() : value.ResultId,
            MetricCode = metricCode!,
            Unit = unit!,
            EvidenceHash = evidenceHash,
            ComputationMethod = NormalizeOptional(value.ComputationMethod)?.ToLowerInvariant() ?? "manual",
            RecordedBy = recordedBy!,
            RecordedAt = value.RecordedAt == default ? DateTimeOffset.UtcNow : value.RecordedAt
        };
        error = null;
        return true;
    }

    private static bool TryValidateProtocol(
        ExperimentalProtocol value,
        IReadOnlyList<OperatingConstraint> constraints,
        out ExperimentalProtocol? normalized,
        out string? error)
    {
        normalized = null;
        if (!TryText(value.Hypothesis, "预注册假设", 4000, out var hypothesis, out error) ||
            !TryCode(value.PrimaryMetric.MetricCode, "主要指标编码", out var metricCode, out error) ||
            !TryCode(value.PrimaryMetric.SignalCode, "主要指标信号", out var signalCode, out error) ||
            !TryCode(value.PrimaryMetric.FeatureCode, "主要指标特征", out var featureCode, out error) ||
            !TryText(value.PrimaryMetric.Unit, "主要指标单位", 40, out var unit, out error) ||
            !TryText(value.Estimator, "估计方法", 120, out var estimator, out error) ||
            !TryText(value.PreRegisteredBy, "协议预注册人", 200, out var preRegisteredBy, out error))
        {
            return false;
        }
        if (value.PreRegisteredAt == default)
            return Fail("验证性试验协议必须记录预注册时间。", out error);
        if (value.MinimumControlSampleSize < 2 || value.MinimumTrialSampleSize < 2)
            return Fail("验证性试验每组计划样本量至少为 2。", out error);
        if (!double.IsFinite(value.Alpha) || value.Alpha is < 0.001 or > 0.2)
            return Fail("显著性水平必须位于 0.001 到 0.2 之间。", out error);
        var allocationMethod = NormalizeOptional(value.AllocationMethod)?.ToLowerInvariant() ?? "blocked";
        if (allocationMethod is not ("randomized" or "blocked" or "sequential"))
            return Fail("分配方法只能是 randomized、blocked 或 sequential。", out error);
        if (!string.Equals(
                estimator,
                ScientificTrialEstimators.WelchDifferenceInMeansCornishFisherV1,
                StringComparison.OrdinalIgnoreCase))
        {
            return Fail("当前不支持该验证性试验估计方法。", out error);
        }
        var direction = NormalizeOptional(value.PrimaryMetric.Direction)?.ToLowerInvariant() ?? "two-sided";
        if (direction is not ("higher-is-better" or "lower-is-better" or "two-sided"))
            return Fail("主要指标方向无效。", out error);
        var primaryPhase = NormalizeOptional(value.PrimaryMetric.PhaseCode)?.ToLowerInvariant();
        if (primaryPhase is not null && (value.PrimaryMetric.PhaseOrder is null or <= 0))
            return Fail("阶段主要指标必须指定大于 0 的阶段序号。", out error);
        var constraintCodes = constraints.Select(static constraint => constraint.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in value.SafetyMetricBindings)
        {
            if (!constraintCodes.Contains(binding.ConstraintCode))
                return Fail($"安全指标绑定引用了不存在的约束：{binding.ConstraintCode}。", out error);
            if (!TryCode(binding.SignalCode, "安全指标信号", out _, out error) ||
                !TryCode(binding.FeatureCode, "安全指标特征", out _, out error))
            {
                return false;
            }
            if (!string.IsNullOrWhiteSpace(binding.PhaseCode) && (binding.PhaseOrder is null or <= 0))
                return Fail($"安全指标 {binding.ConstraintCode} 必须指定大于 0 的阶段序号。", out error);
        }
        if (constraintCodes.Any(code => !value.SafetyMetricBindings.Any(binding =>
                string.Equals(binding.ConstraintCode, code, StringComparison.OrdinalIgnoreCase))))
        {
            return Fail("验证性试验的每个安全约束都必须绑定可由源数据计算的信号特征。", out error);
        }
        normalized = value with
        {
            Hypothesis = hypothesis!,
            PrimaryMetric = value.PrimaryMetric with
            {
                MetricCode = metricCode!,
                SignalCode = signalCode!,
                FeatureCode = featureCode!,
                PhaseCode = primaryPhase,
                Unit = unit!,
                Direction = direction
            },
            AllocationMethod = allocationMethod,
            BlockingKeys = NormalizeCodes(value.BlockingKeys),
            Estimator = estimator!.ToLowerInvariant(),
            ExclusionRules = NormalizeValues(value.ExclusionRules),
            SafetyMetricBindings = value.SafetyMetricBindings.Select(binding => binding with
            {
                ConstraintCode = binding.ConstraintCode.Trim().ToLowerInvariant(),
                SignalCode = binding.SignalCode.Trim().ToLowerInvariant(),
                FeatureCode = binding.FeatureCode.Trim().ToLowerInvariant(),
                PhaseCode = NormalizeOptional(binding.PhaseCode)?.ToLowerInvariant()
            }).ToArray(),
            PreRegisteredBy = preRegisteredBy!
        };
        error = null;
        return true;
    }

    public static bool TryValidate(
        InvestigationConclusion? value,
        out InvestigationConclusion? normalized,
        out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("调查结论不能为空。", out error);
        if (value.InvestigationId == Guid.Empty || value.CauseId == Guid.Empty || value.TrialId == Guid.Empty)
            return Fail("调查结论必须关联调查记录、可能原因和调整试验。", out error);
        if (value.Decision is not (PossibleCauseStatuses.Confirmed or PossibleCauseStatuses.Rejected or PossibleCauseStatuses.Inconclusive))
            return Fail("调查结论只能是确认、排除或暂不确定。", out error);
        if (!TryText(value.Summary, "结论摘要", 4000, out var summary, out error) ||
            !TryText(value.ReviewedBy, "复核人", 200, out var reviewedBy, out error))
            return false;
        if (value.ResultIds.Count == 0)
            return Fail("调查结论至少需要关联一个试验结果。", out error);
        normalized = value with
        {
            ConclusionId = value.ConclusionId == Guid.Empty ? Guid.CreateVersion7() : value.ConclusionId,
            Summary = summary!,
            ReviewedBy = reviewedBy!,
            ApplicableContext = NormalizeMap(value.ApplicableContext),
            ResultIds = value.ResultIds.Where(id => id != Guid.Empty).Distinct().ToArray(),
            ReviewedAt = value.ReviewedAt == default ? DateTimeOffset.UtcNow : value.ReviewedAt
        };
        error = null;
        return true;
    }

    public static bool TryValidate(KnowledgeRecord? value, out KnowledgeRecord? normalized, out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("知识记录不能为空。", out error);
        if (value.SourceId == Guid.Empty)
            return Fail("知识来源标识不能为空。", out error);
        if (!TryText(value.Content, "知识内容", 16000, out var content, out error) ||
            !TryText(value.CreatedBy, "创建人", 200, out var createdBy, out error))
            return false;
        normalized = value with
        {
            RecordId = value.RecordId == Guid.Empty ? Guid.CreateVersion7() : value.RecordId,
            Content = content!,
            CreatedBy = createdBy!,
            Category = NormalizeOptional(value.Category)?.ToLowerInvariant() ?? "field-note",
            PageOrSheet = NormalizeOptional(value.PageOrSheet),
            Region = NormalizeOptional(value.Region),
            StructuredValues = NormalizeMap(value.StructuredValues),
            CreatedAt = value.CreatedAt == default ? DateTimeOffset.UtcNow : value.CreatedAt
        };
        error = null;
        return true;
    }

    public static bool TryValidate(
        ParameterRecommendation? value,
        out ParameterRecommendation? normalized,
        out string? error)
    {
        normalized = null;
        if (value is null)
            return Fail("参数建议不能为空。", out error);
        if (value.InvestigationId == Guid.Empty || value.ConclusionId == Guid.Empty)
            return Fail("参数建议必须关联调查记录和调查结论。", out error);
        if (!RecommendationStatuses.IsValid(value.Status))
            return Fail("参数建议状态无效。", out error);
        if (!TryText(value.Title, "参数建议标题", 240, out var title, out error) ||
            !TryText(value.RiskSummary, "风险说明", 4000, out var riskSummary, out error) ||
            !TryText(value.StopRule, "停止规则", 2000, out var stopRule, out error) ||
            !TryText(value.RollbackPlan, "回退方案", 2000, out var rollbackPlan, out error) ||
            !TryText(value.CreatedBy, "创建人", 200, out var createdBy, out error))
            return false;
        if (value.ParameterSettings.Count == 0 || value.ExpectedOutcomes.Count == 0)
            return Fail("参数建议至少需要一个参数设置和一个预期结果。", out error);
        if (value.Constraints.Count == 0)
            return Fail("参数建议至少需要一个运行约束。", out error);
        foreach (var setting in value.ParameterSettings)
        {
            if (!TryCode(setting.ParameterCode, "参数编码", out _, out error) ||
                !double.IsFinite(setting.CurrentValue) || !double.IsFinite(setting.RecommendedValue) ||
                !double.IsFinite(setting.AllowedMinimum) || !double.IsFinite(setting.AllowedMaximum) ||
                setting.AllowedMinimum > setting.AllowedMaximum ||
                setting.RecommendedValue < setting.AllowedMinimum ||
                setting.RecommendedValue > setting.AllowedMaximum)
                return Fail($"参数 {setting.ParameterCode} 的建议值或允许范围无效。", out error);
        }
        if (!TryValidateConstraints(value.Constraints, out error))
            return false;
        foreach (var outcome in value.ExpectedOutcomes)
        {
            if (!TryCode(outcome.MetricCode, "预期结果指标", out _, out error) ||
                !double.IsFinite(outcome.BaselineValue) || !double.IsFinite(outcome.ExpectedValue) ||
                outcome.LowerBound is { } lower && !double.IsFinite(lower) ||
                outcome.UpperBound is { } upper && !double.IsFinite(upper) ||
                outcome.LowerBound > outcome.UpperBound)
                return Fail($"预期结果 {outcome.MetricCode} 的数值或区间无效。", out error);
        }
        if (value.ValueEstimate is null)
            return Fail("参数建议必须包含预期经济价值。", out error);
        if (!TryText(value.ValueEstimate.Currency, "价值币种", 12, out var currency, out error) ||
            !TryText(value.ValueEstimate.CalculationNote, "价值计算说明", 2000, out var calculationNote, out error))
            return false;
        if (!double.IsFinite(value.ValueEstimate.ExpectedAnnualValue) ||
            !double.IsFinite(value.ValueEstimate.TrialCost) ||
            !double.IsFinite(value.ValueEstimate.ImplementationCost) ||
            !double.IsFinite(value.ValueEstimate.DownsideAtRisk) ||
            value.ValueEstimate.TrialCost < 0 ||
            value.ValueEstimate.ImplementationCost < 0 ||
            value.ValueEstimate.DownsideAtRisk < 0)
            return Fail("预期经济价值、成本或风险金额无效。", out error);
        var now = DateTimeOffset.UtcNow;
        normalized = value with
        {
            RecommendationId = value.RecommendationId == Guid.Empty ? Guid.CreateVersion7() : value.RecommendationId,
            Title = title!,
            RiskSummary = riskSummary!,
            StopRule = stopRule!,
            RollbackPlan = rollbackPlan!,
            CreatedBy = createdBy!,
            Status = value.Status.ToLowerInvariant(),
            ModelId = NormalizeOptional(value.ModelId)?.ToLowerInvariant(),
            ApplicableContext = NormalizeMap(value.ApplicableContext),
            ParameterSettings = value.ParameterSettings.Select(setting => setting with
            {
                ParameterCode = setting.ParameterCode.Trim().ToLowerInvariant(),
                PhaseCode = NormalizeOptional(setting.PhaseCode)?.ToLowerInvariant(),
                Unit = setting.Unit.Trim()
            }).ToArray(),
            Constraints = NormalizeConstraints(value.Constraints),
            ExpectedOutcomes = value.ExpectedOutcomes.Select(outcome => outcome with
            {
                MetricCode = outcome.MetricCode.Trim().ToLowerInvariant(),
                Unit = outcome.Unit.Trim()
            }).ToArray(),
            ValueEstimate = value.ValueEstimate with
            {
                Currency = currency!.ToUpperInvariant(),
                CalculationNote = calculationNote!
            },
            CreatedAt = value.CreatedAt == default ? now : value.CreatedAt,
            UpdatedAt = now
        };
        error = null;
        return true;
    }

    private static bool TryValidateConstraints(IReadOnlyList<OperatingConstraint> constraints, out string? error)
    {
        foreach (var constraint in constraints)
        {
            if (!TryCode(constraint.Code, "约束编码", out _, out error) ||
                !TryText(constraint.Description, "约束说明", 1000, out _, out error) ||
                !double.IsFinite(constraint.Limit) ||
                constraint.Operator is not ("<" or "<=" or "=" or ">=" or ">"))
                return Fail($"约束 {constraint.Code} 无效。", out error);
        }
        error = null;
        return true;
    }

    private static IReadOnlyList<OperatingConstraint> NormalizeConstraints(
        IReadOnlyList<OperatingConstraint> constraints)
        => constraints.Select(constraint => constraint with
        {
            Code = constraint.Code.Trim().ToLowerInvariant(),
            Description = constraint.Description.Trim(),
            Unit = constraint.Unit.Trim()
        }).ToArray();

    private static bool TryId(string? value, string field, out string? normalized, out string? error)
    {
        normalized = NormalizeOptional(value)?.ToLowerInvariant();
        if (normalized is null || normalized.Length > 120 || !IdPattern().IsMatch(normalized))
            return Fail($"{field}必须是 1–120 位的小写字母、数字、点、下划线或连字符。", out error);
        error = null;
        return true;
    }

    private static bool TryCode(string? value, string field, out string? normalized, out string? error)
        => TryId(value, field, out normalized, out error);

    private static bool TryText(
        string? value,
        string field,
        int maximumLength,
        out string? normalized,
        out string? error)
    {
        normalized = NormalizeOptional(value);
        if (normalized is null || normalized.Length > maximumLength)
            return Fail($"{field}不能为空且最长 {maximumLength} 个字符。", out error);
        error = null;
        return true;
    }

    private static IReadOnlyDictionary<string, string> NormalizeMap(IReadOnlyDictionary<string, string> values)
        => values
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(
                pair => pair.Key.Trim().ToLowerInvariant(),
                pair => pair.Value.Trim(),
                StringComparer.Ordinal);

    private static IReadOnlyList<string> NormalizeCodes(IReadOnlyList<string> values)
        => values
            .Select(NormalizeOptional)
            .Where(value => value is not null)
            .Select(value => value!.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<string> NormalizeValues(IReadOnlyList<string> values)
        => values
            .Select(NormalizeOptional)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
