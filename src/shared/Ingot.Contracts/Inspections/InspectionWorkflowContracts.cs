namespace Ingot.Contracts.Inspections;

public static class InspectionReviewDecisions
{
    public const string Confirmed = "CONFIRMED";
    public const string Rejected = "REJECTED";
    public const string ReinspectionRequired = "REINSPECTION_REQUIRED";

    public static bool IsValid(string? value)
        => value is Confirmed or Rejected or ReinspectionRequired;
}

public sealed record CreateInspectionReviewRequest
{
    public required Guid ReviewId { get; init; }

    public required Guid InspectionRecordId { get; init; }

    public required string Decision { get; init; }

    public string? Notes { get; init; }
}

public sealed record InspectionReview
{
    public required Guid ReviewId { get; init; }

    public required Guid InspectionRecordId { get; init; }

    public required string OperationRunId { get; init; }

    public required string Decision { get; init; }

    public required DateTimeOffset ReviewedAt { get; init; }

    public required string ReviewedBy { get; init; }

    public string? Notes { get; init; }
}

public sealed record InspectionAuditEntry
{
    public required long AuditId { get; init; }

    public Guid? InspectionRecordId { get; init; }

    public Guid? AttachmentId { get; init; }

    public required string Action { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string Actor { get; init; }

    public string? Detail { get; init; }
}

public sealed record InspectionTask
{
    public string ScopeType { get; init; } = "production-cycle";
    public required string OperationRunId { get; init; }

    public required string WorkpieceId { get; init; }

    public required string MachineId { get; init; }

    public required string ProductSeries { get; init; }

    public required string InspectionPlanId { get; init; }

    public int InspectionPlanVersion { get; init; }

    public required string InspectionPlanName { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }

    public required string Status { get; init; }

    public IReadOnlyList<string> RequiredDefinitionCodes { get; init; } = [];

    public IReadOnlyList<InspectionPlanItem> RequiredInspections { get; init; } = [];

    public IReadOnlyList<string> CompletedDefinitionCodes { get; init; } = [];

    public IReadOnlyList<string> MissingDefinitionCodes { get; init; } = [];

    public Guid? VisualInspectionRecordId { get; init; }

    public string? VisualReviewDecision { get; init; }
}

public sealed record InspectionScope
{
    public required string ScopeId { get; init; }
    public string ScopeType { get; init; } = "analysis-window";
    public required string WorkpieceId { get; init; }
    public string SubjectType { get; init; } = "equipment";
    public required string SubjectId { get; init; }
    public required string ProductSeries { get; init; }
    public required string InspectionPlanId { get; init; }
    public int InspectionPlanVersion { get; init; } = 1;
    public required DateTimeOffset From { get; init; }
    public required DateTimeOffset To { get; init; }
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
    public DateTimeOffset CreatedAt { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

public sealed record InspectionTaskSummary
{
    public int Total { get; init; }
    public int Pending { get; init; }
    public int InProgress { get; init; }
    public int ReviewPending { get; init; }
    public int Completed { get; init; }
    public int ActionRequired => Pending + InProgress + ReviewPending;
}

public sealed record InspectionTaskPage
{
    public IReadOnlyList<InspectionTask> Data { get; init; } = [];
    public int Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
}
