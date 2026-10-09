// 从同站点知识来源生成未持久化的机理声明草稿；草稿必须再经人工保存、审核和证据升级。
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

public sealed class MechanismClaimDraftService(
    IResearchAssetStore assets,
    RecipeKnowledgeScopeReader scopes,
    IMechanismClaimDraftGenerator generator)
{
    public async Task<MechanismClaimVersion> GenerateAsync(
        MechanismClaimDraftGenerationRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var source = await assets.GetKnowledgeSourceAsync(request.SourceId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("知识来源不存在。");
        var scope = await scopes.ReadAsync(source.SiteCode, request.ProcessSpecificationId, ct)
            .ConfigureAwait(false);
        if (source.ExtractionStatus != "completed")
            throw new ResearchAssetRuleException("知识来源完成确定性提取后才能生成语义草稿。");
        var records = (await assets.ListKnowledgeRecordsAsync(source.SourceId, ct).ConfigureAwait(false))
            .Where(static value => !string.IsNullOrWhiteSpace(value.Content))
            .Take(80)
            .ToArray();
        if (records.Length == 0)
            throw new ResearchAssetRuleException("知识来源没有可供语义提取的片段。");
        var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [MechanismKnowledgeService.SiteDimension] = scope.SiteCode,
            [MechanismKnowledgeService.ProcessSpecificationDimension] = scope.ProcessSpecificationId
        };
        var fragments = new List<MechanismDraftFragment>();
        var remainingCharacters = 60_000;
        foreach (var record in records)
        {
            if (remainingCharacters <= 0) break;
            var length = Math.Min(record.Content.Length, Math.Min(3000, remainingCharacters));
            fragments.Add(new MechanismDraftFragment(
                record.RecordId,
                record.Content[..length],
                record.Citation?.ContentHash ?? source.Sha256));
            remainingCharacters -= length;
        }
        var generated = await generator.GenerateAsync(new MechanismClaimDraftGenerationContext
        {
            ProcessSpecificationId = scope.ProcessSpecificationId,
            ScopeContext = context,
            Variables = scope.ControlParameters.Values
                .Select(static value => new MechanismDraftVariable(
                    value.Code, value.ChangeAllowed ? "control" : "process", value.Unit ?? ""))
                .Concat(scope.DataItems.Values.Select(static value =>
                    new MechanismDraftVariable(value.Code, value.Category, value.Unit ?? "")))
                .ToArray(),
            SourceTitle = source.Title,
            SourceHash = source.Sha256,
            Fragments = fragments,
            Focus = string.IsNullOrWhiteSpace(request.Focus) ? null : request.Focus.Trim()
        }, ct).ConfigureAwait(false);
        ValidateGeneratedDraft(generated, scope, context);
        var recordMap = records.ToDictionary(static value => value.RecordId);
        var evidence = generated.SupportingRecordIds.Distinct().Select(recordId =>
        {
            if (!recordMap.TryGetValue(recordId, out var record))
                throw new ResearchAssetRuleException("语义草稿引用了未提供给模型的知识片段。");
            return new MechanismClaimEvidence
            {
                EvidenceLinkId = Guid.CreateVersion7(),
                EvidenceKind = "knowledge-fragment",
                ReferenceId = record.RecordId.ToString(),
                ContentHash = record.Citation?.ContentHash ?? source.Sha256,
                Polarity = "supporting"
            };
        }).ToArray();
        if (evidence.Length == 0)
            throw new ResearchAssetRuleException("语义草稿必须引用至少一个原始知识片段。");
        await assets.AddAuditEntryAsync(new ResearchAssetAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ResourceType = "mechanism-claim-draft-suggestion",
            ResourceId = source.SourceId.ToString(),
            Action = "generated",
            UserId = userId,
            Details = new Dictionary<string, string>
            {
                ["siteCode"] = scope.SiteCode,
                ["processSpecificationId"] = scope.ProcessSpecificationId,
                ["generatorModel"] = generated.GeneratorModel,
                ["persisted"] = "false"
            },
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);
        return new MechanismClaimVersion
        {
            SiteCode = scope.SiteCode,
            ProcessSpecificationId = scope.ProcessSpecificationId,
            Name = generated.Name,
            MechanismType = generated.MechanismType,
            Statement = generated.Statement,
            ExpectedSignature = generated.ExpectedSignature,
            FalsificationCondition = generated.FalsificationCondition,
            Variables = generated.Variables,
            Applicability = generated.Applicability,
            Constraints = generated.Constraints,
            ForbiddenCombinations = generated.ForbiddenCombinations,
            Evidence = evidence,
            EvidenceLevel = "model-assisted-draft",
            CreatedBy = userId,
            ContentHash = "not-persisted"
        };
    }

    private static void ValidateGeneratedDraft(
        GeneratedMechanismClaimDraft draft,
        RecipeKnowledgeScope scope,
        IReadOnlyDictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Length > 240 ||
            string.IsNullOrWhiteSpace(draft.Statement) || draft.Statement.Length > 8000 ||
            string.IsNullOrWhiteSpace(draft.FalsificationCondition) || draft.FalsificationCondition.Length > 8000 ||
            !MechanismClaimTypes.All.Contains(draft.MechanismType?.Trim().ToLowerInvariant() ?? ""))
            throw new ResearchAssetRuleException("语义草稿缺少有效名称、类型、陈述或反证条件。");
        if (draft.Variables.Count is < 1 or > 100 || draft.Applicability.Count > 100 ||
            draft.Constraints.Count > 100 || draft.ForbiddenCombinations.Count > 100)
            throw new ResearchAssetRuleException("语义草稿的变量、适用范围或约束数量超出限制。");
        foreach (var variable in draft.Variables.Where(static value => value.VariableRole != "outcome"))
        {
            var code = variable.VariableCode.Trim().ToLowerInvariant();
            var unit = scope.ControlParameters.TryGetValue(code, out var parameter)
                ? parameter.Unit
                : scope.DataItems.TryGetValue(code, out var item)
                    ? item.Unit
                    : throw new ResearchAssetRuleException($"语义草稿引用了配方中不存在的变量：{variable.VariableCode}。");
            if (!string.IsNullOrWhiteSpace(unit) &&
                !string.Equals(
                    MechanismKnowledgeService.NormalizeUnit(variable.Unit),
                    MechanismKnowledgeService.NormalizeUnit(unit),
                    StringComparison.Ordinal))
                throw new ResearchAssetRuleException($"语义草稿变量单位与配方定义不一致：{variable.VariableCode}。");
        }
        foreach (var scopeValue in draft.Applicability)
            if (context.TryGetValue(scopeValue.DimensionCode, out var value) &&
                !string.Equals(value, scopeValue.DimensionValue, StringComparison.OrdinalIgnoreCase))
                throw new ResearchAssetRuleException("语义草稿适用范围不属于当前站点和配方。");
        foreach (var code in draft.Constraints.Select(static value => value.VariableCode)
            .Concat(draft.ForbiddenCombinations.SelectMany(static value => value.Factors)
                .Select(static value => value.VariableCode)))
            if (!scope.ControlParameters.TryGetValue(code.Trim().ToLowerInvariant(), out var parameter) ||
                !parameter.ChangeAllowed)
                throw new ResearchAssetRuleException($"语义草稿约束引用了不可调整的参数：{code}。");
    }
}
