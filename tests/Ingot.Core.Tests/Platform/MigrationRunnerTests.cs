using Ingot.Platform.Infrastructure.Migrations;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MigrationRunnerTests
{
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
    public void Legacy_adoption_preserves_incompatible_cycle_tables_before_baseline()
    {
        Assert.Contains("cycle_phases_legacy_v1", MigrationRunner.LegacySchemaAdoptionSql);
        Assert.Contains("cycle_features_legacy_v1", MigrationRunner.LegacySchemaAdoptionSql);
        Assert.Contains("column_name = 'algorithm_version'", MigrationRunner.LegacySchemaAdoptionSql);
        Assert.Contains("column_name = 'signal_code'", MigrationRunner.LegacySchemaAdoptionSql);
        Assert.DoesNotContain("DROP TABLE", MigrationRunner.LegacySchemaAdoptionSql);
    }
}
