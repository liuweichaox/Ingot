// 通过扩展名和文件签名共同限制可保存的检验附件格式。
namespace Ingot.Platform.Application.Inspections;

public static class InspectionAttachmentPolicy
{
    private static readonly IReadOnlyDictionary<string, string> MediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".tif"] = "image/tiff",
            [".tiff"] = "image/tiff",
            [".pdf"] = "application/pdf"
        };

    public static string DetectMediaType(string fileName, ReadOnlySpan<byte> prefix)
    {
        var extension = Path.GetExtension(fileName);
        if (!MediaTypes.TryGetValue(extension, out var expected))
            throw new InvalidDataException("检验附件仅支持 PNG、JPEG、TIFF 或 PDF 文件。");

        var detected = prefix switch
        {
            _ when prefix.StartsWith(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }) => "image/png",
            _ when prefix.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) => "image/jpeg",
            _ when prefix.StartsWith(new byte[] { 0x49, 0x49, 0x2a, 0x00 }) ||
                   prefix.StartsWith(new byte[] { 0x4d, 0x4d, 0x00, 0x2a }) => "image/tiff",
            _ when prefix.StartsWith("%PDF-"u8) => "application/pdf",
            _ => null
        };
        if (detected is null || !string.Equals(detected, expected, StringComparison.Ordinal))
            throw new InvalidDataException("检验附件的扩展名与文件内容不一致，或文件格式不受支持。");
        return detected;
    }
}
