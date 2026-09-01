// 覆盖日常下一配方建议的工程师回执与实际生产结果冻结。
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.Events;
using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessResearchWorkflowRecipeDecisionTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task RecipeRecommendationDecision_FreezesChoiceAndActualOutcome()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft() with
        {
            Code = "daily-recipe-decision"
        }, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project);
        var assembler = new StubObservationAssembler(new ResearchRunObservation
        {
            ExecutionKey = "production-recipe-001",
            ActualFactors =
            [
                new ResearchVariableSetting
                    { VariableCode = "holding-temperature", Value = 521, Unit = "Cel" },
                new ResearchVariableSetting
                    { VariableCode = "press-force", Value = 12.2, Unit = "kN" }
            ],
            ProcessFeatures = new Dictionary<string, double> { ["temperature.average"] = 520.4 },
            Outcomes = new Dictionary<string, double> { ["form-error"] = 0.31 },
            SourceContentHash = new string('a', 64)
        });
        var executions = new MutableExecutionComparisonService();
        var service = new ResearchRecipeRecommendationDecisionService(store, assembler, executions);
        var item = Assert.Single(recommendation.Items);

        var recorded = await service.RecordDecisionAsync(
            recommendation.RecommendationId,
            item.RecommendationKey,
            new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Modified,
                EngineerSelectedParameters =
                [
                    new ResearchVariableSetting
                        { VariableCode = "holding-temperature", Value = 520, Unit = "Cel" },
                    new ResearchVariableSetting
                        { VariableCode = "press-force", Value = 12, Unit = "kN" }
                ],
                Reason = "当前材料批次要求降低升温幅度。",
                UsefulnessRating = ResearchUsefulnessRatings.PartlyUseful
            },
            "engineer-b");

        Assert.Equal(64, recorded.DecisionSnapshotHash.Length);
        Assert.Equal(ResearchRecipeRecommendationDecisionStatuses.Modified, recorded.Decision);
        Assert.Null(recorded.Outcome);
        Assert.Contains(await store.ListAuditEntriesAsync(project.ProjectId), entry =>
            entry.ResourceType == "recipe-recommendation-decision" &&
            entry.Action == "decision-frozen");

        var startedAt = recorded.DecidedAt.AddSeconds(1);
        executions.Set(Execution("production-recipe-001", startedAt));
        recorded = await service.LinkActualExecutionAsync(
            recorded.DecisionId,
            new ResearchRecipeRecommendationExecutionLinkRequest
            {
                ActualExecutionKey = "production-recipe-001"
            },
            "engineer-b");
        executions.Set(Execution("production-recipe-001", startedAt, completed: true));

        var completed = await service.MaterializeOutcomeAsync(recorded.DecisionId, "engineer-c");

        Assert.NotNull(completed.Outcome);
        Assert.Equal(6, completed.Outcome.SettingDeviationFromSuggestion["holding-temperature"]);
        Assert.Equal(1, completed.Outcome.SettingDeviationFromEngineerSelection["holding-temperature"]);
        Assert.Equal(0.31, completed.Outcome.Outcomes["form-error"]);
        Assert.Equal(new string('a', 64), completed.Outcome.SourceContentHash);
        Assert.Equal("production-recipe-001", assembler.RequestedExecutionKey);
        var frozen = await service.MaterializeOutcomeAsync(recorded.DecisionId, "engineer-d");
        Assert.Equal(completed.Outcome.CapturedAt, frozen.Outcome!.CapturedAt);
        var workspace = await workflow.GetWorkspaceAsync(project.ProjectId);
        Assert.Equal(recorded.DecisionId, Assert.Single(workspace.RecipeRecommendationDecisions).DecisionId);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_ValidatesDecisionAndReturnsFrozenDuplicate()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft() with
        {
            Code = "daily-recipe-decision-validation"
        }, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project);
        var executions = new MutableExecutionComparisonService();
        var service = new ResearchRecipeRecommendationDecisionService(
            store, new StubObservationAssembler(null), executions);
        var item = Assert.Single(recommendation.Items);
        IReadOnlyList<ResearchVariableSetting> modifiedFactors =
        [
            new ResearchVariableSetting
                { VariableCode = "holding-temperature", Value = 520, Unit = "Cel" },
            new ResearchVariableSetting
                { VariableCode = "press-force", Value = 12, Unit = "kN" }
        ];

        var noReason = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.RecordDecisionAsync(recommendation.RecommendationId, item.RecommendationKey,
                new ResearchRecipeRecommendationDecisionRequest
                {
                    Decision = ResearchRecipeRecommendationDecisionStatuses.Modified,
                    EngineerSelectedParameters = modifiedFactors
                }, "engineer-b"));
        Assert.Contains("说明原因", noReason.Message, StringComparison.Ordinal);

        var acceptedMismatch = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.RecordDecisionAsync(recommendation.RecommendationId, item.RecommendationKey,
                new ResearchRecipeRecommendationDecisionRequest
                {
                    Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
                    EngineerSelectedParameters = modifiedFactors
                }, "engineer-b"));
        Assert.Contains("一致", acceptedMismatch.Message, StringComparison.Ordinal);

        var acceptedRequest = new ResearchRecipeRecommendationDecisionRequest
        {
            Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
            EngineerSelectedParameters = item.Parameters
        };
        var first = await service.RecordDecisionAsync(
            recommendation.RecommendationId, item.RecommendationKey, acceptedRequest, "engineer-b");
        var repeated = await service.RecordDecisionAsync(
            recommendation.RecommendationId, item.RecommendationKey, acceptedRequest, "engineer-b");

        Assert.Equal(first.DecisionId, repeated.DecisionId);
        var conflict = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.RecordDecisionAsync(
                recommendation.RecommendationId, item.RecommendationKey, acceptedRequest, "engineer-c"));
        Assert.Contains("幂等重试", conflict.Message, StringComparison.Ordinal);
        Assert.Single((await store.ListRecipeRecommendationDecisionsPageAsync(
            project.ProjectId, null, 100)).Items);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_CanLinkTheActualRunAfterTheDecision()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft() with
        {
            Code = "daily-recipe-decision-late-execution-link"
        }, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project);
        var executions = new MutableExecutionComparisonService();
        var service = new ResearchRecipeRecommendationDecisionService(
            store, new StubObservationAssembler(null), executions);
        var item = Assert.Single(recommendation.Items);

        var decision = await service.RecordDecisionAsync(
            recommendation.RecommendationId,
            item.RecommendationKey,
            new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
                EngineerSelectedParameters = item.Parameters
            },
            "engineer-b");

        Assert.Null(decision.ActualExecutionKey);
        executions.Set(Execution(
            "production-recipe-004", decision.DecidedAt.AddSeconds(1)));
        var linked = await service.LinkActualExecutionAsync(
            decision.DecisionId,
            new ResearchRecipeRecommendationExecutionLinkRequest
            {
                ActualExecutionKey = "production-recipe-004"
            },
            "engineer-b");
        var repeated = await service.LinkActualExecutionAsync(
            decision.DecisionId,
            new ResearchRecipeRecommendationExecutionLinkRequest
            {
                ActualExecutionKey = "production-recipe-004"
            },
            "engineer-c");

        Assert.Equal("production-recipe-004", linked.ActualExecutionKey);
        Assert.Equal(linked.ActualExecutionKey, repeated.ActualExecutionKey);
        await Assert.ThrowsAsync<ProcessResearchRuleException>(() => service.LinkActualExecutionAsync(
            decision.DecisionId,
            new ResearchRecipeRecommendationExecutionLinkRequest
            {
                ActualExecutionKey = "production-recipe-005"
            },
            "engineer-c"));
    }

    [Fact]
    public async Task RecipeRecommendationDecision_ExactRetriesRemainReadableAfterProjectIsArchived()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft() with
        {
            Code = "daily-recipe-decision-archived-retry"
        }, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project);
        var executions = new MutableExecutionComparisonService();
        var service = new ResearchRecipeRecommendationDecisionService(
            store, new StubObservationAssembler(null), executions);
        var item = Assert.Single(recommendation.Items);
        var request = new ResearchRecipeRecommendationDecisionRequest
        {
            Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
            EngineerSelectedParameters = item.Parameters
        };

        var decision = await service.RecordDecisionAsync(
            recommendation.RecommendationId, item.RecommendationKey, request, "engineer-b");
        executions.Set(Execution("production-recipe-archived", decision.DecidedAt.AddSeconds(1)));
        var linked = await service.LinkActualExecutionAsync(
            decision.DecisionId,
            new ResearchRecipeRecommendationExecutionLinkRequest
            {
                ActualExecutionKey = "production-recipe-archived"
            },
            "engineer-b");
        await store.SaveProjectAsync(project with { Status = ResearchProjectStatuses.Archived });

        var repeatedDecision = await service.RecordDecisionAsync(
            recommendation.RecommendationId, item.RecommendationKey, request, "engineer-b");
        var repeatedLink = await service.LinkActualExecutionAsync(
            decision.DecisionId,
            new ResearchRecipeRecommendationExecutionLinkRequest
            {
                ActualExecutionKey = "production-recipe-archived"
            },
            "engineer-b");

        Assert.Equal(decision.DecisionId, repeatedDecision.DecisionId);
        Assert.Equal(linked.ActualExecutionKey, repeatedLink.ActualExecutionKey);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_DoesNotFreezeWithoutAllQualityOutcomes()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(ProjectDraft() with
        {
            Code = "daily-recipe-decision-missing-quality"
        }, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project);
        var item = Assert.Single(recommendation.Items);
        var executions = new MutableExecutionComparisonService();
        var service = new ResearchRecipeRecommendationDecisionService(
            store,
            new StubObservationAssembler(new ResearchRunObservation
            {
                ExecutionKey = "production-recipe-003",
                ActualFactors = item.Parameters,
                SourceContentHash = new string('e', 64)
            }),
            executions);
        var decision = await service.RecordDecisionAsync(
            recommendation.RecommendationId,
            item.RecommendationKey,
            new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
                EngineerSelectedParameters = item.Parameters
            },
            "engineer-b");
        var startedAt = decision.DecidedAt.AddSeconds(1);
        executions.Set(Execution("production-recipe-003", startedAt));
        decision = await service.LinkActualExecutionAsync(
            decision.DecisionId,
            new ResearchRecipeRecommendationExecutionLinkRequest
            {
                ActualExecutionKey = "production-recipe-003"
            },
            "engineer-b");
        executions.Set(Execution("production-recipe-003", startedAt, completed: true));

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.MaterializeOutcomeAsync(decision.DecisionId, "engineer-b"));

        Assert.Contains("完整质量结果", error.Message, StringComparison.Ordinal);
        Assert.Null((await store.GetRecipeRecommendationDecisionAsync(decision.DecisionId))!.Outcome);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_RejectedIsTerminalAndNeedsNoParameters()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "daily-recipe-rejected" }, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project);
        var service = new ResearchRecipeRecommendationDecisionService(
            store, new StubObservationAssembler(null), new MutableExecutionComparisonService());
        var item = Assert.Single(recommendation.Items);

        var decision = await service.RecordDecisionAsync(
            recommendation.RecommendationId,
            item.RecommendationKey,
            new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Rejected,
                EngineerSelectedParameters = [],
                Reason = "当前批次不适用。"
            },
            "engineer-b");

        Assert.Empty(decision.EngineerSelectedParameters);
        Assert.Null(decision.ActualExecutionKey);
        Assert.Null(decision.Outcome);
        var linkError = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.LinkActualExecutionAsync(
                decision.DecisionId,
                new ResearchRecipeRecommendationExecutionLinkRequest
                    { ActualExecutionKey = "rejected-run" },
                "engineer-b"));
        Assert.Contains("终态", linkError.Message, StringComparison.Ordinal);
        var outcomeError = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.MaterializeOutcomeAsync(decision.DecisionId, "engineer-b"));
        Assert.Contains("终态", outcomeError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_SemanticRetryRejectsEveryChangedField()
    {
        static ResearchVariableSetting Temperature(double value) => new()
        {
            VariableCode = "holding-temperature",
            Value = value,
            Unit = "Cel"
        };

        var variants = new (string Name, Func<ResearchRecipeRecommendationItem,
            ResearchRecipeRecommendationDecisionRequest> Request, string Actor)[]
        {
            ("decision", item => new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Rejected,
                EngineerSelectedParameters = [],
                Reason = "降低温度以适配当前批次。",
                UsefulnessRating = ResearchUsefulnessRatings.PartlyUseful
            }, "engineer-b"),
            ("parameters", item => new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Modified,
                EngineerSelectedParameters = [Temperature(519), item.Parameters[1]],
                Reason = "降低温度以适配当前批次。",
                UsefulnessRating = ResearchUsefulnessRatings.PartlyUseful
            }, "engineer-b"),
            ("reason", item => new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Modified,
                EngineerSelectedParameters = [Temperature(520), item.Parameters[1]],
                Reason = "另一条工程判断。",
                UsefulnessRating = ResearchUsefulnessRatings.PartlyUseful
            }, "engineer-b"),
            ("rating", item => new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Modified,
                EngineerSelectedParameters = [Temperature(520), item.Parameters[1]],
                Reason = "降低温度以适配当前批次。",
                UsefulnessRating = ResearchUsefulnessRatings.Useful
            }, "engineer-b"),
            ("actor", item => new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Modified,
                EngineerSelectedParameters = [Temperature(520), item.Parameters[1]],
                Reason = "降低温度以适配当前批次。",
                UsefulnessRating = ResearchUsefulnessRatings.PartlyUseful
            }, "engineer-c")
        };

        foreach (var variant in variants)
        {
            var store = new MemoryStore();
            var workflow = CreateWorkflow(store);
            var project = await workflow.CreateProjectAsync(
                ProjectDraft() with { Code = $"semantic-retry-{variant.Name}" }, "engineer-a");
            var recommendation = await CreateRecommendationAsync(store, project);
            var item = Assert.Single(recommendation.Items);
            var service = new ResearchRecipeRecommendationDecisionService(
                store, new StubObservationAssembler(null), new MutableExecutionComparisonService());
            var original = new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Modified,
                EngineerSelectedParameters = [Temperature(520), item.Parameters[1]],
                Reason = "降低温度以适配当前批次。",
                UsefulnessRating = ResearchUsefulnessRatings.PartlyUseful
            };

            var first = await service.RecordDecisionAsync(
                recommendation.RecommendationId, item.RecommendationKey, original, "engineer-b");
            var exact = await service.RecordDecisionAsync(
                recommendation.RecommendationId, item.RecommendationKey, original, "engineer-b");
            Assert.Equal(first.DecisionId, exact.DecisionId);

            var conflict = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
                service.RecordDecisionAsync(
                    recommendation.RecommendationId,
                    item.RecommendationKey,
                    variant.Request(item),
                    variant.Actor));
            Assert.Contains("幂等重试", conflict.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RecipeRecommendationDecision_RejectsInvalidExecutionIdentityAndTiming()
    {
        var cases = new (string Name, Func<DateTimeOffset, ExecutionComparisonRow?> Execution,
            string ExpectedMessage)[]
        {
            ("missing", _ => null, "不存在或不在项目站点范围"),
            ("family", decidedAt => Execution("scope-family", decidedAt.AddSeconds(1)) with
                { ProductFamilyCode = "lens-b" }, "产品族"),
            ("product", decidedAt => Execution("scope-product", decidedAt.AddSeconds(1)) with
                { ProductCode = "product-b" }, "产品"),
            ("equipment", decidedAt => Execution("scope-equipment", decidedAt.AddSeconds(1)) with
                { EquipmentId = "press-02" }, "设备"),
            ("historical", decidedAt => Execution("scope-historical", decidedAt.AddSeconds(-1)),
                "决定之后开始"),
            ("known-result", decidedAt => Execution(
                "scope-known-result", decidedAt.AddSeconds(1), completed: true), "结果已知")
        };

        foreach (var testCase in cases)
        {
            var store = new MemoryStore();
            var workflow = CreateWorkflow(store);
            var project = await workflow.CreateProjectAsync(ProjectDraft() with
            {
                Code = $"execution-integrity-{testCase.Name}",
                Context = new Dictionary<string, string>
                {
                    ["product_family_code"] = "lens-a",
                    ["product_code"] = "product-a",
                    ["equipment_id"] = "press-01"
                }
            }, "engineer-a");
            var recommendation = await CreateRecommendationAsync(store, project);
            var item = Assert.Single(recommendation.Items);
            var executions = new MutableExecutionComparisonService();
            var service = new ResearchRecipeRecommendationDecisionService(
                store, new StubObservationAssembler(null), executions);
            var decision = await service.RecordDecisionAsync(
                recommendation.RecommendationId,
                item.RecommendationKey,
                new ResearchRecipeRecommendationDecisionRequest
                {
                    Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
                    EngineerSelectedParameters = item.Parameters
                },
                "engineer-b");
            var execution = testCase.Execution(decision.DecidedAt);
            var executionKey = execution?.ExecutionId ?? "missing-run";
            if (execution is not null)
                executions.Set(execution);

            var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
                service.LinkActualExecutionAsync(
                    decision.DecisionId,
                    new ResearchRecipeRecommendationExecutionLinkRequest
                        { ActualExecutionKey = executionKey },
                    "engineer-b"));
            Assert.Contains(testCase.ExpectedMessage, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task RecipeRecommendationDecision_IncompleteOutcomeRemainsRetryable()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var draft = ProjectDraft() with
        {
            Code = "daily-outcome-retry",
            OutcomeConstraints =
            [
                new ResearchOutcomeConstraint
                {
                    Code = "form-error-limit",
                    Description = "面形误差上限",
                    OutcomeCode = "form-error",
                    Limit = 0.4,
                    Unit = "um"
                }
            ]
        };
        var project = await workflow.CreateProjectAsync(draft, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project);
        var item = Assert.Single(recommendation.Items);
        var assembler = new MutableObservationAssembler();
        var executions = new MutableExecutionComparisonService();
        var service = new ResearchRecipeRecommendationDecisionService(store, assembler, executions);
        var decision = await service.RecordDecisionAsync(
            recommendation.RecommendationId,
            item.RecommendationKey,
            new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
                EngineerSelectedParameters = item.Parameters
            },
            "engineer-b");
        var startedAt = decision.DecidedAt.AddSeconds(1);
        executions.Set(Execution("retryable-run", startedAt));
        decision = await service.LinkActualExecutionAsync(
            decision.DecisionId,
            new ResearchRecipeRecommendationExecutionLinkRequest
                { ActualExecutionKey = "retryable-run" },
            "engineer-b");
        executions.Set(Execution("retryable-run", startedAt, completed: true));

        assembler.Observation = Observation(item.Parameters) with
        {
            ProcessFeatures = new Dictionary<string, double>(),
            ConstraintOutcomes = new Dictionary<string, double> { ["form-error-limit"] = 0.3 }
        };
        await AssertIncompleteOutcomeAsync(service, store, decision.DecisionId, "过程特征");

        assembler.Observation = Observation(item.Parameters) with
        {
            ConstraintOutcomes = new Dictionary<string, double> { ["form-error-limit"] = 0.3 },
            ValidForOptimization = false,
            ExclusionReason = "context admission failed"
        };
        await AssertIncompleteOutcomeAsync(service, store, decision.DecisionId, "证据准入");

        assembler.Observation = Observation(item.Parameters);
        await AssertIncompleteOutcomeAsync(service, store, decision.DecisionId, "完整结果约束");

        assembler.Observation = Observation(item.Parameters) with
        {
            ConstraintOutcomes = new Dictionary<string, double> { ["form-error-limit"] = 0.3 }
        };
        var completed = await service.MaterializeOutcomeAsync(decision.DecisionId, "engineer-c");
        Assert.NotNull(completed.Outcome);
        Assert.True(completed.Outcome.ValidForOptimization);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_RejectsStaleProjectRevision()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "stale-recipe-decision" }, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project);
        await workflow.UpdateProjectAsync(
            project.ProjectId, project with { Name = "修订后的项目定义" }, "engineer-a");
        var item = Assert.Single(recommendation.Items);
        var service = new ResearchRecipeRecommendationDecisionService(
            store, new StubObservationAssembler(null), new MutableExecutionComparisonService());

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.RecordDecisionAsync(
                recommendation.RecommendationId,
                item.RecommendationKey,
                new ResearchRecipeRecommendationDecisionRequest
                {
                    Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
                    EngineerSelectedParameters = item.Parameters
                },
                "engineer-b"));

        Assert.Contains("项目定义已在建议生成后变更", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_UpgradesCurrentLegacyRecommendationAtDecisionTime()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "legacy-current-recipe-decision" }, "engineer-a");
        var recommendation = await CreateRecommendationAsync(store, project, includeSnapshot: false);
        var item = Assert.Single(recommendation.Items);
        var service = new ResearchRecipeRecommendationDecisionService(
            store, new StubObservationAssembler(null), new MutableExecutionComparisonService());

        var decision = await service.RecordDecisionAsync(
            recommendation.RecommendationId,
            item.RecommendationKey,
            new ResearchRecipeRecommendationDecisionRequest
            {
                Decision = ResearchRecipeRecommendationDecisionStatuses.Accepted,
                EngineerSelectedParameters = item.Parameters
            },
            "engineer-b");

        Assert.Equal(project.Revision, decision.ProjectSnapshot.Revision);
        Assert.Equal(project.ProjectId, decision.ProjectSnapshot.ProjectId);
        Assert.Equal(64, decision.ProjectSnapshotHash.Length);
        Assert.NotEqual("none", decision.ProjectSnapshotHash);
    }

    private static async Task AssertIncompleteOutcomeAsync(
        ResearchRecipeRecommendationDecisionService service,
        MemoryStore store,
        Guid decisionId,
        string expectedMessage)
    {
        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
            service.MaterializeOutcomeAsync(decisionId, "engineer-c"));
        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
        Assert.Null((await store.GetRecipeRecommendationDecisionAsync(decisionId))!.Outcome);
    }

    private static ResearchRunObservation Observation(
        IReadOnlyList<ResearchVariableSetting> actualFactors)
        => new()
        {
            ExecutionKey = "retryable-run",
            ActualFactors = actualFactors,
            ProcessFeatures = new Dictionary<string, double> { ["temperature.average"] = 520.2 },
            Outcomes = new Dictionary<string, double> { ["form-error"] = 0.3 },
            SourceContentHash = new string('f', 64),
            ValidForOptimization = true
        };

    private static Task<ResearchRecipeRecommendation> CreateRecommendationAsync(
        MemoryStore store,
        ResearchProject project,
        bool includeSnapshot = true)
    {
        var now = DateTimeOffset.UtcNow;
        var projectSnapshot = ResearchProjectEvidenceSnapshots.Freeze(project);
        var value = new ResearchRecipeRecommendation
        {
            RecommendationId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ProjectRevision = project.Revision,
            ProjectSnapshot = includeSnapshot ? projectSnapshot : new ResearchProjectEvidenceSnapshot(),
            ProjectSnapshotHash = includeSnapshot
                ? ResearchProjectEvidenceSnapshots.Hash(projectSnapshot)
                : "none",
            ModelVersion = "recipe-test-model",
            InputHash = new string('b', 64),
            FeatureSetId = project.OptimizationFeatures.FeatureSetId,
            FeatureSetVersion = project.OptimizationFeatures.Version,
            MechanismKnowledgeSnapshotHash = new string('c', 64),
            MechanismModelSnapshotHash = new string('d', 64),
            Items =
            [
                new ResearchRecipeRecommendationItem
                {
                    RecommendationKey = "recipe-suggestion-001",
                    Parameters = Parameters(515, 11),
                    Prediction = new OptimizationRunPrediction
                    {
                        ExecutionKey = "recipe-suggestion-001",
                        Objectives = new Dictionary<string, OptimizationMetricPrediction>
                        {
                            ["form-error"] = new()
                            {
                                Mean = 0.3,
                                StandardDeviation = 0.04,
                                Lower95 = 0.22,
                                Upper95 = 0.38,
                                Unit = "um"
                            }
                        },
                        Rationale = "test"
                    }
                }
            ],
            CreatedBy = "engineer-a",
            GeneratedAt = now
        };
        return store.CreateRecipeRecommendationTransactionAsync(value, new ResearchAuditEntry
        {
            EntryId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            ResourceType = "recipe-recommendation",
            ResourceId = value.RecommendationId.ToString(),
            Action = "generated",
            UserId = "engineer-a",
            CreatedAt = now
        });
    }

    private static ExecutionComparisonRow Execution(
        string executionId,
        DateTimeOffset startedAt,
        bool completed = false)
        => new()
        {
            ExecutionId = executionId,
            EquipmentId = "press-01",
            HasStarted = true,
            HasCompleted = completed,
            LifecycleComplete = completed,
            StartedAt = startedAt,
            CompletedAt = completed ? startedAt.AddMinutes(5) : null,
            ProductFamilyCode = "lens-a",
            ProductCode = "product-a"
        };

    private sealed class MutableObservationAssembler : IResearchObservationAssembler
    {
        public ResearchRunObservation? Observation { get; set; }

        public Task<ResearchObservationAssembly> AssembleProductionRunsAsync(
            ResearchProject project,
            CancellationToken ct = default)
            => Result();

        public Task<ResearchObservationAssembly> AssembleProductionRunAsync(
            ResearchProject project,
            string executionKey,
            CancellationToken ct = default)
            => Result();

        private Task<ResearchObservationAssembly> Result()
            => Task.FromResult(new ResearchObservationAssembly(
                Observation is null ? [] : [Observation],
                Observation is null ? 0 : 1));
    }
}
