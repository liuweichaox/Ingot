namespace Ingot.Contracts.Inspections;

public sealed record CreateInspectionRecordRequest
{

    public required Guid RecordId { get; init; }

    public string? OutputItemId { get; init; }

    public required string ExecutionId { get; init; }

    public required string DefinitionCode { get; init; }

    public int DefinitionVersion { get; init; } = 1;

    public required DateTimeOffset MeasuredAt { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public required string Outcome { get; init; }

    public required string SubmittedBy { get; init; }

    public InspectionInstrumentRef? Instrument { get; init; }

    public IReadOnlyList<InspectionCharacteristicResult> Measurements { get; init; } = [];

    public IReadOnlyList<InspectionAttachment> Attachments { get; init; } = [];

    public string? Notes { get; init; }

    public Guid? SupersedesRecordId { get; init; }

    public string? CorrectionReason { get; init; }
}

public sealed record InspectionInstrumentRef
{
    public required string InstrumentId { get; init; }

    public string? Model { get; init; }

    public string? CalibrationRef { get; init; }

    public DateTimeOffset? CalibrationValidUntil { get; init; }
}

public sealed record InspectionCharacteristicResult
{
    public required string CharacteristicCode { get; init; }

    public required string Outcome { get; init; }

    public decimal? NumericValue { get; init; }

    public string? TextValue { get; init; }

    public string? Unit { get; init; }

    public decimal? LowerLimit { get; init; }

    public decimal? UpperLimit { get; init; }
}

public sealed record InspectionAttachment
{
    public required Guid AttachmentId { get; init; }

    public required string StorageRef { get; init; }

    public required string Sha256 { get; init; }

    public required string MediaType { get; init; }

    public required string FileName { get; init; }

    public required long SizeBytes { get; init; }
}

public sealed record InspectionRecord
{
    public required Guid RecordId { get; init; }
    public string? OutputItemId { get; init; }
    public required string ExecutionId { get; init; }
    public required string DefinitionCode { get; init; }
    public required int DefinitionVersion { get; init; }
    public required DateTimeOffset MeasuredAt { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
    public required DateTimeOffset IngestedAt { get; init; }
    public required string Outcome { get; init; }
    public required string SubmittedBy { get; init; }
    public required bool SubmitterVerified { get; init; }
    public InspectionInstrumentRef? Instrument { get; init; }
    public IReadOnlyList<InspectionCharacteristicResult> Measurements { get; init; } = [];
    public IReadOnlyList<InspectionAttachment> Attachments { get; init; } = [];
    public string? Notes { get; init; }
    public Guid? SupersedesRecordId { get; init; }
    public string? CorrectionReason { get; init; }
}

public sealed record InspectionRecordQuery
{
    public string? OutputItemId { get; init; }
    public string? ExecutionId { get; init; }
    public string? DefinitionCode { get; init; }
    public string? Outcome { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 100;
    public int Offset { get; init; }
}

public sealed record InspectionRecordPage
{
    public IReadOnlyList<InspectionRecord> Data { get; init; } = [];
    public int Total { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; }
}
