using System.Security.Cryptography;
using System.Text;
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
            "cycle,temperature,hardness\n" +
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
        Assert.Equal(2, report.CycleCount);
        Assert.InRange(report.StreamBatchMaximumDifference, 0, 1e-10);
        Assert.Empty(report.Issues);
    }

    [Fact]
    public async Task EvaluateAsync_RejectsDataset_WhenSourceHashDoesNotMatchManifest()
    {
        var bytes = Encoding.UTF8.GetBytes(
            "cycle,temperature,hardness\n" +
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
            "cycle,temperature,hardness\n" +
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
            CycleColumn = "cycle",
            SignalColumns = ["temperature"],
            OutcomeColumns = ["hardness"]
        };
}
