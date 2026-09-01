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

    private sealed class CapturingOptimizerClient : IProcessOptimizerClient
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
                Suggestions =
                [
                    new OptimizerSuggestionOutput
                    {
                        RecommendedParameters = new Dictionary<string, double>
                        {
                            ["holding-temperature"] = 515,
                            ["press-force"] = 11
                        },
                        ModelVersion = "pending-point-test-v1"
                    }
                ]
            });
        }

        public Task<OptimizerDesignResponse> DesignAsync(
            OptimizerDesignCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProcessDiagnosisResponse> DiagnoseAsync(
            ProcessDiagnosisCall request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<JsonElement> ReplayHistoryAsync(
            OptimizerHistoricalReplayCall request, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
