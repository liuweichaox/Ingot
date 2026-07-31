using System.Net;
using System.Text;
using ClosedXML.Excel;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class KnowledgeExtractionTests
{
    [Fact]
    public async Task ExcelExtractor_PreservesSheetAndCellRangeCitation()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Heat");
        sheet.Cell("A1").Value = "temperature";
        sheet.Cell("B1").Value = "hardness";
        sheet.Cell("A2").Value = 850;
        sheet.Cell("B2").Value = 612;
        await using var content = new MemoryStream();
        workbook.SaveAs(content);
        content.Position = 0;

        var fragments = await new ExcelKnowledgeExtractor().ExtractAsync(
            content,
            Source("heat-treatment.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));

        var fragment = Assert.Single(fragments);
        Assert.Equal("Heat", fragment.Citation.SheetName);
        Assert.Equal("A1:B2", fragment.Citation.CellRange);
        Assert.Equal(64, fragment.Citation.ContentHash.Length);
        Assert.Contains("612", fragment.Content);
    }

    [Fact]
    public async Task PdfExtractor_PreservesPageCitation()
    {
        await using var content = new MemoryStream(BuildSinglePagePdf("Ingot research evidence"));

        var fragments = await new PdfKnowledgeExtractor().ExtractAsync(
            content,
            Source("evidence.pdf", "application/pdf"));

        var fragment = Assert.Single(fragments);
        Assert.Equal(1, fragment.Citation.PageNumber);
        Assert.Contains("Ingot research evidence", fragment.Content);
        Assert.Equal(64, fragment.Citation.ContentHash.Length);
    }

    [Fact]
    public async Task ImageExtractor_MapsOcrTextRegionAndConfidence()
    {
        const string response =
            """
            {"readResult":{"blocks":[{"lines":[{"text":"设备温度 850 C","boundingPolygon":[{"x":1,"y":2},{"x":3,"y":2},{"x":3,"y":4},{"x":1,"y":4}],"words":[{"text":"设备温度","confidence":0.98},{"text":"850","confidence":0.96}]}]}]}}
            """;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["KnowledgeExtraction:Vision:Endpoint"] = "https://vision.example.test",
                ["KnowledgeExtraction:Vision:ApiKey"] = "test-key"
            }).Build();
        using var client = new HttpClient(new StubHandler(response));
        await using var content = new MemoryStream([1, 2, 3]);

        var fragments = await new ImageKnowledgeExtractor(client, configuration).ExtractAsync(
            content,
            Source("furnace.png", "image/png"));

        var fragment = Assert.Single(fragments);
        Assert.Equal("设备温度 850 C", fragment.Content);
        Assert.NotNull(fragment.Confidence);
        Assert.Equal(0.97, fragment.Confidence.Value, 12);
        Assert.Equal("image-region", fragment.Citation.LocationKind);
        Assert.Contains("\"x\":1", fragment.Citation.Region);
    }

    private static KnowledgeSource Source(string fileName, string mediaType)
        => new()
        {
            SourceId = Guid.CreateVersion7(),
            Title = fileName,
            StorageRef = fileName,
            Sha256 = new string('a', 64),
            MediaType = mediaType,
            FileName = fileName
        };

    private static byte[] BuildSinglePagePdf(string text)
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {text.Length + 31} >>\nstream\nBT /F1 12 Tf 30 100 Td ({text}) Tj ET\nendstream"
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n")
                .Append(objects[index]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 ").Append(objects.Length + 1).Append("\n")
            .Append("0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size ").Append(objects.Length + 1)
            .Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("api-version=2024-02-01", request.RequestUri!.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
