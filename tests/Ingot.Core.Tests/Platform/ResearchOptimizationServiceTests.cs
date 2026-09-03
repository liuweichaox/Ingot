// 验证待出质量结果的真实运行会占用下一配方优化器的候选点。
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchOptimizationServiceTests : ProcessResearchWorkflowTestBase
{
    [Fact]
    public async Task CreateNextRecipeRecommendation_UsesOnlyCurrentLinkedPendingDecisions()
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "pending-recipe-points" }, "engineer-a");
        var snapshot = ResearchProjectEvidenceSnapshots.Freeze(project);
        var snapshotHash = ResearchProjectEvidenceSnapshots.Hash(snapshot);

        await SaveDecisionAsync(store, project, snapshot, snapshotHash, "accepted", Parameters(510, 9),
            ResearchRecipeRecommendationDecisionStatuses.Accepted, actualExecutionKey: "pending-accepted");
        await SaveDecisionAsync(store, project, snapshot, snapshotHash, "modified", Parameters(518, 13),
            ResearchRecipeRecommendationDecisionStatuses.Modified, actualExecutionKey: "pending-modified");
        await SaveDecisionAsync(store, project, snapshot, snapshotHash, "rejected", Parameters(524, 14),
            ResearchRecipeRecommendationDecisionStatuses.Rejected, actualExecutionKey: "rejected-run");
        var completed = await SaveDecisionAsync(store, project, snapshot, snapshotHash, "completed", Parameters(530, 15),
            ResearchRecipeRecommendationDecisionStatuses.Accepted, actualExecutionKey: "completed-run");
        await store.AttachRecipeRecommendationOutcomeTransactionAsync(completed.DecisionId, new ResearchRecipeRecommendationOutcome
        {
            ActualExecutionKey = "completed-run",
            SourceContentHash = new string('a', 64),
            CapturedAt = DateTimeOffset.UtcNow
        }, Audit(project.ProjectId));
        await SaveDecisionAsync(store, project, snapshot, snapshotHash, "unlinked", Parameters(534, 16),
            ResearchRecipeRecommendationDecisionStatuses.Accepted);
        await SaveDecisionAsync(store, project, snapshot, snapshotHash, "stale", Parameters(540, 17),
            ResearchRecipeRecommendationDecisionStatuses.Accepted, project.Revision - 1, "stale-run");

        var optimizer = new CapturingOptimizerClient();
        var service = new ResearchOptimizationService(store, optimizer,
            new MultipleObservationAssembler(Observation(500, 8), Observation(520, 12), Observation(540, 16)));

        var first = await service.CreateNextRecipeRecommendationAsync(
            project.ProjectId, new ResearchRecipeRecommendationRequest { Seed = 17 }, "engineer-a");

        Assert.NotNull(optimizer.LastSuggestionCall);
        var call = optimizer.LastSuggestionCall!;
        Assert.Equal(2, call.PendingPoints.Count);
        Assert.Contains(call.PendingPoints, value => Matches(value, 510, 9));
        Assert.Contains(call.PendingPoints, value => Matches(value, 518, 13));
        Assert.DoesNotContain(call.PendingPoints, value => Matches(value, 524, 14));
        Assert.DoesNotContain(call.PendingPoints, value => Matches(value, 530, 15));
        Assert.DoesNotContain(call.PendingPoints, value => Matches(value, 534, 16));
        Assert.DoesNotContain(call.PendingPoints, value => Matches(value, 540, 17));

        await SaveDecisionAsync(store, project, snapshot, snapshotHash, "new-current", Parameters(536, 10),
            ResearchRecipeRecommendationDecisionStatuses.Accepted, actualExecutionKey: "new-current-run");
        var second = await service.CreateNextRecipeRecommendationAsync(
            project.ProjectId, new ResearchRecipeRecommendationRequest { Seed = 17 }, "engineer-a");

        Assert.NotEqual(first.InputHash, second.InputHash);
        Assert.NotEqual(first.RecommendationId, second.RecommendationId);
        Assert.Equal(2, optimizer.SuggestionCallCount);
    }

    [Fact]
    public async Task CreateNextRecipeRecommendation_RejectsSuggestionOutsideObservedCoverage()
    {
        // 观察到的保压温度为 500–540，量程门允许到 544；552 只满足项目安全上限。
        var service = await CreateServiceAsync(new CapturingOptimizerClient(temperature: 548));

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => service.Service.CreateNextRecipeRecommendationAsync(
                service.ProjectId, new ResearchRecipeRecommendationRequest { Seed = 17 }, "engineer-a"));

        Assert.Contains("holding-temperature", error.Message, StringComparison.Ordinal);
        Assert.Contains("观察覆盖范围", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateNextRecipeRecommendation_AcceptsSuggestionInsideObservedCoverage()
    {
        var service = await CreateServiceAsync(new CapturingOptimizerClient(temperature: 543.5));

        var recommendation = await service.Service.CreateNextRecipeRecommendationAsync(
            service.ProjectId, new ResearchRecipeRecommendationRequest { Seed = 17 }, "engineer-a");

        Assert.Equal(543.5, recommendation.Items[0].Parameters
            .Single(value => value.VariableCode == "holding-temperature").Value);
    }

    [Fact]
    public async Task CreateNextRecipeRecommendation_StopsWhenCoverageEnvelopeIsMissing()
    {
        var service = await CreateServiceAsync(new CapturingOptimizerClient(reportCoverage: false));

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => service.Service.CreateNextRecipeRecommendationAsync(
                service.ProjectId, new ResearchRecipeRecommendationRequest { Seed = 17 }, "engineer-a"));

        Assert.Contains("未报告观察覆盖包络", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateNextRecipeRecommendation_StopsWhenReportedCoverageDisagreesWithObservations()
    {
        var service = await CreateServiceAsync(
            new CapturingOptimizerClient(coverageRelativeMargin: 0.40));

        var error = await Assert.ThrowsAsync<ProcessResearchRuleException>(
            () => service.Service.CreateNextRecipeRecommendationAsync(
                service.ProjectId, new ResearchRecipeRecommendationRequest { Seed = 17 }, "engineer-a"));

        Assert.Contains("覆盖范围与平台依据同一批运行计算的结果不一致", error.Message, StringComparison.Ordinal);
    }

    private static async Task<(ResearchOptimizationService Service, Guid ProjectId)> CreateServiceAsync(
        CapturingOptimizerClient optimizer)
    {
        var store = new MemoryStore();
        var workflow = CreateWorkflow(store);
        var project = await workflow.CreateProjectAsync(
            ProjectDraft() with { Code = "observed-coverage" }, "engineer-a");
        return (new ResearchOptimizationService(store, optimizer,
            new MultipleObservationAssembler(
                Observation(500, 8), Observation(520, 12), Observation(540, 16))),
            project.ProjectId);
    }

    private static async Task<ResearchRecipeRecommendationDecision> SaveDecisionAsync(
        MemoryStore store,
        ResearchProject project,
        ResearchProjectEvidenceSnapshot snapshot,
        string snapshotHash,
        string key,
        IReadOnlyList<ResearchVariableSetting> parameters,
        string decision,
        int? projectRevision = null,
        string? actualExecutionKey = null)
    {
        var value = new ResearchRecipeRecommendationDecision
        {
            DecisionId = Guid.CreateVersion7(),
            ProjectId = project.ProjectId,
            RecommendationId = Guid.CreateVersion7(),
            RecommendationKey = key,
            Decision = decision,
            ProjectRevision = projectRevision ?? project.Revision,
            ProjectSnapshot = snapshot,
            ProjectSnapshotHash = snapshotHash,
            SuggestedParameters = Parameters(512, 10),
            EngineerSelectedParameters = parameters,
            Prediction = new OptimizationRunPrediction { ExecutionKey = key, Rationale = "test" },
            DecisionSnapshotHash = new string('b', 64),
            DecidedBy = "engineer-a",
            DecidedAt = DateTimeOffset.UtcNow
        };
        return await store.CreateRecipeRecommendationDecisionTransactionAsync(
            value, actualExecutionKey, Audit(project.ProjectId));
    }

    private static ResearchAuditEntry Audit(Guid projectId) => new()
    {
        EntryId = Guid.CreateVersion7(),
        ProjectId = projectId,
        ResourceType = "recipe-recommendation-decision",
        ResourceId = Guid.CreateVersion7().ToString(),
        Action = "created",
        UserId = "engineer-a",
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ResearchRunObservation Observation(double temperature, double force) => new()
    {
        ExecutionKey = Guid.CreateVersion7().ToString(),
        ActualFactors = Parameters(temperature, force),
        ProcessFeatures = new Dictionary<string, double> { ["temperature.average"] = temperature },
        Outcomes = new Dictionary<string, double> { ["form-error"] = 0.3 },
        SourceContentHash = new string('c', 64),
        ValidForOptimization = true
    };

    private static bool Matches(IReadOnlyDictionary<string, double> point, double temperature, double force)
        => point["holding-temperature"] == temperature && point["press-force"] == force;

    private sealed class MultipleObservationAssembler(params ResearchRunObservation[] observations)
        : IResearchObservationAssembler
    {
        public Task<ResearchObservationAssembly> AssembleProductionRunsAsync(
            ResearchProject project, CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly(observations, observations.Length));

        public Task<ResearchObservationAssembly> AssembleProductionRunAsync(
            ResearchProject project, string executionKey, CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly(
                observations.Where(value => value.ExecutionKey == executionKey).ToArray(), observations.Length));
    }

    /// <summary>按优化服务的量程门规则复算包络，使桩与真实服务保持一致。</summary>
    private static OptimizerCoverageEnvelope Coverage(
        OptimizerSuggestionCall request,
        double relativeMargin = 0.10,
        double minimumStep = 0.02)
        => new()
        {
            ObservationCount = request.Observations.Count,
            LeverageLimit = 1,
            Variables = request.Campaign.Variables.Select(variable =>
            {
                var values = request.Observations.Select(value => value.Params[variable.Name]).ToArray();
                var observedLower = values.Min();
                var observedUpper = values.Max();
                var margin = Math.Max(
                    relativeMargin * (observedUpper - observedLower),
                    minimumStep * (variable.High - variable.Low));
                return new OptimizerCoverageVariable
                {
                    Name = variable.Name,
                    Unit = variable.Unit,
                    Lower = Math.Max(variable.Low, observedLower - margin),
                    Upper = Math.Min(variable.High, observedUpper + margin),
                    ObservedMinimum = observedLower,
                    ObservedMaximum = observedUpper
                };
            }).ToArray()
        };

    private sealed class CapturingOptimizerClient(
        double temperature = 515,
        double force = 11,
        bool reportCoverage = true,
        double coverageRelativeMargin = 0.10) : IProcessOptimizerClient
    {
        public OptimizerSuggestionCall? LastSuggestionCall { get; private set; }
        public int SuggestionCallCount { get; private set; }

        public Task<OptimizerSuggestionResponse> SuggestAsync(
            OptimizerSuggestionCall request, CancellationToken ct = default)
        {
            LastSuggestionCall = request;
            SuggestionCallCount++;
            return Task.FromResult(new OptimizerSuggestionResponse
            {
                ModelVersion = "pending-point-test-v1",
                ObservationCount = request.Observations.Count,
                CoverageEnvelope = reportCoverage ? Coverage(request, coverageRelativeMargin) : null,
                Suggestions =
                [
                    new OptimizerSuggestionOutput
                    {
                        RecommendedParameters = new Dictionary<string, double>
                        {
                            ["holding-temperature"] = temperature,
                            ["press-force"] = force
                        },
                        ModelVersion = "pending-point-test-v1"
                    }
                ]
            });
        }

        public Task<ProcessDiagnosisResponse> DiagnoseAsync(
            ProcessDiagnosisCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<JsonElement> ReplayHistoryAsync(
            OptimizerHistoricalReplayCall request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
