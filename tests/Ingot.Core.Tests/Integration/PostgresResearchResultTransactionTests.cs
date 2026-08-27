// 验证 PostgresResearchResultTransaction 的真实基础设施集成、失败和恢复行为。

using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Integration;

[Collection(PostgresIntegrationCollection.Name)]
public sealed class PostgresResearchResultTransactionTests(PostgresIntegrationFixture postgres)
{
    [LinuxDockerFact]
    public async Task RecipeRecommendation_ShouldUseIndependentAppendOnlyStorage()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"recipe-store-{Guid.NewGuid():N}",
            Name = "Recipe recommendation store",
            ProcessName = "Test Process",
            OwnerUserId = "engineer-a",
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var recommendation = new ResearchRecipeRecommendation
        {
            RecommendationId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = project.Revision,
            ModelVersion = "recipe-model-v1",
            InputHash = new string('a', 64),
            ObservationCount = 3,
            FeatureSetId = "recipe-features",
            MechanismKnowledgeSnapshotHash = "none",
            MechanismModelSnapshotHash = "none",
            CreatedBy = "engineer-a",
            GeneratedAt = now
        };
        var audit = new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "recipe-recommendation",
            ResourceId = recommendation.RecommendationId.ToString(),
            Action = "created",
            UserId = "engineer-a",
            CreatedAt = now
        };
        await store.SaveProjectAsync(project);

        await store.CreateRecipeRecommendationTransactionAsync(recommendation, audit);

        var persisted = await store.GetRecipeRecommendationAsync(recommendation.RecommendationId);
        Assert.Equal(recommendation.RecommendationId, persisted?.RecommendationId);
        Assert.Equal(recommendation.InputHash, persisted?.InputHash);
        Assert.Equal(recommendation.RecommendationId,
            (await store.GetRecipeRecommendationByInputHashAsync(
                project.ProjectId, recommendation.InputHash))?.RecommendationId);
        Assert.Equal(recommendation.RecommendationId, Assert.Single(
            (await store.ListRecipeRecommendationsPageAsync(project.ProjectId, null, 100)).Items)
            .RecommendationId);
        Assert.Empty(await store.ListExperimentsAsync(project.ProjectId));
        Assert.Equal("recipe-recommendation", Assert.Single(
            await store.ListAuditEntriesAsync(project.ProjectId)).ResourceType);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.CreateRecipeRecommendationTransactionAsync(
                recommendation with { RecommendationId = Guid.CreateVersion7() },
                audit with { EntryId = Guid.CreateVersion7() }));
    }

    [LinuxDockerFact]
    public async Task ExperimentTransition_ShouldAllowExactlyOneConcurrentWriterWithMatchingAudit()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"experiment-cas-{Guid.NewGuid():N}",
            Name = "Experiment CAS",
            ProcessName = "Test Process",
            OwnerUserId = "engineer-a",
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var experiment = new ResearchExperiment
        {
            ExperimentId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = project.Revision,
            Revision = 1,
            Name = "Concurrent transition",
            StopRule = "stop",
            RollbackPlan = "rollback",
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveProjectAsync(project);
        await store.SaveExperimentAsync(experiment);

        async Task<bool> TryTransitionAsync(string status, string actor)
        {
            try
            {
                await store.SaveExperimentTransactionAsync(
                    experiment with
                    {
                        Revision = 2,
                        Status = status,
                        UpdatedAt = now.AddSeconds(1)
                    },
                    new ResearchAuditEntry
                    {
                        EntryId = Guid.CreateVersion7(),
                        ProjectId = project.ProjectId,
                        ResourceType = "experiment",
                        ResourceId = experiment.ExperimentId.ToString(),
                        Action = "status-changed",
                        FromStatus = ResearchExperimentStatuses.Planned,
                        ToStatus = status,
                        UserId = actor,
                        CreatedAt = now.AddSeconds(1)
                    });
                return true;
            }
            catch (ProcessResearchRuleException)
            {
                return false;
            }
        }

        var outcomes = await Task.WhenAll(
            TryTransitionAsync(ResearchExperimentStatuses.Approved, "engineer-b"),
            TryTransitionAsync(ResearchExperimentStatuses.Cancelled, "engineer-c"));

        Assert.Single(outcomes, static value => value);
        var persisted = await store.GetExperimentAsync(experiment.ExperimentId);
        Assert.Equal(2, persisted!.Revision);
        var audits = (await store.ListAuditEntriesAsync(project.ProjectId))
            .Where(value => value.ResourceId == experiment.ExperimentId.ToString())
            .ToArray();
        var audit = Assert.Single(audits);
        Assert.Equal(persisted.Status, audit.ToStatus);
    }

    [LinuxDockerFact]
    public async Task ShadowRecommendation_ShouldBeAppendOnlyAcrossDecisionAndOutcome()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"shadow-{Guid.NewGuid():N}",
            Name = "Shadow Store Test",
            ProcessName = "Test Process",
            OwnerUserId = "engineer",
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };
        var experiment = new ResearchExperiment
        {
            ExperimentId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = project.Revision,
            Name = "Shadow Suggestions",
            StopRule = "shadow only",
            RollbackPlan = "no dispatch",
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveProjectAsync(project);
        await store.SaveExperimentAsync(experiment);
        var recommendation = new ResearchShadowRecommendation
        {
            RecommendationId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = experiment.ExperimentId,
            SuggestionExecutionKey = "suggestion-1",
            ActualExecutionKey = "actual-1",
            Decision = ResearchShadowDecisionStatuses.Accepted,
            ModelVersion = "model-a",
            ModelInputHash = new string('a', 64),
            SuggestedFactors =
            [
                new ResearchVariableSetting { VariableCode = "temperature", Value = 500, Unit = "Cel" }
            ],
            EngineerSelectedFactors =
            [
                new ResearchVariableSetting { VariableCode = "temperature", Value = 500, Unit = "Cel" }
            ],
            Prediction = new OptimizationRunPrediction
            {
                ExecutionKey = "suggestion-1",
                Rationale = "test"
            },
            Applicability = new ResearchShadowApplicabilityAssessment
            {
                Status = ResearchApplicabilityStatuses.InDomain,
                Summary = "test"
            },
            ContextSnapshot = new Dictionary<string, string> { ["equipment_id"] = "machine-1" },
            DecisionSnapshotHash = new string('b', 64),
            DecidedBy = "engineer",
            DecidedAt = now
        };

        await store.CreateShadowRecommendationAsync(recommendation);
        Assert.Equal(
            recommendation.RecommendationId,
            (await store.GetShadowRecommendationBySuggestionAsync(
                experiment.ExperimentId, "suggestion-1"))!.RecommendationId);
        Assert.Single(await store.ListShadowRecommendationsAsync(project.ProjectId));
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.CreateShadowRecommendationAsync(recommendation with
            {
                RecommendationId = Guid.CreateVersion7(),
                ActualExecutionKey = "actual-2"
            }));

        var withOutcome = recommendation with
        {
            Outcome = new ResearchShadowOutcome
            {
                ActualExecutionKey = "actual-1",
                SourceContentHash = new string('c', 64),
                CapturedAt = now.AddMinutes(1)
            }
        };
        await store.AttachShadowOutcomeAsync(withOutcome);
        Assert.Equal(
            new string('c', 64),
            (await store.GetShadowRecommendationAsync(recommendation.RecommendationId))!
                .Outcome!.SourceContentHash);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.AttachShadowOutcomeAsync(withOutcome with
            {
                Outcome = withOutcome.Outcome with { SourceContentHash = new string('d', 64) }
            }));

        using var rawDocument = JsonDocument.Parse("{\"step_traces\":[]}");
        var replay = new ResearchHistoricalReplayReport
        {
            ReportId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            DatasetSnapshotHash = new string('e', 64),
            UniqueConditionCount = 5,
            SourceRunCount = 5,
            Budget = 5,
            SeedCount = 2,
            InitialObservationCount = 3,
            Optimizer = new ResearchReplayMethodSummary { SuccessRate = 1, Runs = 2 },
            Random = new ResearchReplayMethodSummary { SuccessRate = 0.5, Runs = 2 },
            EnginePolicy = "production-equivalent:test",
            EvidenceKind = "historical-pool-ranking",
            Limitations = "test",
            GatePassed = true,
            RawResult = rawDocument.RootElement.Clone(),
            ReportHash = new string('f', 64),
            GeneratedBy = "engineer-a",
            GeneratedAt = now
        };
        await store.CreateHistoricalReplayReportAsync(replay);
        Assert.Single(await store.ListHistoricalReplayReportsAsync(project.ProjectId));
        var reviewedReplay = replay with
        {
            Status = ResearchHistoricalReplayStatuses.Reviewed,
            ReviewedBy = "engineer-b",
            ReviewedAt = now.AddMinutes(2)
        };
        await store.ReviewHistoricalReplayReportAsync(reviewedReplay);
        Assert.Equal(
            "engineer-b",
            (await store.GetHistoricalReplayReportAsync(replay.ReportId))!.ReviewedBy);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.ReviewHistoricalReplayReportAsync(reviewedReplay));

        var drill = new ResearchRollbackDrill
        {
            DrillId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = project.Revision,
            Name = "Rollback Drill",
            Scenario = "optimizer unavailable",
            StopTrigger = "timeout",
            RollbackTarget = "last safe processSpecification",
            ExpectedActions = ["stop", "rollback"],
            ObservedActions = ["stop", "rollback"],
            Passed = true,
            EvidenceReference = "log:rollback-1",
            EvidenceContentHash = new string('1', 64),
            RecordHash = new string('2', 64),
            ConductedBy = "engineer-a",
            ConductedAt = now,
            RecordedAt = now
        };
        await store.CreateRollbackDrillAsync(drill);
        var reviewedDrill = drill with
        {
            Status = ResearchRollbackDrillStatuses.Reviewed,
            ReviewedBy = "engineer-b",
            ReviewedAt = now.AddMinutes(3)
        };
        await store.ReviewRollbackDrillAsync(reviewedDrill);
        Assert.Equal(
            "engineer-b",
            (await store.GetRollbackDrillAsync(drill.DrillId))!.ReviewedBy);
        Assert.Single(await store.ListRollbackDrillsAsync(project.ProjectId));
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.ReviewRollbackDrillAsync(reviewedDrill));
    }

    [LinuxDockerFact]
    public async Task ControlledDecision_ShouldCommitOnceWithRunPlanAndAudit()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"controlled-{Guid.NewGuid():N}",
            Name = "Controlled Transaction Test",
            ProcessName = "Test Process",
            OwnerUserId = "engineer-a",
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };
        var originalRun = new ExperimentRunPlan
        {
            ExecutionKey = "controlled-run-1",
            Sequence = 1,
            Factors =
            [
                new ResearchVariableSetting
                    { VariableCode = "temperature", Value = 500, Unit = "Cel" }
            ]
        };
        var experiment = new ResearchExperiment
        {
            ExperimentId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Name = "Controlled Suggestion",
            RunPlan = [originalRun],
            StopRule = "stop",
            RollbackPlan = "rollback",
            CreatedAt = now,
            UpdatedAt = now,
            Optimization = new ResearchOptimizationMetadata
            {
                ModelVersion = "test",
                InputHash = new string('a', 64),
                Mode = ResearchOptimizationModes.Controlled
            }
        };
        await store.SaveProjectAsync(project);
        await store.SaveExperimentAsync(experiment);

        async Task<bool> TryFreezeAsync(double approvedValue, char hashCharacter)
        {
            var approved = new ResearchVariableSetting
            { VariableCode = "temperature", Value = approvedValue, Unit = "Cel" };
            var updated = experiment with
            {
                Revision = experiment.Revision + 1,
                RunPlan = [originalRun with { Factors = [approved] }],
                ControlledDecision = new ResearchControlledDecision
                {
                    Decision = ResearchControlledDecisionStatuses.Modified,
                    SuggestedFactors = originalRun.Factors,
                    ApprovedFactors = [approved],
                    Reason = "test",
                    DecisionSnapshotHash = new string(hashCharacter, 64),
                    DecidedBy = "engineer-b",
                    DecidedAt = now.AddSeconds(1)
                },
                UpdatedAt = now.AddSeconds(1)
            };
            try
            {
                await store.SaveControlledDecisionTransactionAsync(
                    updated,
                    new ResearchAuditEntry
                    {
                        EntryId = Guid.CreateVersion7(),
                        ProjectId = project.ProjectId,
                        ResourceType = "controlled-online-decision",
                        ResourceId = experiment.ExperimentId.ToString(),
                        Action = "controlled-modified",
                        UserId = "engineer-b",
                        CreatedAt = now.AddSeconds(1)
                    });
                return true;
            }
            catch (ProcessResearchRuleException)
            {
                return false;
            }
        }

        var attempts = await Task.WhenAll(
            TryFreezeAsync(501, 'b'),
            TryFreezeAsync(502, 'c'));

        Assert.Single(attempts, static value => value);
        var frozen = (await store.GetExperimentAsync(experiment.ExperimentId))!;
        Assert.NotNull(frozen.ControlledDecision);
        Assert.Contains(
            frozen.ControlledDecision.ApprovedFactors.Single().Value,
            new[] { 501d, 502d });
        Assert.Single(
            await store.ListAuditEntriesAsync(project.ProjectId),
            value => value.ResourceType == "controlled-online-decision");
    }

    [LinuxDockerFact]
    public async Task TransferAssessment_ShouldPersistIdempotentlyAndReviewOnce()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var source = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"transfer-source-{Guid.NewGuid():N}",
            Name = "Transfer Source",
            ProcessName = "Test Process",
            OwnerUserId = "engineer-a",
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };
        var target = source with
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"transfer-target-{Guid.NewGuid():N}",
            Name = "Transfer Target"
        };
        await store.SaveProjectAsync(source);
        await store.SaveProjectAsync(target);
        var window = new ResearchOperatingRegion
        {
            OperatingRegionId = Guid.CreateVersion7(),
            ProjectId = source.ProjectId,
            Name = "Source Window",
            Status = OperatingRegionStatuses.Validated,
            ValidationLevel = OperatingRegionValidationLevels.Production,
            ConfidenceMethod = ResearchConfidenceMethods.Frequentist,
            AnalysisHash = new string('a', 64),
            Applicability = "test",
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveOperatingRegionAsync(window);
        var assessment = new ResearchTransferAssessment
        {
            AssessmentId = Guid.CreateVersion7(),
            ProjectId = target.ProjectId,
            TargetProjectRevision = 1,
            SourceProjectId = source.ProjectId,
            SourceProjectRevision = 1,
            SourceOperatingRegionId = window.OperatingRegionId,
            SourceOperatingRegionAnalysisHash = window.AnalysisHash,
            TransferResultId = Guid.CreateVersion7(),
            TransferResultAnalysisHash = new string('b', 64),
            ColdStartResultId = Guid.CreateVersion7(),
            ColdStartResultAnalysisHash = new string('c', 64),
            Outcome = ResearchTransferOutcomes.Beneficial,
            SchemaCompatible = true,
            EvidenceSufficient = true,
            SafetyPassed = true,
            RelativeGain = 0.2,
            RecordHash = new string('d', 64),
            CreatedBy = "engineer-a",
            CreatedAt = now
        };

        var created = await store.CreateTransferAssessmentAsync(assessment);
        var duplicate = await store.CreateTransferAssessmentAsync(assessment with
        {
            AssessmentId = Guid.CreateVersion7()
        });
        Assert.Equal(created.AssessmentId, duplicate.AssessmentId);
        Assert.Single(await store.ListTransferAssessmentsAsync(target.ProjectId));

        var reviewed = created with
        {
            Status = ResearchTransferAssessmentStatuses.Reviewed,
            ReviewedBy = "engineer-b",
            ReviewedAt = now.AddMinutes(1)
        };
        await store.ReviewTransferAssessmentAsync(reviewed);
        Assert.Equal("engineer-b",
            (await store.GetTransferAssessmentAsync(created.AssessmentId))!.ReviewedBy);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.ReviewTransferAssessmentAsync(reviewed));
    }

    [LinuxDockerFact]
    public async Task MissingExperimentUpdate_ShouldRollbackResultAndAudit()
    {
        await postgres.EnsureSchemaAsync();
        var store = new PostgresProcessResearchStore(postgres.DataSource);
        var now = DateTimeOffset.UtcNow;
        var project = new ResearchProject
        {
            ProjectId = Guid.CreateVersion7(),
            Code = $"transaction-{Guid.NewGuid():N}",
            Name = "Transaction Test",
            ProcessName = "Test Process",
            OwnerUserId = "engineer",
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 1
        };
        var experiment = new ResearchExperiment
        {
            ExperimentId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            Name = "Experiment",
            StopRule = "stop",
            RollbackPlan = "rollback",
            CreatedAt = now,
            UpdatedAt = now
        };
        await store.SaveProjectAsync(project);
        await store.SaveExperimentAsync(experiment);

        var result = new ResearchExperimentResult
        {
            ResultId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ExperimentId = experiment.ExperimentId,
            DatasetSnapshotId = "snapshot-a",
            AnalysisRunId = Guid.CreateVersion7(),
            AnalysisHash = new string('a', 64),
            SafetyPassed = true,
            RecordedBy = "engineer",
            RecordedAt = now
        };
        var nonexistentUpdate = experiment with
        {
            ExperimentId = Guid.CreateVersion7(),
            Revision = experiment.Revision + 1,
            ResultIds = [result.ResultId],
            UpdatedAt = now.AddSeconds(1)
        };
        var audit = new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "experiment-result",
            ResourceId = result.ResultId.ToString(),
            Action = "recorded",
            UserId = "engineer",
            CreatedAt = now
        };

        await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            store.SaveExperimentResultTransactionAsync(result, nonexistentUpdate, audit));

        Assert.Null(await store.GetExperimentResultAsync(result.ResultId));
        Assert.DoesNotContain(
            await store.ListAuditEntriesAsync(project.ProjectId),
            item => item.EntryId == audit.EntryId);
        Assert.Empty((await store.GetExperimentAsync(experiment.ExperimentId))!.ResultIds);
    }
}
