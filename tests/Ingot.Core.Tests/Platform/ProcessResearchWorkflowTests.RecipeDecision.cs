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
        var recommendation = await CreateRecommendationAsync(store, BriefDraft());
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
        Assert.Equal(TestSiteCode, recorded.SiteCode);
        Assert.Equal(TestProcessSpecificationId, recorded.ProcessSpecificationId);
        Assert.Equal(recommendation.BriefHash, recorded.BriefHash);
        Assert.Null(recorded.Outcome);

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
        Assert.Equal(recommendation.BriefHash, completed.Outcome.BriefHash);
        Assert.Equal(6, completed.Outcome.SettingDeviationFromSuggestion["holding-temperature"]);
        Assert.Equal(1, completed.Outcome.SettingDeviationFromEngineerSelection["holding-temperature"]);
        Assert.Equal(0.31, completed.Outcome.Outcomes["form-error"]);
        Assert.Equal(new string('a', 64), completed.Outcome.SourceContentHash);
        Assert.Equal("production-recipe-001", assembler.RequestedExecutionKey);
        var frozen = await service.MaterializeOutcomeAsync(recorded.DecisionId, "engineer-d");
        Assert.Equal(completed.Outcome.CapturedAt, frozen.Outcome!.CapturedAt);

        var flows = await new RecipeRecommendationQueries(store).ListFlowsAsync(
            new RecipeRecommendationFilter([TestSiteCode], TestProcessSpecificationId), null, 100);
        var flow = Assert.Single(flows.Items);
        Assert.Equal(recorded.DecisionId, flow.Decision!.DecisionId);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_ValidatesDecisionAndReturnsFrozenDuplicate()
    {
        var store = new MemoryStore();
        var recommendation = await CreateRecommendationAsync(store, BriefDraft());
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
        Assert.Single(await store.ListPendingRecipeRecommendationDecisionsAsync(
            TestSiteCode, TestProcessSpecificationId));
    }

    [Fact]
    public async Task RecipeRecommendationDecision_CanLinkTheActualRunAfterTheDecision()
    {
        var store = new MemoryStore();
        var recommendation = await CreateRecommendationAsync(store, BriefDraft());
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
    public async Task RecipeRecommendationDecision_RejectsNewDecisionsOnExpiredRecommendation()
    {
        var store = new MemoryStore();
        var recommendation = await CreateRecommendationAsync(
            store, BriefDraft(), expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var service = new ResearchRecipeRecommendationDecisionService(
            store, new StubObservationAssembler(null), new MutableExecutionComparisonService());
        var item = Assert.Single(recommendation.Items);

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

        Assert.Contains("已过期", error.Message, StringComparison.Ordinal);
        Assert.Empty(await store.ListPendingRecipeRecommendationDecisionsAsync(
            TestSiteCode, TestProcessSpecificationId));
    }

    [Fact]
    public async Task RecipeRecommendationDecision_DoesNotFreezeWithoutAllQualityOutcomes()
    {
        var store = new MemoryStore();
        var recommendation = await CreateRecommendationAsync(store, BriefDraft());
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
        var recommendation = await CreateRecommendationAsync(store, BriefDraft());
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
            var recommendation = await CreateRecommendationAsync(store, BriefDraft());
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
            string? Site, string? ExpectedMessage)[]
        {
            ("missing", _ => null, null, "不存在或不在建议所属站点"),
            ("other-site", decidedAt => Execution("scope-site", decidedAt.AddSeconds(1)), "SITE-002",
                "不存在或不在建议所属站点"),
            ("family", decidedAt => Execution("scope-family", decidedAt.AddSeconds(1)) with
                { ProductFamilyCode = "lens-b" }, null, "产品族"),
            ("product", decidedAt => Execution("scope-product", decidedAt.AddSeconds(1)) with
                { ProductCode = "product-b" }, null, "产品"),
            ("equipment", decidedAt => Execution("scope-equipment", decidedAt.AddSeconds(1)) with
                { EquipmentId = "press-02" }, null, "设备"),
            ("recipe", decidedAt => Execution("scope-recipe", decidedAt.AddSeconds(1)) with
                { ProcessSpecificationId = "other-spec" }, null, "配方"),
            ("simultaneous", decidedAt => Execution("scope-simultaneous", decidedAt), null,
                "决定之后开始"),
            ("historical", decidedAt => Execution("scope-historical", decidedAt.AddSeconds(-1)), null,
                "决定之后开始"),
            ("known-result", decidedAt => Execution(
                "scope-known-result", decidedAt.AddSeconds(1), completed: true), null, null)
        };

        foreach (var testCase in cases)
        {
            var store = new MemoryStore();
            var recommendation = await CreateRecommendationAsync(store, BriefDraft());
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
                executions.Set(execution, testCase.Site ?? TestSiteCode);

            if (testCase.ExpectedMessage is not null)
            {
                var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(() =>
                    service.LinkActualExecutionAsync(
                        decision.DecisionId,
                        new ResearchRecipeRecommendationExecutionLinkRequest
                            { ActualExecutionKey = executionKey },
                        "engineer-b"));
                Assert.Contains(testCase.ExpectedMessage, error.Message, StringComparison.Ordinal);
            }
            else
            {
                var linked = await service.LinkActualExecutionAsync(
                    decision.DecisionId,
                    new ResearchRecipeRecommendationExecutionLinkRequest
                        { ActualExecutionKey = executionKey },
                    "engineer-b");
                Assert.Equal(executionKey, linked.ActualExecutionKey);
            }
        }
    }

    [Fact]
    public async Task RecipeRecommendationDecision_RecordsExcludedOutcomeAsTerminal()
    {
        var store = new MemoryStore();
        var brief = BriefDraft() with
        {
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
        var recommendation = await CreateRecommendationAsync(store, brief);
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
            ConstraintOutcomes = new Dictionary<string, double> { ["form-error-limit"] = 0.3 },
            ValidForOptimization = false,
            ExclusionReason = "context admission failed"
        };
        var excluded = await service.MaterializeOutcomeAsync(decision.DecisionId, "engineer-c");
        Assert.NotNull(excluded.Outcome);
        Assert.False(excluded.Outcome.ValidForOptimization);
        Assert.Equal("context admission failed", excluded.Outcome.ExclusionReason);
        var frozen = await service.MaterializeOutcomeAsync(decision.DecisionId, "engineer-d");
        Assert.Equal(excluded.Outcome.CapturedAt, frozen.Outcome!.CapturedAt);
    }

    [Fact]
    public async Task RecipeRecommendationDecision_RejectsTamperedFrozenBrief()
    {
        var store = new MemoryStore();
        var brief = BriefDraft();
        var recommendation = await CreateRecommendationAsync(
            store,
            brief,
            storedBrief: brief with { Name = "建议生成后被改写的条件" });
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

        Assert.Contains("冻结条件校验失败", error.Message, StringComparison.Ordinal);
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
        RecipeRecommendationBrief brief,
        DateTimeOffset? expiresAt = null,
        RecipeRecommendationBrief? storedBrief = null)
    {
        var now = DateTimeOffset.UtcNow;
        var value = new ResearchRecipeRecommendation
        {
            RecommendationId = Guid.CreateVersion7(),
            SiteCode = brief.SiteCode,
            ProcessSpecificationId = brief.ProcessSpecificationId,
            Brief = storedBrief ?? brief,
            BriefHash = RecipeRecommendationBriefPolicy.Hash(brief),
            ModelVersion = "recipe-test-model",
            InputHash = new string('b', 64),
            FeatureSetId = brief.OptimizationFeatures.FeatureSetId,
            FeatureSetVersion = brief.OptimizationFeatures.Version,
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
            GeneratedAt = now,
            ExpiresAt = expiresAt ?? now.AddHours(24)
        };
        return store.CreateRecipeRecommendationAsync(value);
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
            ProductCode = "product-a",
            ProcessSpecificationId = TestProcessSpecificationId
        };

    private sealed class MutableObservationAssembler : IResearchObservationAssembler
    {
        public ResearchRunObservation? Observation { get; set; }

        public Task<ResearchObservationAssembly> AssembleProductionRunsAsync(
            RecipeRecommendationBrief brief,
            CancellationToken ct = default)
            => Result();

        public Task<ResearchObservationAssembly> AssembleProductionRunAsync(
            RecipeRecommendationBrief brief,
            string executionKey,
            CancellationToken ct = default)
            => Result();

        private Task<ResearchObservationAssembly> Result()
            => Task.FromResult(new ResearchObservationAssembly(
                Observation is null ? [] : [Observation],
                Observation is null ? 0 : 1));
    }
}
