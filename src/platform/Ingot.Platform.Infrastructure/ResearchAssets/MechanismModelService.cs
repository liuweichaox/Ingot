using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class MechanismModelService(IResearchAssetStore store)
{
    public async Task<MechanismModelVersion> SaveModelDraftAsync(
        MechanismModelVersion request,
        string userId,
        CancellationToken ct = default)
    {
        ValidateModel(request);
        var id = NormalizeId(request.ModelId);
        var existing = await store.GetMechanismModelAsync(id, request.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != MechanismModelStatuses.Draft)
            throw new ResearchAssetRuleException("只有草稿机理模型版本可以修改。");
        var now = DateTimeOffset.UtcNow;
        var normalized = request with
        {
            ModelId = id,
            Status = MechanismModelStatuses.Draft,
            EquationKind = "affine",
            Inputs = request.Inputs.Select(NormalizeVariable).OrderBy(static item => item.Code).ToArray(),
            Output = NormalizeVariable(request.Output),
            Coefficients = request.Coefficients.ToDictionary(
                static pair => pair.Key.Trim().ToLowerInvariant(),
                static pair => pair.Value,
                StringComparer.Ordinal),
            ApplicabilityContext = NormalizeContext(request.ApplicabilityContext),
            ScientificBasis = request.ScientificBasis.Trim(),
            SourceReference = NullIfBlank(request.SourceReference),
            CreatedBy = existing?.CreatedBy ?? userId,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            ValidatedBy = null,
            ValidatedAt = null
        };
        normalized = normalized with { ContentHash = ModelHash(normalized) };
        var saved = await store.SaveMechanismModelAsync(normalized, ct).ConfigureAwait(false);
        await AuditAsync(
            "mechanism-model",
            $"{saved.ModelId}:{saved.Version}",
            existing is null ? "draft-created" : "draft-updated",
            userId,
            ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<MechanismModelVersion> ChangeModelStatusAsync(
        string modelId,
        int version,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        var model = await store.GetMechanismModelAsync(NormalizeId(modelId), version, ct)
            .ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("机理模型不存在。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        var allowed = model.Status switch
        {
            MechanismModelStatuses.Draft => targetStatus == MechanismModelStatuses.Validated,
            MechanismModelStatuses.Validated => targetStatus is MechanismModelStatuses.Active or MechanismModelStatuses.Retired,
            MechanismModelStatuses.Active => targetStatus == MechanismModelStatuses.Retired,
            _ => false
        };
        if (!allowed)
            throw new ResearchAssetRuleException($"不允许从 {model.Status} 转换到 {targetStatus}。");
        var now = DateTimeOffset.UtcNow;
        var updated = model with
        {
            Status = targetStatus,
            ValidatedBy = targetStatus == MechanismModelStatuses.Validated ? userId : model.ValidatedBy,
            ValidatedAt = targetStatus == MechanismModelStatuses.Validated ? now : model.ValidatedAt,
            UpdatedAt = now
        };
        await store.SaveMechanismModelAsync(updated, ct).ConfigureAwait(false);
        await AuditAsync(
            "mechanism-model",
            $"{model.ModelId}:{model.Version}",
            $"status-{targetStatus}",
            userId,
            ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<MechanismFusionDefinition> SaveFusionDraftAsync(
        MechanismFusionDefinition request,
        string userId,
        CancellationToken ct = default)
    {
        ValidateFusion(request);
        var fusionId = NormalizeId(request.FusionId);
        var modelId = NormalizeId(request.MechanismModelId);
        var model = await store.GetMechanismModelAsync(modelId, request.MechanismModelVersion, ct)
            .ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("融合定义引用的机理模型不存在。");
        if (model.Status is not (MechanismModelStatuses.Validated or MechanismModelStatuses.Active))
            throw new ResearchAssetRuleException("融合定义只能引用已经验证的机理模型。");
        var existing = await store.GetMechanismFusionAsync(fusionId, request.Version, ct).ConfigureAwait(false);
        if (existing is not null && existing.Status != MechanismModelStatuses.Draft)
            throw new ResearchAssetRuleException("只有草稿融合定义可以修改。");
        var now = DateTimeOffset.UtcNow;
        var normalized = request with
        {
            FusionId = fusionId,
            Status = MechanismModelStatuses.Draft,
            Mode = request.Mode.Trim().ToLowerInvariant(),
            MechanismModelId = modelId,
            DataModelId = NullIfBlank(request.DataModelId)?.ToLowerInvariant(),
            MechanismFeatureCode = request.MechanismFeatureCode.Trim().ToLowerInvariant(),
            OutputCode = request.OutputCode.Trim().ToLowerInvariant(),
            ApplicabilityContext = NormalizeContext(request.ApplicabilityContext),
            CreatedBy = existing?.CreatedBy ?? userId,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        normalized = normalized with { ContentHash = FusionHash(normalized) };
        var saved = await store.SaveMechanismFusionAsync(normalized, ct).ConfigureAwait(false);
        await AuditAsync(
            "mechanism-fusion",
            $"{saved.FusionId}:{saved.Version}",
            existing is null ? "draft-created" : "draft-updated",
            userId,
            ct).ConfigureAwait(false);
        return saved;
    }

    public async Task<MechanismFusionDefinition> ChangeFusionStatusAsync(
        string fusionId,
        int version,
        string targetStatus,
        string userId,
        CancellationToken ct = default)
    {
        var fusion = await store.GetMechanismFusionAsync(NormalizeId(fusionId), version, ct)
            .ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("机理融合定义不存在。");
        targetStatus = targetStatus.Trim().ToLowerInvariant();
        var allowed = fusion.Status switch
        {
            MechanismModelStatuses.Draft => targetStatus == MechanismModelStatuses.Validated,
            MechanismModelStatuses.Validated => targetStatus is MechanismModelStatuses.Active or MechanismModelStatuses.Retired,
            MechanismModelStatuses.Active => targetStatus == MechanismModelStatuses.Retired,
            _ => false
        };
        if (!allowed)
            throw new ResearchAssetRuleException($"不允许从 {fusion.Status} 转换到 {targetStatus}。");
        var updated = fusion with { Status = targetStatus, UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveMechanismFusionAsync(updated, ct).ConfigureAwait(false);
        await AuditAsync(
            "mechanism-fusion",
            $"{fusion.FusionId}:{fusion.Version}",
            $"status-{targetStatus}",
            userId,
            ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<MechanismFusionExecutionResult> ExecuteAsync(
        MechanismFusionExecutionRequest request,
        CancellationToken ct = default)
    {
        var fusion = await store.GetMechanismFusionAsync(
                NormalizeId(request.FusionId),
                request.FusionVersion,
                ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("机理融合定义不存在。");
        if (fusion.Status != MechanismModelStatuses.Active)
            throw new ResearchAssetRuleException("只有已启用的机理融合定义可以执行。");
        var model = await store.GetMechanismModelAsync(
                fusion.MechanismModelId,
                fusion.MechanismModelVersion,
                ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("融合定义引用的机理模型不存在。");
        if (model.Status != MechanismModelStatuses.Active)
            throw new ResearchAssetRuleException("融合定义引用的机理模型未启用。");
        if (!Matches(model.ApplicabilityContext, request.OperatingContext) ||
            !Matches(fusion.ApplicabilityContext, request.OperatingContext))
        {
            throw new ResearchAssetRuleException("当前运行信息不在机理模型或融合定义的适用范围内。");
        }
        var mechanism = EvaluateModel(model, request.MechanismInputs);
        var features = new Dictionary<string, double>(StringComparer.Ordinal);
        double? fused = fusion.Mode switch
        {
            MechanismFusionModes.Calibration =>
                fusion.CalibrationScale * mechanism + fusion.CalibrationOffset,
            MechanismFusionModes.PostProcessing =>
                RequireDataPrediction(request) +
                fusion.PostProcessingGain * (mechanism - fusion.MechanismReference),
            MechanismFusionModes.MechanismAsFeature =>
                AddMechanismFeature(features, fusion.MechanismFeatureCode, mechanism, request),
            MechanismFusionModes.Ensemble =>
                fusion.MechanismWeight * mechanism +
                (1 - fusion.MechanismWeight) * RequireDataPrediction(request),
            _ => throw new ResearchAssetRuleException("未知的机理融合方式。")
        };
        if (fused.HasValue)
            EnsureRange(model.Output, fused.Value, "融合输出");
        var executionHash = ExecutionHash(fusion, model, request, mechanism, fused);
        return new MechanismFusionExecutionResult
        {
            FusionId = fusion.FusionId,
            FusionVersion = fusion.Version,
            Mode = fusion.Mode,
            MechanismPrediction = mechanism,
            DataPrediction = request.DataPrediction,
            FusedPrediction = fused,
            AugmentedFeatures = features,
            OutputCode = fusion.OutputCode,
            OutputUnit = model.Output.Unit,
            MechanismModelHash = model.ContentHash,
            FusionDefinitionHash = fusion.ContentHash,
            ExecutionHash = executionHash
        };
    }

    private static double EvaluateModel(
        MechanismModelVersion model,
        IReadOnlyDictionary<string, double> inputs)
    {
        var value = model.Intercept;
        foreach (var input in model.Inputs)
        {
            if (!inputs.TryGetValue(input.Code, out var observed) || !double.IsFinite(observed))
                throw new ResearchAssetRuleException($"缺少有效机理输入：{input.Code}。");
            EnsureRange(input, observed, $"机理输入 {input.Code}");
            value += model.Coefficients[input.Code] * observed;
        }
        if (!double.IsFinite(value))
            throw new ResearchAssetRuleException("机理模型输出不是有限数值。");
        EnsureRange(model.Output, value, "机理模型输出");
        return value;
    }

    private static double AddMechanismFeature(
        IDictionary<string, double> features,
        string code,
        double mechanism,
        MechanismFusionExecutionRequest request)
    {
        features[code] = mechanism;
        return RequireDataPrediction(request);
    }

    private static double RequireDataPrediction(MechanismFusionExecutionRequest request)
        => request.DataPrediction is { } value && double.IsFinite(value)
            ? value
            : throw new ResearchAssetRuleException("当前融合方式需要有效的数据模型预测值。");

    private static void EnsureRange(MechanismVariableDefinition definition, double value, string field)
    {
        if (definition.ValidMinimum is { } minimum && value < minimum ||
            definition.ValidMaximum is { } maximum && value > maximum)
        {
            throw new ResearchAssetRuleException($"{field} 超出机理模型适用范围。");
        }
    }

    private static bool Matches(
        IReadOnlyDictionary<string, string> selector,
        IReadOnlyDictionary<string, string> context)
        => selector.All(pair => context.TryGetValue(pair.Key, out var value) &&
                                string.Equals(value, pair.Value, StringComparison.OrdinalIgnoreCase));

    private static void ValidateModel(MechanismModelVersion value)
    {
        if (value.Version <= 0 || string.IsNullOrWhiteSpace(value.ModelId) ||
            string.IsNullOrWhiteSpace(value.Name) || string.IsNullOrWhiteSpace(value.ScientificBasis))
            throw new ResearchAssetRuleException("机理模型标识、名称、版本和科学说明不能为空。");
        if (!string.Equals(value.EquationKind, "affine", StringComparison.OrdinalIgnoreCase))
            throw new ResearchAssetRuleException("当前只允许可审计的 affine 机理方程。");
        if (value.Inputs.Count == 0 || value.Inputs.Select(static item => item.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != value.Inputs.Count)
            throw new ResearchAssetRuleException("机理模型必须包含不重复的输入变量。");
        foreach (var variable in value.Inputs.Append(value.Output))
        {
            if (string.IsNullOrWhiteSpace(variable.Code) || string.IsNullOrWhiteSpace(variable.Unit) ||
                variable.ValidMinimum > variable.ValidMaximum)
                throw new ResearchAssetRuleException("机理变量编码、单位或有效范围无效。");
        }
        var inputCodes = value.Inputs.Select(static item => item.Code.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        var coefficientCodes = value.Coefficients.Keys.Select(static item => item.Trim().ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        if (!inputCodes.SetEquals(coefficientCodes) ||
            !double.IsFinite(value.Intercept) ||
            value.Coefficients.Values.Any(static coefficient => !double.IsFinite(coefficient)))
            throw new ResearchAssetRuleException("机理模型系数必须与输入变量一一对应且为有限数值。");
    }

    private static void ValidateFusion(MechanismFusionDefinition value)
    {
        if (value.Version <= 0 || value.MechanismModelVersion <= 0 ||
            string.IsNullOrWhiteSpace(value.FusionId) ||
            string.IsNullOrWhiteSpace(value.Name) ||
            string.IsNullOrWhiteSpace(value.OutputCode) ||
            !MechanismFusionModes.IsValid(value.Mode))
            throw new ResearchAssetRuleException("机理融合定义无效。");
        if (!double.IsFinite(value.CalibrationScale) ||
            !double.IsFinite(value.CalibrationOffset) ||
            !double.IsFinite(value.PostProcessingGain) ||
            !double.IsFinite(value.MechanismReference) ||
            !double.IsFinite(value.MechanismWeight) ||
            value.MechanismWeight is < 0 or > 1)
            throw new ResearchAssetRuleException("机理融合参数无效。");
    }

    private static MechanismVariableDefinition NormalizeVariable(MechanismVariableDefinition value)
        => value with
        {
            Code = value.Code.Trim().ToLowerInvariant(),
            Unit = value.Unit.Trim()
        };

    private static IReadOnlyDictionary<string, string> NormalizeContext(
        IReadOnlyDictionary<string, string> context)
        => context.ToDictionary(
            static pair => pair.Key.Trim().ToLowerInvariant(),
            static pair => pair.Value.Trim(),
            StringComparer.Ordinal);

    private static string ModelHash(MechanismModelVersion value)
    {
        var canonical = new StringBuilder()
            .Append(value.ModelId).Append('|').Append(value.Version).Append('|')
            .Append(value.EquationKind).Append('|')
            .Append(value.Intercept.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(value.Output.Code).Append('|').Append(value.Output.Unit).Append('|')
            .Append(value.ScientificBasis);
        foreach (var input in value.Inputs.OrderBy(static item => item.Code))
        {
            canonical.Append('|').Append(input.Code).Append(':').Append(input.Unit).Append(':')
                .Append(input.ValidMinimum?.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                .Append(input.ValidMaximum?.ToString("R", CultureInfo.InvariantCulture)).Append(':')
                .Append(value.Coefficients[input.Code].ToString("R", CultureInfo.InvariantCulture));
        }
        return Hash(canonical.ToString());
    }

    private static string FusionHash(MechanismFusionDefinition value)
        => Hash(string.Join(
            "|",
            value.FusionId,
            value.Version,
            value.Mode,
            value.MechanismModelId,
            value.MechanismModelVersion,
            value.CalibrationScale.ToString("R", CultureInfo.InvariantCulture),
            value.CalibrationOffset.ToString("R", CultureInfo.InvariantCulture),
            value.PostProcessingGain.ToString("R", CultureInfo.InvariantCulture),
            value.MechanismReference.ToString("R", CultureInfo.InvariantCulture),
            value.MechanismWeight.ToString("R", CultureInfo.InvariantCulture),
            value.MechanismFeatureCode,
            value.OutputCode));

    private static string ExecutionHash(
        MechanismFusionDefinition fusion,
        MechanismModelVersion model,
        MechanismFusionExecutionRequest request,
        double mechanism,
        double? fused)
    {
        var canonical = new StringBuilder()
            .Append(fusion.ContentHash).Append('|').Append(model.ContentHash).Append('|')
            .Append(request.DataPrediction?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(mechanism.ToString("R", CultureInfo.InvariantCulture)).Append('|')
            .Append(fused?.ToString("R", CultureInfo.InvariantCulture));
        foreach (var pair in request.MechanismInputs.OrderBy(static pair => pair.Key))
            canonical.Append('|').Append(pair.Key).Append(':')
                .Append(pair.Value.ToString("R", CultureInfo.InvariantCulture));
        return Hash(canonical.ToString());
    }

    private async Task AuditAsync(
        string resourceType,
        string resourceId,
        string action,
        string userId,
        CancellationToken ct)
        => await store.AddAuditEntryAsync(new ResearchAssetAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ResourceType = resourceType,
            ResourceId = resourceId,
            Action = action,
            UserId = userId,
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);

    private static string NormalizeId(string value) => value.Trim().ToLowerInvariant();
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Hash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
