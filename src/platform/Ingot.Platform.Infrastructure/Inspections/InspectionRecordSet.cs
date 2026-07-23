using Ingot.Contracts.Inspections;

namespace Ingot.Platform.Infrastructure.Inspections;

internal static class InspectionRecordSet
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
}
