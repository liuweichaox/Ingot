using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Ingot.Contracts.ResearchAssets;
using UglyToad.PdfPig;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed record ExtractedKnowledgeFragment
{
    public required string Category { get; init; }
    public required string Content { get; init; }
    public required KnowledgeCitation Citation { get; init; }
    public IReadOnlyDictionary<string, string> StructuredValues { get; init; } =
        new Dictionary<string, string>();
    public double? Confidence { get; init; }
}

public interface IKnowledgeContentExtractor
{
    string Version { get; }
    bool Supports(KnowledgeSource source);
    Task<IReadOnlyList<ExtractedKnowledgeFragment>> ExtractAsync(
        Stream content,
        KnowledgeSource source,
        CancellationToken ct = default);
}

public sealed class KnowledgeExtractionService(
    IResearchAssetStore store,
    IEnumerable<IKnowledgeContentExtractor> extractors)
{
    public const string PipelineVersion = "knowledge-extraction-v1";

    public async Task<KnowledgeSource> ExtractAsync(
        Guid sourceId,
        string userId,
        CancellationToken ct = default)
    {
        var source = await store.GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("知识来源不存在。");
        var extractor = extractors.FirstOrDefault(item => item.Supports(source))
            ?? throw new ResearchAssetRuleException($"暂不支持自动解析 {source.FileName}。");
        var pipelineVersion = $"{PipelineVersion}/{extractor.Version}";
        if (source.ExtractionStatus == "completed" &&
            string.Equals(source.ExtractorVersion, pipelineVersion, StringComparison.Ordinal))
            return source;
        var stream = await store.OpenKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false)
            ?? throw new ResearchAssetRuleException("知识来源文件不可用。");
        await using (stream.ConfigureAwait(false))
        {
            try
            {
                var fragments = await extractor.ExtractAsync(stream, source, ct).ConfigureAwait(false);
                if (fragments.Count == 0)
                    throw new InvalidDataException("文件中没有提取到可复核内容。");
                foreach (var fragment in fragments)
                {
                    ct.ThrowIfCancellationRequested();
                    await store.SaveKnowledgeRecordAsync(new KnowledgeRecord
                    {
                        RecordId = Guid.CreateVersion7(),
                        SourceId = source.SourceId,
                        Category = fragment.Category,
                        PageOrSheet = DisplayLocation(fragment.Citation),
                        Region = fragment.Citation.Region,
                        Content = fragment.Content,
                        StructuredValues = fragment.StructuredValues,
                        HumanReviewed = false,
                        CreatedBy = userId,
                        CreatedAt = DateTimeOffset.UtcNow,
                        ExtractionMethod = extractor.GetType().Name,
                        ExtractorVersion = extractor.Version,
                        ExtractionConfidence = fragment.Confidence,
                        Citation = fragment.Citation
                    }, ct).ConfigureAwait(false);
                }
                var updated = source with
                {
                    Status = KnowledgeSourceStatuses.Indexed,
                    ExtractionStatus = "completed",
                    ExtractionError = null,
                    ExtractorVersion = pipelineVersion
                };
                await store.SaveKnowledgeSourceMetadataAsync(updated, ct).ConfigureAwait(false);
                await AddAuditAsync(updated, "automatically-indexed", userId, fragments.Count, ct)
                    .ConfigureAwait(false);
                return updated;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var failed = source with
                {
                    ExtractionStatus = "failed",
                    ExtractionError = exception.Message[..Math.Min(exception.Message.Length, 1000)],
                    ExtractorVersion = pipelineVersion
                };
                await store.SaveKnowledgeSourceMetadataAsync(failed, ct).ConfigureAwait(false);
                await AddAuditAsync(failed, "automatic-index-failed", userId, 0, ct)
                    .ConfigureAwait(false);
                throw new ResearchAssetRuleException($"自动解析失败：{exception.Message}");
            }
        }
    }

    private Task AddAuditAsync(
        KnowledgeSource source,
        string action,
        string userId,
        int recordCount,
        CancellationToken ct)
        => store.AddAuditEntryAsync(new ResearchAssetAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ResourceType = "knowledge-source",
            ResourceId = source.SourceId.ToString(),
            Action = action,
            ToStatus = source.Status,
            UserId = userId,
            Details = new Dictionary<string, string>
            {
                ["recordCount"] = recordCount.ToString(),
                ["extractorVersion"] = source.ExtractorVersion ?? ""
            },
            CreatedAt = DateTimeOffset.UtcNow
        }, ct);

    private static string? DisplayLocation(KnowledgeCitation value)
        => value.PageNumber is { } page ? $"page:{page}" :
            value.SheetName is not null ? $"sheet:{value.SheetName}" :
            value.LocationKind;
}

