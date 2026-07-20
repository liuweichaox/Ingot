namespace Ingot.Contracts.Inspections;

/// <summary>
///     人工或半自动检测提交。它是检测结果事实，不是生产事件，也不代表 QMS 放行。
/// </summary>
public sealed record CreateInspectionRecordRequest
{
    /// <summary>由提交端生成的 UUIDv7，用于离线重试和幂等提交。</summary>
    public required Guid RecordId { get; init; }

    /// <summary>被检测工件、样件或批次内单件的稳定标识。</summary>
    public required string WorkpieceId { get; init; }

    /// <summary>本次检测所关联的加工运行稳定标识。</summary>
    public required string OperationRunId { get; init; }

    public required string DefinitionCode { get; init; }

    public int DefinitionVersion { get; init; } = 1;

    public required DateTimeOffset MeasuredAt { get; init; }

    /// <summary>提交端将记录固化到本地的时间，支持断网后补传。</summary>
    public required DateTimeOffset RecordedAt { get; init; }

    /// <summary>PASS、FAIL 或 INCONCLUSIVE；只表示检测结果。</summary>
    public required string Outcome { get; init; }

    /// <summary>人员或受控工位身份；是否已验证由服务端决定。</summary>
    public required string SubmittedBy { get; init; }

    public InspectionInstrumentRef? Instrument { get; init; }

    public IReadOnlyList<InspectionCharacteristicResult> Measurements { get; init; } = [];

    public IReadOnlyList<InspectionEvidenceRef> Evidence { get; init; } = [];

    public string? Notes { get; init; }
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

    /// <summary>数值量纲；无量纲值使用 UCUM 的 1。</summary>
    public string? Unit { get; init; }

    public decimal? LowerLimit { get; init; }

    public decimal? UpperLimit { get; init; }
}

/// <summary>已经进入受控证据暂存区的文件引用；API 不会主动抓取该地址。</summary>
public sealed record InspectionEvidenceRef
{
    public required Guid EvidenceId { get; init; }

    public required string StorageRef { get; init; }

    public required string Sha256 { get; init; }

    public required string MediaType { get; init; }

    public required string FileName { get; init; }

    public required long SizeBytes { get; init; }
}

public sealed record InspectionRecord
{
    public required Guid RecordId { get; init; }
    public required string WorkpieceId { get; init; }
    public required string OperationRunId { get; init; }
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
    public IReadOnlyList<InspectionEvidenceRef> Evidence { get; init; } = [];
    public string? Notes { get; init; }
}

public sealed record InspectionRecordQuery
{
    public string? WorkpieceId { get; init; }
    public string? OperationRunId { get; init; }
    public string? DefinitionCode { get; init; }
    public string? Outcome { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public int Limit { get; init; } = 100;
}

