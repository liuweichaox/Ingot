using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>Produces an editable suggestion only. Persistence and review stay explicit human actions.</summary>
public sealed class MechanismClaimDraftService(
    IResearchAssetStore assets,
    IResearchProjectContextReader projects,
    IMechanismClaimDraftGenerator generator)
{
    public async Task<MechanismClaimVersion> GenerateAsync(
        Guid projectId,
        MechanismClaimDraftGenerationRequest request,
        string userId,
        CancellationToken ct = default)
    {
        var project = await projects.GetProjectAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("研发项目不存在。");
        var source = await assets.GetKnowledgeSourceAsync(request.SourceId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("知识来源不存在。");
        if (!source.ContextSelector.TryGetValue("research-project-id", out var boundProject) ||
            !Guid.TryParse(boundProject, out var boundProjectId) || boundProjectId != projectId)
            throw new ResearchAssetRuleException("知识来源不属于当前研发项目。");
        if (source.ExtractionStatus != "completed")
            throw new ResearchAssetRuleException("知识来源完成确定性提取后才能生成语义草稿。");
        var records = (await assets.ListKnowledgeRecordsAsync(source.SourceId, ct).ConfigureAwait(false))
            .Where(static value => !string.IsNullOrWhiteSpace(value.Content))
            .Take(80)
            .ToArray();
        if (records.Length == 0)
            throw new ResearchAssetRuleException("知识来源没有可供语义提取的片段。");
        var context = new Dictionary<string, string>(project.Context, StringComparer.OrdinalIgnoreCase)
        {
            ["project-code"] = project.Code,
            ["process"] = project.ProcessName
        };
        Add(context, "product", project.ProductName);
        Add(context, "material", project.MaterialName);
        Add(context, "site", project.SiteCode);
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
            ProjectName = project.Name,
            ProjectContext = context,
            Variables = project.Variables.Select(static value =>
                new MechanismDraftVariable(value.Code, value.Role, value.Unit)).ToArray(),
            SourceTitle = source.Title,
            SourceHash = source.Sha256,
            Fragments = fragments,
            Focus = string.IsNullOrWhiteSpace(request.Focus) ? null : request.Focus.Trim()
        }, ct).ConfigureAwait(false);
        ValidateGeneratedDraft(generated, project, context);
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
                ["projectId"] = projectId.ToString(),
                ["generatorModel"] = generated.GeneratorModel,
                ["persisted"] = "false"
            },
            CreatedAt = DateTimeOffset.UtcNow
        }, ct).ConfigureAwait(false);
        return new MechanismClaimVersion
        {
            ProjectId = projectId,
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

    private static void Add(IDictionary<string, string> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) values[key] = value;
    }

    private static void ValidateGeneratedDraft(
        GeneratedMechanismClaimDraft draft,
        ResearchProject project,
        IReadOnlyDictionary<string, string> context)
    {
        if (string.IsNullOrWhiteSpace(draft.Name) || draft.Name.Length > 240 ||
            string.IsNullOrWhiteSpace(draft.Statement) || draft.Statement.Length > 8000 ||
            string.IsNullOrWhiteSpace(draft.FalsificationCondition) || draft.FalsificationCondition.Length > 8000 ||
            !MechanismClaimTypes.All.Contains(draft.MechanismType?.Trim().ToLowerInvariant() ?? ""))
            throw new ResearchAssetRuleException("语义草稿缺少有效名称、类型、陈述或反证条件。");
        if (draft.Variables.Count is < 1 or > 100 || draft.Applicability.Count is < 1 or > 100 ||
            draft.Constraints.Count > 100 || draft.ForbiddenCombinations.Count > 100)
            throw new ResearchAssetRuleException("语义草稿的变量、适用范围或约束数量超出限制。");
        var variables = project.Variables.ToDictionary(static value => value.Code, StringComparer.Ordinal);
        foreach (var variable in draft.Variables)
        {
            if (!variables.TryGetValue(variable.VariableCode, out var projectVariable) ||
                !string.Equals(
                    MechanismKnowledgeService.NormalizeUnit(variable.Unit),
                    MechanismKnowledgeService.NormalizeUnit(projectVariable.Unit),
                    StringComparison.Ordinal))
                throw new ResearchAssetRuleException($"语义草稿引用了未知变量或错误单位：{variable.VariableCode}。");
        }
        foreach (var scope in draft.Applicability)
            if (!context.TryGetValue(scope.DimensionCode, out var value) ||
                !string.Equals(value, scope.DimensionValue, StringComparison.OrdinalIgnoreCase))
                throw new ResearchAssetRuleException("语义草稿适用范围不属于当前项目上下文。");
        foreach (var code in draft.Constraints.Select(static value => value.VariableCode)
            .Concat(draft.ForbiddenCombinations.SelectMany(static value => value.Factors)
                .Select(static value => value.VariableCode)))
            if (!variables.TryGetValue(code, out var variable) || variable.Role != ResearchVariableRoles.Control)
                throw new ResearchAssetRuleException($"语义草稿约束引用了非可控变量：{code}。");
    }
}
