namespace Ingot.Platform.Infrastructure.Inspections;

public sealed class InspectionAttachmentOptions
{
    public string RootPath { get; init; } = "data/inspection-attachments";

    /// <summary>
    /// Optional second, independently mounted write-once archive. Production
    /// configuration requires this path so original evidence survives loss of
    /// the serving volume.
    /// </summary>
    public string? ArchiveRootPath { get; init; }

    public long MaxFileBytes { get; init; } = 25 * 1024 * 1024;
}
