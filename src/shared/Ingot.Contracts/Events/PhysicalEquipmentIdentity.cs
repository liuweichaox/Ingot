using Ingot.Domain.Events;

namespace Ingot.Contracts.Events;

/// <summary>Resolves the production equipment unit from explicit context, with legacy equipment-subject fallback.</summary>
public static class PhysicalEquipmentIdentity
{
    public static string Resolve(ProductionEvent? evt)
    {
        if (evt is null)
            return string.Empty;

        return Resolve(evt.Context, evt.Subject);
    }

    public static string Resolve(
        IReadOnlyDictionary<string, string>? context,
        ObjectRef? subject)
    {
        if (context is not null &&
            context.TryGetValue("equipment_id", out var configuredEquipmentId) &&
            !string.IsNullOrWhiteSpace(configuredEquipmentId))
        {
            return configuredEquipmentId.Trim();
        }

        return subject is not null &&
               string.Equals(subject.Type, "equipment", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(subject.Id)
            ? subject.Id.Trim()
            : string.Empty;
    }

    public static string Resolve(IEnumerable<ProductionEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var materialized = events as IReadOnlyCollection<ProductionEvent> ?? events.ToArray();
        foreach (var evt in materialized)
        {
            if (evt.Context.TryGetValue("equipment_id", out var configuredEquipmentId) &&
                !string.IsNullOrWhiteSpace(configuredEquipmentId))
            {
                return configuredEquipmentId.Trim();
            }
        }

        foreach (var evt in materialized)
        {
            var equipmentId = Resolve(null, evt.Subject);
            if (equipmentId.Length > 0)
                return equipmentId;
        }

        return string.Empty;
    }
}