public sealed class PdfKnowledgeExtractor : IKnowledgeContentExtractor
{
    public string Version => "pdfpig-0.1.14/page-text-v1";

    public bool Supports(KnowledgeSource source)
        => Path.GetExtension(source.FileName).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<IReadOnlyList<ExtractedKnowledgeFragment>> ExtractAsync(
        Stream content,
        KnowledgeSource source,
        CancellationToken ct = default)
    {
        using var document = PdfDocument.Open(content);
        var result = new List<ExtractedKnowledgeFragment>();
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            var text = NormalizeText(page.Text);
            foreach (var chunk in Chunk(text, 12000))
            {
                result.Add(new ExtractedKnowledgeFragment
                {
                    Category = "document-text",
                    Content = chunk,
                    Citation = Citation(
                        "pdf-page",
                        chunk,
                        page.Number,
                        null,
                        null,
                        $"0,0,{page.Width:R},{page.Height:R}")
                });
            }
        }
        return Task.FromResult<IReadOnlyList<ExtractedKnowledgeFragment>>(result);
    }

    private static string NormalizeText(string value)
        => string.Join(" ", value.Split(default(string[]), StringSplitOptions.RemoveEmptyEntries));

    internal static IEnumerable<string> Chunk(string value, int maximum)
    {
        for (var start = 0; start < value.Length; start += maximum)
            yield return value.Substring(start, Math.Min(maximum, value.Length - start));
    }

    internal static KnowledgeCitation Citation(
        string kind,
        string text,
        int? page,
        string? sheet,
        string? range,
        string? region)
        => new()
        {
            LocationKind = kind,
            PageNumber = page,
            SheetName = sheet,
            CellRange = range,
            Region = region,
            ContentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
                .ToLowerInvariant()
        };
}

public sealed class ExcelKnowledgeExtractor : IKnowledgeContentExtractor
{
    public string Version => "closedxml-0.105.0/used-range-v1";

    public bool Supports(KnowledgeSource source)
        => Path.GetExtension(source.FileName) is var extension &&
           (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase));

    public Task<IReadOnlyList<ExtractedKnowledgeFragment>> ExtractAsync(
        Stream content,
        KnowledgeSource source,
        CancellationToken ct = default)
    {
        using var workbook = new XLWorkbook(content);
        var result = new List<ExtractedKnowledgeFragment>();
        foreach (var worksheet in workbook.Worksheets)
        {
            var used = worksheet.RangeUsed();
            if (used is null)
                continue;
            const int rowsPerFragment = 100;
            var firstRow = used.RangeAddress.FirstAddress.RowNumber;
            var lastRow = used.RangeAddress.LastAddress.RowNumber;
            var firstColumn = used.RangeAddress.FirstAddress.ColumnNumber;
            var lastColumn = used.RangeAddress.LastAddress.ColumnNumber;
            for (var startRow = firstRow; startRow <= lastRow; startRow += rowsPerFragment)
            {
                ct.ThrowIfCancellationRequested();
                var endRow = Math.Min(lastRow, startRow + rowsPerFragment - 1);
                var range = worksheet.Range(startRow, firstColumn, endRow, lastColumn);
                var lines = range.Rows().Select(row =>
                    string.Join("\t", row.Cells().Select(cell => cell.GetFormattedString())));
                var text = string.Join(Environment.NewLine, lines).Trim();
                if (text.Length == 0)
                    continue;
                var address = range.RangeAddress.ToStringRelative();
                result.Add(new ExtractedKnowledgeFragment
                {
                    Category = "spreadsheet-range",
                    Content = text[..Math.Min(text.Length, 15000)],
                    StructuredValues = new Dictionary<string, string>
                    {
                        ["sheet"] = worksheet.Name,
                        ["range"] = address,
                        ["rowCount"] = (endRow - startRow + 1).ToString(),
                        ["columnCount"] = (lastColumn - firstColumn + 1).ToString()
                    },
                    Citation = PdfKnowledgeExtractor.Citation(
                        "excel-range",
                        text,
                        null,
                        worksheet.Name,
                        address,
                        null)
                });
            }
        }
        return Task.FromResult<IReadOnlyList<ExtractedKnowledgeFragment>>(result);
    }
}

