using Ingot.Platform.Infrastructure.Migrations;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MigrationRunnerTests
{
    private static readonly IReadOnlyDictionary<string, string> CommittedChecksums =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["0001"] = "d713f244197b72bd43571a643ff10a52316c6160c3df60473c0482133f51e125"
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
