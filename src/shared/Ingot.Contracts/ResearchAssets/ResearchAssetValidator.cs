// 集中校验 ResearchAssetValidator 的输入、范围和失败条件，调用方不得绕过。

using System.Text.RegularExpressions;

namespace Ingot.Contracts.ResearchAssets;

public static partial class ResearchAssetValidator
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
            ProcessExecutionIds = NormalizeValues(value.ProcessExecutionIds),
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
