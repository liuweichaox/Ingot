// 汇总检验领域的只读查询并保持分页与站点边界。
using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

/// <summary>提供检验主数据、事实、附件、复核和审计的查询入口。</summary>
public sealed class InspectionQueries(
    IInspectionMasterDataStore masterData,
    IInspectionRecordStore records,
    IInspectionAttachmentStore attachments,
    IInspectionReviewStore reviews)
{
    public Task<IReadOnlyList<InspectionDefinition>> ListDefinitionsAsync(CancellationToken ct = default)
        => masterData.ListInspectionDefinitionsAsync(ct);

    public Task<InspectionDefinition?> GetDefinitionAsync(string code, int version, CancellationToken ct = default)
        => masterData.GetInspectionDefinitionAsync(Normalize(code), version, ct);

    public Task<IReadOnlyList<InspectionPlan>> ListPlansAsync(CancellationToken ct = default)
        => masterData.ListInspectionPlansAsync(ct);

    public Task<InspectionPlan?> GetPlanAsync(string planId, int version, CancellationToken ct = default)
        => masterData.GetInspectionPlanAsync(Normalize(planId), version, ct);

    public Task<InspectionRecord?> GetRecordAsync(Guid recordId, CancellationToken ct = default)
        => records.GetAsync(recordId, ct);

    public async Task<InspectionCommandResult<InspectionRecordPage>> QueryRecordsAsync(
        InspectionRecordQuery query,
        CancellationToken ct = default)
    {
        if (!InspectionRecordValidator.TryValidateQuery(query, out var error))
            return InspectionCommandResult<InspectionRecordPage>.Invalid(error);

        return InspectionCommandResult<InspectionRecordPage>.Success(
            await records.QueryPageAsync(query, ct).ConfigureAwait(false));
    }

    public Task<IReadOnlyList<InspectionScope>> ListScopesAsync(
        CancellationToken ct = default,
        string? siteId = null)
        => records.ListScopesAsync(siteId, ct);

    public Task<InspectionScope?> GetScopeAsync(string scopeId, CancellationToken ct = default)
        => records.GetScopeAsync(scopeId.Trim(), ct);

    public Task<InspectionAttachment?> GetAttachmentAsync(Guid attachmentId, CancellationToken ct = default)
        => attachments.GetAsync(attachmentId, ct);

    public Task<InspectionReview?> GetReviewAsync(Guid reviewId, CancellationToken ct = default)
        => reviews.GetAsync(reviewId, ct);

    public Task<IReadOnlyList<InspectionReview>> QueryReviewsAsync(
        Guid? inspectionRecordId,
        string? executionId,
        int limit,
        CancellationToken ct = default)
        => reviews.QueryAsync(inspectionRecordId, executionId, limit, ct);

    public Task<IReadOnlyList<InspectionAuditEntry>> QueryAuditAsync(
        Guid? inspectionRecordId,
        Guid? attachmentId,
        int limit,
        CancellationToken ct = default)
        => reviews.QueryAuditAsync(inspectionRecordId, attachmentId, limit, ct);

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
