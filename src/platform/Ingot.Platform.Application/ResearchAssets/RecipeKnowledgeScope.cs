// 解析机理知识所属配方的参数字典；机理知识只能引用该配方已发布版本中存在的参数和采集项。
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.ResearchAssets;

public sealed record RecipeKnowledgeScope(
    string SiteCode,
    string ProcessSpecificationId,
    IReadOnlyDictionary<string, ControlParameterDefinition> ControlParameters,
    IReadOnlyDictionary<string, ProcessDataItemDefinition> DataItems);

public sealed class RecipeKnowledgeScopeReader(IProcessConfigurationStore configurations)
{
    public async Task<RecipeKnowledgeScope> ReadAsync(
        string? siteCode,
        string? processSpecificationId,
        CancellationToken ct = default)
    {
        var site = siteCode?.Trim();
        var specificationId = processSpecificationId?.Trim();
        if (string.IsNullOrEmpty(site) || site.Length > 120)
            throw new ResearchAssetRuleException("机理知识必须指定站点。");
        if (string.IsNullOrEmpty(specificationId) || specificationId.Length > 120)
            throw new ResearchAssetRuleException("机理知识必须指定配方。");
        var specification = (await configurations.ListProcessSpecificationsAsync(ct).ConfigureAwait(false))
            .Where(value => string.Equals(value.ProcessSpecificationId, specificationId, StringComparison.Ordinal) &&
                value.Status != ConfigurationStatuses.Draft)
            .MaxBy(static value => value.Version)
            ?? throw new ResearchAssetRuleException($"配方 {specificationId} 没有可用的已发布版本。");
        var model = await configurations.GetDataModelAsync(
                specification.DataModelId, specification.DataModelVersion, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException($"配方 {specificationId} 引用的数据模型不存在。");
        return new RecipeKnowledgeScope(
            site,
            specificationId,
            model.ControlParameters.ToDictionary(
                static value => value.Code.Trim().ToLowerInvariant(), StringComparer.Ordinal),
            model.Acquisition.DataItems.ToDictionary(
                static value => value.Code.Trim().ToLowerInvariant(), StringComparer.Ordinal));
    }
}
