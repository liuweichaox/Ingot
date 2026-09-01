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
            ["0007"] = "7950252a0b4beeb08f34e4aa723be8228289e93f3a812b80c319885872f8ade9",
            ["0008"] = "7cc3cf8e45b18080d5dc595805d905efac5702fc53bc3834715d03b3ebdd3130",
            ["0009"] = "3093625e88223a477f685b06516b831d62f19248aa74f91950662d42e265bd43",
            ["0010"] = "cec8d49b86a41924a4475b69b74fba86bbe69b5ad146b0062a333d4184ecaf5b",
            ["0011"] = "60130e9bc1bb8379bddb515ad8ba1664ec67438d37171fd847fa80ca0e90685f",
            ["0012"] = "5602c217d27cc0d21df812c54492e2fa1bbf0d4deb43f55bb4fe5b942ba9a427",
            ["0013"] = "2bd1756ca36b7d7fde0c08be27b89f4d9723dd07f6641a29cf6d613a6ca4f0f7",
            ["0014"] = "f6430252aed8b0e07b96d3bfb9962d85d4592cee380ae47ffb668ec4e23c9e54",
            ["0015"] = "0a64f6a61abd2ef22a9100539ef7d6df0b8a9fab39c9e708b96fee01a5dfdae5",
            ["0016"] = "927309f7440fb2cfed18d66ab14c018226f3258a4e7601714f1aad1da860d5ab"
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
