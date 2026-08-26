// 验证平台组件 DatasetQualityValidationRunner 的成功、拒绝和安全边界。

using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class DatasetQualityValidationRunnerTests
{
    [Fact]
    public async Task EvaluateAsync_AcceptsMeasuredDataset_WhenProvenanceAndStreamBatchAgree()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "execution,temperature,hardness\n" +
            string.Join("\n", Enumerable.Range(1, 10)
                .Select(index => $"{index % 2},{300 + index},{40 + index * 0.5}")));
        var manifest = Manifest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

        var report = await DatasetQualityValidationRunner.EvaluateAsync(
            new MemoryStream(bytes),
            "heat-treatment.csv",
            manifest,
            "test");

        Assert.Equal(DatasetQualityValidationStatuses.Passed, report.Status);
        Assert.True(report.ResearchClaimsAllowed);
        Assert.Equal(10, report.RowCount);
        Assert.Equal(2, report.ProcessExecutionCount);
        Assert.InRange(report.StreamBatchMaximumDifference, 0, 1e-10);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsDataset_WhenSourceHashDoesNotMatchManifest()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "execution,temperature,hardness\n" +
            string.Join("\n", Enumerable.Range(1, 10)
                .Select(index => $"{index},{300 + index},{40 + index}")));

        var report = await DatasetQualityValidationRunner.EvaluateAsync(
            new MemoryStream(bytes),
            "heat-treatment.csv",
            Manifest(new string('0', 64)),
            "test");

        Assert.Equal(DatasetQualityValidationStatuses.Rejected, report.Status);
        Assert.False(report.ResearchClaimsAllowed);
        Assert.Contains(report.Issues, issue => issue.Contains("SHA-256", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EvaluateAsync_RejectsUnjustifiedSignalRange()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "execution,temperature,hardness\n" +
            string.Join("\n", Enumerable.Range(1, 10)
                .Select(index => $"{index},{300 + index},{40 + index}")));
        var manifest = Manifest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()) with
        {
            ValidSignalRanges = new Dictionary<string, ScientificNumericRange>
            {
                ["temperature"] = new()
                {
                    Minimum = 500,
                    Maximum = 100,
                    Basis = ""
                }
            }
        };

        await Assert.ThrowsAsync<ResearchAssetRuleException>(() =>
            DatasetQualityValidationRunner.EvaluateAsync(
                new MemoryStream(bytes),
                "heat-treatment.csv",
                manifest,
                "test"));
    }

    [Fact]
    public async Task EvaluateAsync_RejectsCsv_WhenColumnCountExceedsSafetyLimit()
    {
        var headers = string.Join(",", Enumerable.Range(0, 257).Select(index => $"column-{index}"));
        var values = string.Join(",", Enumerable.Repeat("1", 257));
        var bytes = Encoding.UTF8.GetBytes($"{headers}\n{values}");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            DatasetQualityValidationRunner.EvaluateAsync(
                new MemoryStream(bytes),
                "too-wide.csv",
                Manifest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()),
                "test"));

        Assert.Contains("256", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsCsv_WhenCellExceedsSafetyLimit()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "execution,temperature,hardness\n" +
            $"1,{new string('1', 32 * 1024 + 1)},40");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            DatasetQualityValidationRunner.EvaluateAsync(
                new MemoryStream(bytes),
                "oversized-cell.csv",
                Manifest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()),
                "test"));

        Assert.Contains("32768", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EvaluateAsync_StreamsWorkbookRows_AfterArchiveBoundsAreChecked()
    {
        byte[] bytes;
        using (var workbook = new XLWorkbook())
        {
            var worksheet = workbook.AddWorksheet("measurements");
            worksheet.Cell(1, 1).Value = "execution";
            worksheet.Cell(1, 2).Value = "temperature";
            worksheet.Cell(1, 3).Value = "hardness";
            for (var row = 2; row <= 11; row++)
            {
                worksheet.Cell(row, 1).Value = row % 2;
                worksheet.Cell(row, 2).Value = 300 + row;
                worksheet.Cell(row, 3).Value = 40 + row;
            }
            using var content = new MemoryStream();
            workbook.SaveAs(content);
            bytes = content.ToArray();
        }

        var report = await DatasetQualityValidationRunner.EvaluateAsync(
            new MemoryStream(bytes),
            "measurements.xlsx",
            Manifest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()) with
            {
                SheetName = "measurements"
            },
            "test");

        Assert.Equal(DatasetQualityValidationStatuses.Passed, report.Status);
        Assert.Equal(10, report.RowCount);
        Assert.Equal(2, report.ProcessExecutionCount);
    }

    [Fact]
    public async Task EvaluateAsync_ParsesQuotedCsvFieldsAcrossInputBuffers()
    {
        var prefix = new string('x', 16 * 1024);
        var bytes = Encoding.UTF8.GetBytes(
            "execution,temperature,hardness\n" +
            string.Join("\n", Enumerable.Range(1, 10).Select(index =>
                $"\"{prefix},{index}\",\"{300 + index}\",\"{40 + index}\"")));

        var report = await DatasetQualityValidationRunner.EvaluateAsync(
            new MemoryStream(bytes),
            "quoted.csv",
            Manifest(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()),
            "test");

        Assert.Equal(DatasetQualityValidationStatuses.Passed, report.Status);
        Assert.Equal(10, report.RowCount);
        Assert.Equal(10, report.ProcessExecutionCount);
    }

    private static DatasetQualityValidationDatasetManifest Manifest(string expectedSha256)
        => new()
        {
            DatasetId = "test-heat-treatment",
            Industry = "metallurgy",
            Process = "aging",
            DataKind = "measured-experiment",
            IsMeasuredData = true,
            SourceUri = "https://example.org/dataset",
            License = "CC BY 4.0",
            Citation = "Example measured dataset",
            ExpectedSha256 = expectedSha256,
            ProcessExecutionColumn = "execution",
            SignalColumns = ["temperature"],
            OutcomeColumns = ["hardness"]
        };
}
