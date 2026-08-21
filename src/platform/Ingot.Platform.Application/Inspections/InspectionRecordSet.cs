
using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Application.Inspections;

public static class InspectionRecordSet
{
    public static IReadOnlyList<InspectionRecord> Effective(IEnumerable<InspectionRecord> records)
    {
        var values = records.ToArray();
        var superseded = values
            .Where(static record => record.SupersedesRecordId.HasValue)
            .Select(static record => record.SupersedesRecordId!.Value)
            .ToHashSet();
        return values.Where(record => !superseded.Contains(record.RecordId)).ToArray();
    }

    public static IReadOnlyList<InspectionRecord> AnalysisEligible(
        IEnumerable<InspectionRecord> records,
        InspectionPlan? plan,
        IReadOnlyDictionary<Guid, InspectionReview> latestReviews)
    {
        if (plan is null || plan.Status != InspectionPlanStatuses.Published)
            return [];
        var items = plan.Items.ToDictionary(
            static item => (item.DefinitionCode, item.DefinitionVersion));
        return Effective(records).Where(record =>
        {
            if (!record.SubmitterVerified ||
                !items.TryGetValue((record.DefinitionCode, record.DefinitionVersion), out var item))
            {
                return false;
            }
            if (!latestReviews.TryGetValue(record.RecordId, out var review))
                return !item.RequiresReview;
            return review.Decision == InspectionReviewDecisions.Confirmed &&
                   !string.Equals(review.ReviewedBy, record.SubmittedBy, StringComparison.Ordinal);
        }).ToArray();
    }
}
