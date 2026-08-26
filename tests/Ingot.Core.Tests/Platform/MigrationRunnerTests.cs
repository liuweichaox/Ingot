// 验证平台组件 MigrationRunner 的成功、拒绝和安全边界。

using Ingot.Platform.Infrastructure.Migrations;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MigrationRunnerTests
{
    private static readonly IReadOnlyDictionary<string, string> CommittedChecksums =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["0001"] = "62d13b11ac9e7cfca3ccfa3f3d5c8687d5bd709537328c8ce1afe63d736c8ce1",
            ["0002"] = "208472536e44cca47c5e0104419d488a0c2abd4705f4092e7a4ad33bff9b6b08",
            ["0003"] = "17b4efb6995fb71a87af86caf29f53916665b32bf482a4de92df22097449add0",
            ["0004"] = "39df89fdf54f0dfb97266ad007d95e6c48666b3b1b3f659aa71e83de29bc1149",
            ["0005"] = "dbc9626c9b5063a1336b9a3107e63e039da1b4c94b01eae5c62c5e12df38b8ee",
            ["0006"] = "b9333aea970e8af21c37cd198e38b1246ea154e2c2607181c1a57f4f8257b33d",
            ["0007"] = "7950252a0b4beeb08f34e4aa723be8228289e93f3a812b80c319885872f8ade9"
        };

    [Fact]
    public void ComputeChecksum_NormalizesLineEndings()
    {
        var unix = MigrationRunner.ComputeChecksum("CREATE TABLE a();\nCREATE TABLE b();\n");
        var windows = MigrationRunner.ComputeChecksum("CREATE TABLE a();\r\nCREATE TABLE b();\r\n");
        Assert.Equal(unix, windows);
    }

    [Fact]
    public void ComputeChecksum_DetectsContentDrift()
    {
        Assert.NotEqual(
            MigrationRunner.ComputeChecksum("CREATE TABLE a();"),
            MigrationRunner.ComputeChecksum("CREATE TABLE a(); -- edited"));
    }

    [Theory]
    [InlineData("Ingot.Platform.Infrastructure.Migrations.sql.0001_baseline.sql", "0001", "baseline")]
    [InlineData("Ingot.Platform.Infrastructure.Migrations.sql.0002_problem_cases.sql", "0002", "problem_cases")]
    public void ParseResourceName_SplitsVersionAndName(string resource, string version, string name)
    {
        var parsed = MigrationRunner.ParseResourceName(resource);
        Assert.Equal(version, parsed.Version);
        Assert.Equal(name, parsed.Name);
    }

    [Fact]
    public void BaselineScript_IsEmbedded()
    {
        var names = typeof(MigrationRunner).Assembly.GetManifestResourceNames();
        Assert.Contains("Ingot.Platform.Infrastructure.Migrations.sql.0001_baseline.sql", names);
    }

    [Fact]
    public void EmbeddedMigrationScripts_MatchCommittedChecksums()
    {
        const string prefix = "Ingot.Platform.Infrastructure.Migrations.sql.";
        var assembly = typeof(MigrationRunner).Assembly;
        var actual = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal) &&
                           name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                name => MigrationRunner.ParseResourceName(name).Version,
                name =>
                {
                    using var stream = assembly.GetManifestResourceStream(name)!;
                    using var reader = new StreamReader(stream);
                    return MigrationRunner.ComputeChecksum(reader.ReadToEnd());
                },
                StringComparer.Ordinal);

        Assert.Equal(CommittedChecksums.Count, actual.Count);
        foreach (var expected in CommittedChecksums)
        {
            Assert.True(actual.TryGetValue(expected.Key, out var checksum),
                $"Migration {expected.Key} is not embedded.");
            Assert.Equal(expected.Value, checksum);
        }
    }

}
