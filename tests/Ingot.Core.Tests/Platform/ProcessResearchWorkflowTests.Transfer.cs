// 覆盖跨场景迁移评估。
using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessResearchWorkflowTransferTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task TransferAssessment_ComparesColdStartAndDetectsNegativeTransfer()
    {
        var store = new MemoryStore();
        var now = DateTimeOffset.UtcNow;
        var source = TransferProject("source", "material-a", now);
        var target = TransferProject("target", "material-b", now) with
        {
            ProjectId = Guid.CreateVersion7(),
            Code = "target-transfer"
        };
        await store.SaveProjectAsync(source);
        await store.SaveProjectAsync(target);
        var window = new ResearchOperatingRegion
        {
            OperatingRegionId = Guid.CreateVersion7(),
            ProjectId = source.ProjectId,
            Name = "Released source window",
            Status = OperatingRegionStatuses.Validated,
            ValidationLevel = OperatingRegionValidationLevels.Production,
            Variables =
            [
                new OperatingRegionVariable
                {
                    VariableCode = "temperature",
                    LowerBound = 500,
                    UpperBound = 540,
                    Unit = "Cel"
                }
            ],
            ObjectiveCodes = ["error"],
            Confidence = 0.9,
            ConfidenceMethod = ResearchConfidenceMethods.Frequentist,
            AnalysisHash = new string('a', 64),
            Applicability = "same process, measured target context",
            CreatedBy = "engineer-a",
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveOperatingRegionAsync(window);
        var coldStart = TransferResult(target.ProjectId, 0.8, true, 'b', now);
        var transferred = TransferResult(target.ProjectId, 0.3, true, 'c', now);
        await store.SaveExperimentResultAsync(coldStart);
        await store.SaveExperimentResultAsync(transferred);

        var service = new ResearchTransferAssessmentService(store);
        var beneficial = await service.AssessAsync(
            target.ProjectId,
            new ResearchTransferAssessmentRequest
            {
                SourceOperatingRegionId = window.OperatingRegionId,
                TransferResultId = transferred.ResultId,
                ColdStartResultId = coldStart.ResultId
            },
            "engineer-a");

        Assert.Equal(ResearchTransferOutcomes.Beneficial, beneficial.Outcome);
        Assert.True(beneficial.EvidenceSufficient);
        Assert.True(beneficial.SchemaCompatible);
        Assert.True(beneficial.RelativeGain > 0.05);
        Assert.Contains(beneficial.ContextDifferences, item => item.Field == "material");
        await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => service.ReviewAsync(beneficial.AssessmentId, "engineer-a"));
        var reviewed = await service.ReviewAsync(beneficial.AssessmentId, "engineer-b");
        Assert.Equal(ResearchTransferAssessmentStatuses.Reviewed, reviewed.Status);

        var workflow = CreateWorkflow(store);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            workflow.SaveKnowledgeClaimAsync(
                target.ProjectId,
                new ResearchKnowledgeClaim
                {
                    TransferAssessmentId = reviewed.AssessmentId,
                    Statement = "源窗口可迁移到目标材料条件。",
                    Applicability = "当前生产线和已验证材料条件。"
                },
                "engineer-a"));
        var secondTransferred = TransferResult(target.ProjectId, 0.25, true, 'e', now);
        await store.SaveExperimentResultAsync(secondTransferred);
        var secondAssessment = await service.AssessAsync(
            target.ProjectId,
            new ResearchTransferAssessmentRequest
            {
                SourceOperatingRegionId = window.OperatingRegionId,
                TransferResultId = secondTransferred.ResultId,
                ColdStartResultId = coldStart.ResultId
            },
            "engineer-a");
        secondAssessment = await service.ReviewAsync(secondAssessment.AssessmentId, "engineer-b");
        var claim = await workflow.SaveKnowledgeClaimAsync(
            target.ProjectId,
            new ResearchKnowledgeClaim
            {
                TransferAssessmentId = secondAssessment.AssessmentId,
                Statement = "源窗口在目标材料条件下相对从零建立有重复收益。",
                Applicability = "当前生产线和已验证材料条件。"
            },
            "engineer-a");
        Assert.Contains(claim.Evidence,
            item => item.Kind == EvidenceKinds.TransferAssessment &&
                    item.ReferenceId == secondAssessment.AssessmentId.ToString());

        var regressed = TransferResult(target.ProjectId, 1.1, true, 'd', now);
        await store.SaveExperimentResultAsync(regressed);
        var negative = await service.AssessAsync(
            target.ProjectId,
            new ResearchTransferAssessmentRequest
            {
                SourceOperatingRegionId = window.OperatingRegionId,
                TransferResultId = regressed.ResultId,
                ColdStartResultId = coldStart.ResultId
            },
            "engineer-a");
        Assert.Equal(ResearchTransferOutcomes.NegativeTransfer, negative.Outcome);
        Assert.True(negative.NegativeTransferDetected);
    }

    private static ResearchProject TransferProject(string code, string material, DateTimeOffset now)
        => new()
        {
            ProjectId = Guid.CreateVersion7(),
            Code = code,
            Name = code,
            ProcessName = "precision forming",
            MaterialName = material,
            SiteCode = "plant-a",
            Status = ResearchProjectStatuses.Active,
            Objectives =
            [
                new ResearchObjective
                {
                    Code = "error",
                    Name = "Error",
                    Unit = "um",
                    Direction = "minimize",
                    Baseline = 0.8,
                    Target = 0.2,
                    UpperLimit = 0.4
                }
            ],
            Variables =
            [
                new ResearchVariable
                {
                    Code = "temperature",
                    Name = "Temperature",
                    Role = ResearchVariableRoles.Control,
                    Unit = "Cel",
                    LowerLimit = 480,
                    UpperLimit = 560
                }
            ],
            OwnerUserId = "engineer-a",
            MemberUserIds = ["engineer-a", "engineer-b"],
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };

    private static ResearchExperimentResult TransferResult(
        Guid projectId,
        double observed,
        bool safetyPassed,
        char hashCharacter,
        DateTimeOffset now)
    {
        var observations = Enumerable.Range(1, 3).Select(index => new ResearchRunObservation
        {
            ExecutionKey = $"transfer-{hashCharacter}-{index}",
            ActualFactors =
            [
                new ResearchVariableSetting
                {
                    VariableCode = "temperature",
                    Value = 520,
                    Unit = "Cel"
                }
            ],
            Outcomes = new Dictionary<string, double> { ["error"] = observed },
            SourceContentHash = new string(hashCharacter, 64)
        }).ToArray();
        return new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = projectId,
            ExperimentId = Guid.CreateVersion7(),
            DatasetSnapshotId = $"snapshot-{hashCharacter}",
            AnalysisRunId = Guid.CreateVersion7(),
            AnalysisHash = new string(hashCharacter, 64),
            Metrics =
            [
                new ExperimentMetricResult
                {
                    ObjectiveCode = "error",
                    BaselineValue = 0.8,
                    ObservedValue = observed,
                    EffectValue = observed - 0.8,
                    Unit = "um",
                    BaselineSampleCount = 3,
                    ExperimentSampleCount = 3,
                    ComputationMethod = "source mean"
                }
            ],
            RunObservations = observations,
            RunCount = 3,
            ReplicateCount = 3,
            DistinctBlockCount = 2,
            SafetyPassed = safetyPassed,
            CalculatedFromSource = true,
            RecordedBy = "system",
            RecordedAt = now
        };
    }
}
