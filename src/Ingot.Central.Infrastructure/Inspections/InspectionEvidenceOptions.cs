namespace Ingot.Central.Infrastructure.Inspections;

public sealed class InspectionEvidenceOptions
{
    public string RootPath { get; init; } = "data/inspection-evidence";

    public long MaxFileBytes { get; init; } = 25 * 1024 * 1024;
}

