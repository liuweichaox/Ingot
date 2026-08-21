namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class InspectionAttachmentOptions
{
    public string RootPath { get; init; } = "data/inspection-attachments";

    public string? ArchiveRootPath { get; init; }

    public long MaxFileBytes { get; init; } = 25 * 1024 * 1024;
}
