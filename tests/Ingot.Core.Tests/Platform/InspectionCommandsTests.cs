// 验证平台组件 InspectionCommands 的成功、拒绝和安全边界。

using Ingot.Contracts.Inspections;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class InspectionCommandsTests
{
    [Fact]
    public async Task CreateRecord_RejectsSecondCorrectionBeforeWriting()
    {
        var original = Record(Guid.CreateVersion7(), submittedBy: "inspector-a");
        var correction = Record(Guid.CreateVersion7(), submittedBy: "inspector-b") with
        {
            SupersedesRecordId = original.RecordId
        };
        var records = new RecordStore([original, correction]);
        var commands = new InspectionCommands(
            new MasterDataStore(definition: Definition()), records, null!, null!, null!);
        var request = Request(Guid.CreateVersion7()) with
        {
            SupersedesRecordId = original.RecordId,
            CorrectionReason = "再次更正"
        };

        var result = await commands.CreateRecordAsync(request, "inspector-c");

        Assert.Equal(InspectionCommandStatus.Conflict, result.Status);
        Assert.Same(correction, result.Existing);
        Assert.Equal(0, records.CreateCount);
    }

    [Fact]
    public async Task CreateReview_RejectsSubmitterReviewingOwnRecord()
    {
        var record = Record(Guid.CreateVersion7(), submittedBy: "same-user") with
        {
            Attachments =
            [
                new InspectionAttachment
                {
                    AttachmentId = Guid.CreateVersion7(),
                    StorageRef = "attachment://sha256/test/image.tiff",
                    Sha256 = new string('a', 64),
                    MediaType = "image/tiff",
                    FileName = "image.tiff",
                    SizeBytes = 4
                }
            ]
        };
        var commands = new InspectionCommands(null!, new RecordStore([record]), null!, null!, null!);

        var result = await commands.CreateReviewAsync(
            new CreateInspectionReviewRequest
            {
                ReviewId = Guid.CreateVersion7(),
                InspectionRecordId = record.RecordId,
                Decision = InspectionReviewDecisions.Confirmed
            },
            "same-user");

        Assert.Equal(InspectionCommandStatus.Invalid, result.Status);
        Assert.Equal("提交者不能复核自己的检测记录。", result.Error);
    }

    [Fact]
    public async Task UpsertScope_UsesAuthenticatedActorInsteadOfClientAuditFields()
    {
        var records = new RecordStore([]);
        var commands = new InspectionCommands(
            new MasterDataStore(plan: new InspectionPlan
            {
                PlanId = "quality-plan",
                Version = 1,
                Name = "质量方案",
                Status = InspectionPlanStatuses.Published
            }),
            records,
            null!,
            null!,
            null!);
        var clientTimestamp = DateTimeOffset.Parse("2020-01-01T00:00:00Z");

        var result = await commands.UpsertScopeAsync(
            new InspectionScope
            {
                ScopeId = "scope-1",
                OutputItemId = "part-1",
                SubjectId = "equipment-1",
                ProductFamilyCode = "lens-a",
                InspectionPlanId = "quality-plan",
                From = DateTimeOffset.UtcNow.AddHours(-1),
                To = DateTimeOffset.UtcNow,
                CreatedAt = clientTimestamp,
                CreatedBy = "spoofed-user"
            },
            "authenticated-user");

        Assert.Equal(InspectionCommandStatus.Success, result.Status);
        Assert.Equal("authenticated-user", result.Value!.CreatedBy);
        Assert.NotEqual(clientTimestamp, result.Value.CreatedAt);
        Assert.Equal("lens-a", result.Value.Context["product_family_code"]);
    }

    private static InspectionDefinition Definition() => new()
    {
        Code = "visual",
        Version = 1,
        Name = "外观",
        Characteristics =
        [
            new InspectionCharacteristicDefinition
            {
                Code = "surface",
                Name = "表面",
                InputType = "select",
                AllowedValues = ["ok", "ng"],
                PassingValues = ["ok"]
            }
        ]
    };

    private static CreateInspectionRecordRequest Request(Guid recordId) => new()
    {
        RecordId = recordId,
        ExecutionId = "execution-1",
        DefinitionCode = "visual",
        DefinitionVersion = 1,
        MeasuredAt = DateTimeOffset.UtcNow,
        RecordedAt = DateTimeOffset.UtcNow,
        Outcome = "PASS",
        SubmittedBy = "ignored",
        Measurements =
        [
            new InspectionCharacteristicResult
            {
                CharacteristicCode = "surface",
                Outcome = "PASS",
                TextValue = "ok"
            }
        ]
    };

    private static InspectionRecord Record(Guid recordId, string submittedBy) => new()
    {
        RecordId = recordId,
        ExecutionId = "execution-1",
        DefinitionCode = "visual",
        DefinitionVersion = 1,
        MeasuredAt = DateTimeOffset.UtcNow,
        RecordedAt = DateTimeOffset.UtcNow,
        IngestedAt = DateTimeOffset.UtcNow,
        Outcome = "PASS",
        SubmittedBy = submittedBy,
        SubmitterVerified = true
    };

    private sealed class RecordStore(IReadOnlyList<InspectionRecord> records) : IInspectionRecordStore
    {
        public int CreateCount { get; private set; }
        public InspectionScope? StoredScope { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<StoreInspectionRecordResult> CreateAsync(
            CreateInspectionRecordRequest request,
            bool submitterVerified,
            CancellationToken ct = default)
        {
            CreateCount++;
            throw new InvalidOperationException("This test expects the command to reject before writing.");
        }

        public Task<InspectionRecord?> GetAsync(Guid recordId, CancellationToken ct = default)
            => Task.FromResult(records.FirstOrDefault(value => value.RecordId == recordId));

        public Task<InspectionRecord?> GetCorrectionForAsync(Guid recordId, CancellationToken ct = default)
            => Task.FromResult(records.FirstOrDefault(value => value.SupersedesRecordId == recordId));

        public Task<IReadOnlyList<InspectionScope>> ListScopesAsync(string? siteId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionScope>>(StoredScope is null ? [] : [StoredScope]);

        public Task<InspectionScope?> GetScopeAsync(string scopeId, CancellationToken ct = default)
            => Task.FromResult(StoredScope?.ScopeId == scopeId ? StoredScope : null);

        public Task<InspectionScope> UpsertScopeAsync(InspectionScope scope, CancellationToken ct = default)
        {
            StoredScope = scope;
            return Task.FromResult(scope);
        }

        public Task<bool> DeleteScopeAsync(string scopeId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<InspectionRecord>> QueryAsync(
            InspectionRecordQuery query,
            CancellationToken ct = default)
            => Task.FromResult(records);

        public Task<InspectionRecordPage> QueryPageAsync(
            InspectionRecordQuery query,
            CancellationToken ct = default)
            => Task.FromResult(new InspectionRecordPage
            {
                Data = records,
                Total = records.Count,
                Offset = query.Offset,
                Limit = query.Limit
            });

        public Task<IReadOnlyList<InspectionRecord>> QueryAllByExecutionIdsAsync(
            IReadOnlyCollection<string> executionIds,
            string? siteId,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionRecord>>(
                records.Where(value => executionIds.Contains(value.ExecutionId) &&
                    (string.IsNullOrWhiteSpace(siteId) || value.SiteId == siteId)).ToArray());
    }

    private sealed class MasterDataStore(
        InspectionDefinition? definition = null,
        InspectionPlan? plan = null) : IInspectionMasterDataStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<InspectionDefinition> UpsertInspectionDefinitionAsync(InspectionDefinition value, CancellationToken ct = default) => Task.FromResult(value);
        public Task<IReadOnlyList<InspectionDefinition>> ListInspectionDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionDefinition>>(definition is null ? [] : [definition]);
        public Task<InspectionDefinition?> GetInspectionDefinitionAsync(string code, int version, CancellationToken ct = default) => Task.FromResult(definition?.Code == code && definition.Version == version ? definition : null);
        public Task<bool> DeleteInspectionDefinitionAsync(string code, int version, CancellationToken ct = default) => Task.FromResult(false);
        public Task<InspectionPlan> UpsertInspectionPlanAsync(InspectionPlan value, CancellationToken ct = default) => Task.FromResult(value);
        public Task<IReadOnlyList<InspectionPlan>> ListInspectionPlansAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionPlan>>(plan is null ? [] : [plan]);
        public Task<InspectionPlan?> GetInspectionPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult(plan?.PlanId == planId && plan.Version == version ? plan : null);
        public Task<bool> DeleteInspectionPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult(false);
        public Task<PhaseDefinition> UpsertPhaseDefinitionAsync(PhaseDefinition value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhaseDefinition>> ListPhaseDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PhaseDefinition>>([]);
        public Task<PhaseDefinition?> GetPhaseDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult<PhaseDefinition?>(null);
        public Task<bool> DeletePhaseDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult(false);
        public Task<PhaseMapping> UpsertPhaseMappingAsync(PhaseMapping value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhaseMapping>> ListPhaseMappingsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PhaseMapping>>([]);
        public Task<PhaseMapping?> GetPhaseMappingAsync(string mappingId, CancellationToken ct = default) => Task.FromResult<PhaseMapping?>(null);
        public Task<bool> DeletePhaseMappingAsync(string mappingId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<FeatureDefinition> UpsertFeatureDefinitionAsync(FeatureDefinition value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FeatureDefinition>> ListFeatureDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FeatureDefinition>>([]);
        public Task<FeatureDefinition?> GetFeatureDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult<FeatureDefinition?>(null);
        public Task<bool> DeleteFeatureDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult(false);
    }
}
