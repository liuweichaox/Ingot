using Ingot.Platform.Infrastructure.Migrations;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class MigrationRunnerTests
{
    private static readonly IReadOnlyDictionary<string, string> CommittedChecksums =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["0001"] = "e26969bca69b3a5bdd403d5c4eefc650674736a19b26764f926ba4a8b06b35a9",
            ["0002"] = "3c608549298cde0a6ae7ee1ee67b8e47d7653e3e320d614e7ee581e899c01939",
            ["0003"] = "6a658982aee074e918783dbe7a7bff6f7c07738afb99f4ca112184e6c05f96bb",
            ["0004"] = "695b0d926da98b1b75706cb40464106c9a8beb1a140232d6f330699647de84ab",
            ["0005"] = "43cea28112e20173b36e7e250c7e2a95487e01105cd739b2795b686a42051a6f",
            ["0006"] = "5d840f28f57b44d370133e9828efede36964e5e59c6cdeefd65cf4a62c985703",
            ["0007"] = "9311267c1d4e64ac808155616d93c8a9876f94519b06c5f2c57395a218048387",
            ["0008"] = "7859c19cea2e04f997aef0bcfa6390c740a5be34ef950bef8bc54080d7ca6e10",
            ["0009"] = "5c768cb0515f2ec4f989b000696555d4738bf51114b92b09290eaf95f6f7451a",
            ["0010"] = "09ce983cddb0a734be2ca18df14686f726f64347e2d17df9070f3ec8d04b3447",
            ["0011"] = "4741c548d379fd18616c69435088f194bf5e1505edd94979674597a398abcdeb",
            ["0012"] = "909f715e68779fbd39fb30b0d2b6d8f03ea04c1627796a72cfba0740740ee1dc",
            ["0013"] = "4d84501a6ae1b6c061cd01bc2ab6a7e69c741e199971471e0316e74c6cff0f9d",
            ["0014"] = "ed1b0dba02cce0081696ee49b323741d3d8bf2c429b933685827bb27c756565c",
            ["0015"] = "16c0b5ac0fc5f42dd1c34909ffc40d11d34f19f78fb2e7f59b9df8d39f5b3ef5",
            ["0016"] = "5c00d4d34fd425b3c1943652ef41b7e8dcc02fce181e8b514c5d60a20ab2bf8e",
            ["0017"] = "ff16328a947bb51a39866bb31f3b722fc7524998b96da30b310797ff8c0a5a4e",
            ["0018"] = "d55876a4986369c3cdd9e411416b633a8f9bf5819c1cd8a4c59a8ea545281c43",
            ["0019"] = "414e6b9dd287913733a9c36ca58881869d053e03301a17aecb3e8fcfcbfd0268",
            ["0020"] = "eebb88e9efcf8d53bd75b99d5c73793ad3b0a69eb0e5a4c5f0919e27edd45361",
            ["0021"] = "e03ae279086a303681411d8534fa7a80061bf53997b41ed3536a276f41ce6367",
            ["0022"] = "90fad9cf3c02154e584c2ec5b7ab250184a2525cbaf726dc7df264463b8ceb94",
            ["0023"] = "7188d64c40c6026386295125d4fdc998786776569e93e77f95a823707ecdbc04",
            ["0024"] = "3b2347a70dc0e77d7fcf55e29b029228628fd995eda059c0b27c72a4238baf6b",
            ["0025"] = "8156358a3b273501a26720353f18a58ab4ad7c70c5fef8bf724df4333c855b9c",
            ["0026"] = "c5a2940312049b4893eed2397b45bcf669eb33e115dda0f783be2f92f279b29d",
            ["0027"] = "cf9fba60db986d5ab0c14238f61cb6136d99a756b4dc4dec74ca415ec0d2ec6f",
            ["0028"] = "2a6af3ef3cf362820ab44a11a6cac2f41f8b7bed8f2ea6c3fd4f54653d1b7f86",
            ["0029"] = "a2f0b723ed2d07af673bbcacfabf763ba47b1516af1a1df5a5304d6d90c3c9f9",
            ["0030"] = "e2ca2d749058934a43ef52405e0bcd123f96321973077265f082b095fe9c68de",
            ["0031"] = "f028cd31acb086455053408da863f4cf402c95af1595c4a621b83bba15afb838",
            ["0032"] = "c8b4826c823280e8d0d46cd8592bcf8f5f2b803547d645c9a0704522a0948fe3",
            ["0033"] = "bc262e491ff3cc089f1ee395e7fb0bcd522dd1f4baef2d519eb325870fda3bee",
            ["0034"] = "7584c4adf05a8dc017dbcca979a8647ad96b15aedc751cc1c7c292d58fba060b",
            ["0035"] = "83ba8879fc7b1a6a611afa6cb35f14f2c9094611650f2d1017278767fbd01a0f"
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
