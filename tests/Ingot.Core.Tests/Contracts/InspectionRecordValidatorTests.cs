using Ingot.Contracts.Inspections;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class InspectionRecordValidatorTests
{
    [Fact]
    public void TryValidate_ShouldNormalizeOfflineInspectionFact()
    {
        var request = CreateRequest() with
        {
            WorkpieceId = " PART-2026-0001 ",
            DefinitionCode = " VISUAL.HOUSING ",
            Outcome = " pass ",
            SubmittedBy = " OPERATOR-001 ",
            Measurements =
            [
                new InspectionCharacteristicResult
                {
                    CharacteristicCode = " surface.grade ",
                    Outcome = "pass",
                    TextValue = " A "
                }
            ]
        };

        Assert.True(InspectionRecordValidator.TryValidate(request, out var normalized, out _));
        Assert.Equal("PART-2026-0001", normalized!.WorkpieceId);
        Assert.Equal("visual.housing", normalized.DefinitionCode);
        Assert.Equal("PASS", normalized.Outcome);
        Assert.Equal("OPERATOR-001", normalized.SubmittedBy);
        Assert.Equal("surface.grade", normalized.Measurements[0].CharacteristicCode);
        Assert.Equal("A", normalized.Measurements[0].TextValue);
    }

    [Fact]
    public void TryValidate_ShouldRequireStructuredResultOrAttachments()
    {
        var request = CreateRequest() with { Measurements = [], Attachments = [] };

        Assert.False(InspectionRecordValidator.TryValidate(request, out _, out var error));
        Assert.Contains("至少包含", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldRejectNonV7IdAndDuplicateCharacteristics()
    {
        Assert.False(InspectionRecordValidator.TryValidate(
            CreateRequest() with { RecordId = Guid.NewGuid() },
            out _,
            out var idError));
        Assert.Contains("UUIDv7", idError, StringComparison.Ordinal);

        var measurement = CreateMeasurement();
        Assert.False(InspectionRecordValidator.TryValidate(
            CreateRequest() with
            {
                Measurements = [measurement, measurement with { CharacteristicCode = "LENGTH.MM" }]
            },
            out _,
            out var duplicateError));
        Assert.Contains("检测项目不能重复", duplicateError, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldRequireUnitForNumericMeasurement()
    {
        var request = CreateRequest() with
        {
            Measurements = [CreateMeasurement() with { Unit = null }]
        };

        Assert.False(InspectionRecordValidator.TryValidate(request, out _, out var error));
        Assert.Contains("Unit", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidateQuery_ShouldRejectInvalidWindowAndLimit()
    {
        Assert.False(InspectionRecordValidator.TryValidateQuery(
            new InspectionRecordQuery
            {
                From = DateTimeOffset.UtcNow,
                To = DateTimeOffset.UtcNow.AddMinutes(-1)
            },
            out var windowError));
        Assert.Contains("From", windowError, StringComparison.Ordinal);

        Assert.False(InspectionRecordValidator.TryValidateQuery(
            new InspectionRecordQuery { Limit = 501 },
            out var limitError));
        Assert.Contains("Limit", limitError, StringComparison.Ordinal);

        Assert.False(InspectionRecordValidator.TryValidateQuery(
            new InspectionRecordQuery { Offset = -1 },
            out var offsetError));
        Assert.Contains("Offset", offsetError, StringComparison.Ordinal);
    }

    [Fact]
    public void TryValidate_ShouldRequireReasonForImmutableCorrection()
    {
        var originalId = Guid.CreateVersion7();
        Assert.False(InspectionRecordValidator.TryValidate(
            CreateRequest() with { SupersedesRecordId = originalId },
            out _,
            out var error));
        Assert.Contains("更正原因", error, StringComparison.Ordinal);

        Assert.True(InspectionRecordValidator.TryValidate(
            CreateRequest() with { SupersedesRecordId = originalId, CorrectionReason = " 量具读数录入错误 " },
            out var normalized,
            out _));
        Assert.Equal("量具读数录入错误", normalized!.CorrectionReason);
    }

    private static CreateInspectionRecordRequest CreateRequest() => new()
    {
        RecordId = Guid.CreateVersion7(),
        WorkpieceId = "PART-2026-0001",
        OperationRunId = "RUN-2026-0001",
        DefinitionCode = "dimensional.final",
        MeasuredAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        RecordedAt = DateTimeOffset.UtcNow,
        Outcome = "PASS",
        SubmittedBy = "OPERATOR-001",
        Measurements = [CreateMeasurement()]
    };

    private static InspectionCharacteristicResult CreateMeasurement() => new()
    {
        CharacteristicCode = "length.mm",
        Outcome = "PASS",
        NumericValue = 10.02m,
        Unit = "mm",
        LowerLimit = 9.95m,
        UpperLimit = 10.05m
    };
}

