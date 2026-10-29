// Interpret capture completeness consistently so a connector restart cannot fabricate a complete experiment.
using System.Text.Json;
using Ingot.Domain.Events;

namespace Ingot.Contracts.Events;

public static class ProcessExecutionLifecycle
{
    public static bool IsComplete(ProductionEvent? started, ProductionEvent? completed)
    {
        if (started is null || completed is null || completed.OccurredAt < started.OccurredAt)
            return false;

        // A present capture marker means the start boundary was not fully observed; unknown markers fail closed.
        if (started.Data.ContainsKey("lifecycleCaptureStatus"))
            return false;
        if (!completed.Data.TryGetValue("completionStatus", out var status))
            return true; // Preserve legacy events which predate explicit completion markers.

        return status switch
        {
            string text => text == "completed",
            JsonElement { ValueKind: JsonValueKind.String } json => json.GetString() == "completed",
            _ => false
        };
    }
}
