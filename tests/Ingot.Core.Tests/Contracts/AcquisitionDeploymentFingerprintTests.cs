using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Xunit;

namespace Ingot.Core.Tests.Contracts;

public sealed class AcquisitionDeploymentFingerprintTests
{
    [Fact]
    public void Compute_ShouldIgnoreDictionaryInsertionOrder()
    {
        var first = Deployment(new Dictionary<string, string>
        {
            ["product"] = "lens-a",
            ["line"] = "line-1"
        });
        var second = Deployment(new Dictionary<string, string>
        {
            ["line"] = "line-1",
            ["product"] = "lens-a"
        });

        Assert.Equal(
            AcquisitionDeploymentFingerprint.Compute(first),
            AcquisitionDeploymentFingerprint.Compute(second));
    }

    [Fact]
    public void Compute_ShouldChangeWhenImmutableVersionChanges()
    {
        var first = Deployment(new Dictionary<string, string>()) with
        {
            Task = Deployment(new Dictionary<string, string>()).Task with { Version = 1 }
        };
        var second = first with { Task = first.Task with { Version = 2 } };

        Assert.NotEqual(
            AcquisitionDeploymentFingerprint.Compute(first),
            AcquisitionDeploymentFingerprint.Compute(second));
    }

    private static AcquisitionDeployment Deployment(IReadOnlyDictionary<string, string> context)
        => new()
        {
            Task = new IngestionTask
            {
                TaskId = "profile-a",
                Name = "Profile A",
                Status = ConfigurationStatuses.Published,
                EdgeId = "EDGE-001",
                DataModelId = "model-a",
                Source = "connector/http/profile-a",
                SubjectId = "MACHINE-01",
                StaticContext = context
            },
            DataModel = new ProcessDataModel
            {
                ModelId = "model-a",
                Name = "Model A",
                Status = ConfigurationStatuses.Published
            }
        };
}