public sealed class PlainTextKnowledgeExtractor : IKnowledgeContentExtractor
{
    public string Version => "plain-text-v1";

    public bool Supports(KnowledgeSource source)
        => Path.GetExtension(source.FileName) is var extension &&
           (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".csv", StringComparison.OrdinalIgnoreCase));

    public async Task<IReadOnlyList<ExtractedKnowledgeFragment>> ExtractAsync(
        Stream content,
        KnowledgeSource source,
        CancellationToken ct = default)
    {
        using var reader = new StreamReader(
            content,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        return PdfKnowledgeExtractor.Chunk(text, 12000)
            .Where(static chunk => !string.IsNullOrWhiteSpace(chunk))
            .Select(chunk => new ExtractedKnowledgeFragment
            {
                Category = Path.GetExtension(source.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                    ? "spreadsheet-text"
                    : "document-text",
                Content = chunk,
                Citation = PdfKnowledgeExtractor.Citation(
                    "text-offset",
                    chunk,
                    null,
                    null,
                    null,
                    null)
            }).ToArray();
    }
}

public sealed class ImageKnowledgeExtractor(HttpClient httpClient, IConfiguration configuration)
    : IKnowledgeContentExtractor
{
    public string Version => "azure-vision-read-2024-02-01-v1";

    public bool Supports(KnowledgeSource source)
        => Path.GetExtension(source.FileName) is var extension &&
           new[] { ".png", ".jpg", ".jpeg", ".webp", ".tif", ".tiff" }
               .Contains(extension, StringComparer.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<ExtractedKnowledgeFragment>> ExtractAsync(
        Stream content,
        KnowledgeSource source,
        CancellationToken ct = default)
    {
        var endpoint = configuration["KnowledgeExtraction:Vision:Endpoint"]?.TrimEnd('/');
        var key = configuration["KnowledgeExtraction:Vision:ApiKey"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("图片 OCR 未配置；请设置 KnowledgeExtraction:Vision:Endpoint 和 ApiKey。");
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{endpoint}/imageanalysis:analyze?api-version=2024-02-01&features=read");
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        request.Content = new StreamContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(source.MediaType) ? "application/octet-stream" : source.MediaType);
        using var response = await httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(responseStream, cancellationToken: ct)
            .ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("readResult", out var readResult) ||
            !readResult.TryGetProperty("blocks", out var blocks))
            return [];
        var result = new List<ExtractedKnowledgeFragment>();
        foreach (var block in blocks.EnumerateArray())
        {
            foreach (var line in block.GetProperty("lines").EnumerateArray())
            {
                var text = line.GetProperty("text").GetString()?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;
                var region = line.TryGetProperty("boundingPolygon", out var polygon)
                    ? polygon.GetRawText()
                    : null;
                result.Add(new ExtractedKnowledgeFragment
                {
                    Category = "image-ocr",
                    Content = text,
                    Confidence = MeanConfidence(line),
                    Citation = PdfKnowledgeExtractor.Citation(
                        "image-region",
                        text,
                        null,
                        null,
                        null,
                        region)
                });
            }
        }
        return result;
    }

    private static double? MeanConfidence(JsonElement line)
    {
        if (!line.TryGetProperty("words", out var words))
            return null;
        var values = words.EnumerateArray()
            .Where(static word => word.TryGetProperty("confidence", out _))
            .Select(static word => word.GetProperty("confidence").GetDouble())
            .ToArray();
        return values.Length == 0 ? null : values.Average();
    }
}
