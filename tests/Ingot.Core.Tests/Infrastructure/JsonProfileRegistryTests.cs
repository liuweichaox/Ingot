using System.Text.Json;
using Ingot.Domain.Events;
using Ingot.Domain.Models;
using Ingot.Domain.Profiles;
using Ingot.Infrastructure.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ingot.Core.Tests.Infrastructure;

public sealed class JsonProfileRegistryTests
{
    [Fact]
    public void Validate_ShouldEnforceObjectEventAndRequiredContextDeclarations()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Ingot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "optical.json"), """
            {
              "SchemaVersion": 1,
              "Name": "optical",
              "ObjectTypes": ["polishing-machine"],
              "EventTypes": [
                { "Type": "cycle.started", "RequiredContext": ["material_lot"] },
                { "Type": "cycle.completed", "RequiredContext": ["material_lot"] }
              ]
            }
            """);

            var registry = new JsonProfileRegistry(
                Options.Create(new ProfileOptions { Directory = directory }),
                NullLogger<JsonProfileRegistry>.Instance);
            var config = new DeviceConfig
            {
                SchemaVersion = 2,
                SourceCode = "POL-01-PLC",
                Profile = "optical",
                Asset = new ObjectRef("polishing-machine", "POL-01"),
                EventRules =
                [
                    new EventRule
                    {
                        RuleId = "cycle",
                        Category = "cycle",
                        ContextKeys = []
                    }
                ]
            };

            var errors = registry.Validate(config);

            Assert.Equal(2, errors.Count(error => error.Contains("material_lot")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructor_ShouldRejectUnknownProfileMembers()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Ingot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "invalid.json"), """
            {
              "SchemaVersion": 1,
              "Name": "invalid",
              "ObjectTypes": ["equipment"],
              "EventTypes": [{ "Type": "cycle.started" }],
              "EventTypess": []
            }
            """);

            var exception = Assert.Throws<JsonException>(() =>
                new JsonProfileRegistry(
                    Options.Create(new ProfileOptions { Directory = directory }),
                    NullLogger<JsonProfileRegistry>.Instance));

            Assert.Contains("EventTypess", exception.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
