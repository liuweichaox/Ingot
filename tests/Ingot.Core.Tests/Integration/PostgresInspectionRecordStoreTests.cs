using Ingot.Contracts.Inspections;
using Ingot.Platform.Infrastructure.Inspections;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresInspectionRecordStoreTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task RunLinkedInspection_ShouldRoundTripWithoutFabricatedOutputItemId()
    {
        await postgres.EnsureSchemaAsync();
        await using var store = new PostgresInspectionRecordStore(postgres.Configuration);
        var request = new CreateInspectionRecordRequest
        {
            RecordId = Guid.CreateVersion7(),
            OutputItemId = null,
            ExecutionId = $"RUN-{Guid.NewGuid():N}",
            DefinitionCode = "dimensional.final",
            DefinitionVersion = 1,
            MeasuredAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            RecordedAt = DateTimeOffset.UtcNow,
            Outcome = "PASS",
            SubmittedBy = "integration-test",
            Measurements =
            [
                new InspectionCharacteristicResult
                {
                    CharacteristicCode = "length.mm",
                    Outcome = "PASS",
                    NumericValue = 10.01m,
                    Unit = "mm"
                }
            ]
        };

        var created = await store.CreateAsync(request, submitterVerified: true);
        var loaded = await store.GetAsync(request.RecordId);

        Assert.True(created.Created);
        Assert.NotNull(loaded);
        Assert.Null(loaded.OutputItemId);
        Assert.Equal(request.ExecutionId, loaded.ExecutionId);
    }
}
