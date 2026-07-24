using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessImprovement;
using Ingot.Agent;
using Ingot.Platform.Infrastructure.AgentTools;
using AgentContracts = Ingot.Contracts.Agents;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessImprovement;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ProcessImprovementWorkflowTests
{
    [Fact]
    public async Task MechanismFusion_FourModes_AreVersionedAndDeterministic()
    {
        var store = new MemoryStore();
        var service = new MechanismModelService(store);
        var model = await service.SaveModelDraftAsync(new MechanismModelVersion
        {
            ModelId = "heat-transfer",
            Version = 1,
            Name = "可审计传热近似式",
            EquationKind = "affine",
            Inputs =
            [
                new MechanismVariableDefinition
                {
                    Code = "temperature",
                    Unit = "°C",
                    ValidMinimum = 0,
                    ValidMaximum = 1000
                }
            ],
            Output = new MechanismVariableDefinition
            {
                Code = "response",
                Unit = "unit",
                ValidMinimum = 0,
                ValidMaximum = 3000
            },
            Coefficients = new Dictionary<string, double> { ["temperature"] = 2 },
            Intercept = 1,
            ScientificBasis = "受控适用域内的线性化机理近似。"
        }, "scientist");
        await service.ChangeModelStatusAsync(model.ModelId, model.Version, "validated", "reviewer");
        await service.ChangeModelStatusAsync(model.ModelId, model.Version, "active", "reviewer");

        var expected = new Dictionary<string, double?>
        {
            [MechanismFusionModes.Calibration] = 15,
            [MechanismFusionModes.PostProcessing] = 11,
            [MechanismFusionModes.MechanismAsFeature] = 10,
            [MechanismFusionModes.Ensemble] = 9.25
        };
        foreach (var (mode, expectedValue) in expected)
        {
            var fusion = await service.SaveFusionDraftAsync(new MechanismFusionDefinition
            {
                FusionId = $"fusion-{mode}",
                Version = 1,
                Name = mode,
                Mode = mode,
                MechanismModelId = model.ModelId,
                MechanismModelVersion = model.Version,
                OutputCode = "response",
                CalibrationScale = 2,
                CalibrationOffset = 1,
                PostProcessingGain = 0.5,
                MechanismReference = 5,
                MechanismWeight = 0.25,
                MechanismFeatureCode = "mechanism.response"
            }, "scientist");
            await service.ChangeFusionStatusAsync(fusion.FusionId, 1, "validated", "reviewer");
            await service.ChangeFusionStatusAsync(fusion.FusionId, 1, "active", "reviewer");
            var result = await service.ExecuteAsync(new MechanismFusionExecutionRequest
            {
                FusionId = fusion.FusionId,
                FusionVersion = 1,
                MechanismInputs = new Dictionary<string, double> { ["temperature"] = 3 },
                DataPrediction = 10
            });

            Assert.Equal(7, result.MechanismPrediction, 12);
            Assert.Equal(expectedValue, result.FusedPrediction);
            Assert.Equal(64, result.ExecutionHash.Length);
            if (mode == MechanismFusionModes.MechanismAsFeature)
                Assert.Equal(7, result.AugmentedFeatures["mechanism.response"], 12);
        }
    }

    [Fact]
    public async Task ModelLifecycle_PassingEvaluationAndStopDrift_SuspendsModel()
    {
        var store = new MemoryStore();
        await store.AddDatasetAsync(Dataset());
        var workflow = new ProcessImprovementWorkflow(store, new EmptyProcessConfigurationStore());
        var model = await workflow.SaveModelDraftAsync(
            new ProcessModelVersion
            {
                ModelId = "surface-risk",
                Version = 1,
                Name = "表面质量风险",
                ProblemCode = "surface-defect",
                Algorithm = "gradient-boosting",
                ArtifactRef = "model://surface-risk/v1",
                ArtifactSha256 = new string('b', 64),
                DatasetId = "surface-training",
                DatasetVersion = 1,
                InputFeatureCodes = ["temperature.mean"],
                OutputCode = "quality.fail"
            },
            "engineer-a");

        await workflow.AddEvaluationAsync(
            new ModelEvaluation
            {
                ModelId = model.ModelId,
                ModelVersion = model.Version,
                SampleCount = 300,
                Passed = true,
                Metrics =
                [
                    new ModelMetric
                    {
                        Code = "f1",
                        Value = 0.88,
                        RequiredMinimum = 0.80
                    }
                ]
            },
            "engineer-b");
        await workflow.ChangeModelStatusAsync(model.ModelId, model.Version, "validated", "engineer-b");
        await workflow.ChangeModelStatusAsync(model.ModelId, model.Version, "active", "engineer-b");

        var now = DateTimeOffset.UtcNow;
        await workflow.RecordDriftAsync(
            new ModelDriftReading
            {
                ModelId = model.ModelId,
                ModelVersion = model.Version,
                MetricCode = "psi",
                Value = 0.35,
                WarningThreshold = 0.2,
                StopThreshold = 0.3,
                SampleCount = 120,
                WindowStart = now.AddHours(-24),
                WindowEnd = now
            },
            "monitor");

        Assert.Equal(
            ProcessModelStatuses.Suspended,
            (await store.GetModelAsync(model.ModelId, model.Version))!.Status);
        Assert.Contains(store.Audit, entry => entry.Action == "auto-suspended");
    }

    [Fact]
    public async Task ClosedLoop_ConfirmedTrial_CanReachVerifiedRecommendation()
    {
        var store = new MemoryStore();
        var workflow = new ProcessImprovementWorkflow(store, new EmptyProcessConfigurationStore());
        var investigation = await workflow.CreateInvestigationAsync(
            new InvestigationCase
            {
                Title = "表面缺陷率上升",
                ProblemCode = "surface-defect",
                CycleIds = ["cycle-1", "cycle-2"]
            },
            "engineer-a");
        var cause = await workflow.AddCauseAsync(
            new PossibleCause
            {
                InvestigationId = investigation.InvestigationId,
                Title = "保压阶段温度偏高",
                ParameterCode = "hold.temperature",
                PhaseCode = "holding",
                Reasoning = "同类周期中该参数与缺陷率同步变化。"
            },
            "engineer-a");
        var trial = await workflow.CreateTrialAsync(
            new ProcessTrial
            {
                InvestigationId = investigation.InvestigationId,
                CauseId = cause.CauseId,
                Name = "降低保压温度",
                StopRule = "缺陷连续两件或温度超上限立即停止。",
                RollbackPlan = "恢复到 180 °C 并隔离本批次。",
                ParameterChanges =
                [
                    new TrialParameterChange
                    {
                        ParameterCode = "hold.temperature",
                        PhaseCode = "holding",
                        BaselineValue = 180,
                        TrialValue = 176,
                        AllowedMinimum = 170,
                        AllowedMaximum = 185,
                        Unit = "°C"
                    }
                ],
                SafetyConstraints =
                [
                    new OperatingConstraint
                    {
                        Code = "temperature.maximum",
                        Description = "温度上限",
                        Operator = "<=",
                        Limit = 185,
                        Unit = "°C"
                    }
                ]
            },
            "engineer-a");
        await workflow.ChangeTrialStatusAsync(trial.TrialId, "approved", "engineer-b");
        await workflow.ChangeTrialStatusAsync(trial.TrialId, "running", "engineer-b");
        var result = await workflow.AddTrialResultAsync(
            new TrialResult
            {
                TrialId = trial.TrialId,
                MetricCode = "defect-rate",
                BaselineValue = 0.08,
                TrialValue = 0.03,
                EffectValue = -0.05,
                Unit = "ratio",
                BaselineSampleCount = 100,
                TrialSampleCount = 100,
                SafetyPassed = true
            },
            "engineer-b");
        await workflow.ChangeTrialStatusAsync(trial.TrialId, "completed", "engineer-b");
        var conclusion = await workflow.AddConclusionAsync(
            new InvestigationConclusion
            {
                InvestigationId = investigation.InvestigationId,
                CauseId = cause.CauseId,
                TrialId = trial.TrialId,
                Decision = PossibleCauseStatuses.Confirmed,
                Summary = "在当前产品和设备范围内，降低保压温度后缺陷率下降。",
                ResultIds = [result.ResultId]
            },
            "engineer-c");
        var recommendation = await workflow.CreateRecommendationAsync(
            new ParameterRecommendation
            {
                InvestigationId = investigation.InvestigationId,
                ConclusionId = conclusion.ConclusionId,
                Title = "保压温度调整",
                ParameterSettings =
                [
                    new RecommendedParameterSetting
                    {
                        ParameterCode = "hold.temperature",
                        PhaseCode = "holding",
                        CurrentValue = 180,
                        RecommendedValue = 176,
                        AllowedMinimum = 170,
                        AllowedMaximum = 185,
                        Unit = "°C"
                    }
                ],
                Constraints =
                [
                    new OperatingConstraint
                    {
                        Code = "temperature.maximum",
                        Description = "温度上限",
                        Operator = "<=",
                        Limit = 185,
                        Unit = "°C"
                    }
                ],
                ExpectedOutcomes =
                [
                    new ExpectedOutcome
                    {
                        MetricCode = "defect-rate",
                        BaselineValue = 0.08,
                        ExpectedValue = 0.03,
                        Unit = "ratio"
                    }
                ],
                ValueEstimate = new RecommendationValueEstimate
                {
                    Currency = "CNY",
                    ExpectedAnnualValue = 500_000,
                    TrialCost = 5_000,
                    ImplementationCost = 20_000,
                    DownsideAtRisk = 10_000,
                    CalculationNote = "按年产量和缺陷成本估算。"
                },
                RiskSummary = "过低可能导致充填不足。",
                StopRule = "缺陷连续两件立即停止。",
                RollbackPlan = "恢复 180 °C 并隔离本批次。"
            },
            "engineer-a");
        recommendation = await workflow.ChangeRecommendationStatusAsync(
            recommendation.RecommendationId,
            "reviewed",
            "engineer-b");
        recommendation = await workflow.ChangeRecommendationStatusAsync(
            recommendation.RecommendationId,
            "approved",
            "engineer-c");
        recommendation = await workflow.ChangeRecommendationStatusAsync(
            recommendation.RecommendationId,
            "executed",
            "operator-d",
            "MES-CHANGE-42");
        recommendation = await workflow.ChangeRecommendationStatusAsync(
            recommendation.RecommendationId,
            "verified",
            "engineer-b",
            verification: new RecommendationVerification
            {
                Outcomes =
                [
                    new RecommendationOutcome
                    {
                        MetricCode = result.MetricCode,
                        BaselineValue = result.BaselineValue,
                        ActualValue = result.TrialValue,
                        EffectValue = result.EffectValue,
                        Unit = result.Unit,
                        BaselineSampleCount = result.BaselineSampleCount,
                        ActualSampleCount = result.TrialSampleCount,
                        SafetyPassed = result.SafetyPassed
                    }
                ],
                RealizedValue = RealizedValue(48_000, 2_000) with { NetValue = 999_999 }
            });

        Assert.Equal(RecommendationStatuses.Verified, recommendation.Status);
        Assert.True(recommendation.Verification!.ObjectivesMet);
        Assert.Equal(46_000, recommendation.Verification.RealizedValue!.NetValue);
        Assert.Equal("MES-CHANGE-42", recommendation.ExecutionReference);

        var failedRecommendation = await workflow.CreateRecommendationAsync(
            recommendation with
            {
                RecommendationId = Guid.Empty,
                Title = "保压温度调整复测",
                Status = RecommendationStatuses.Draft,
                ReviewedBy = null,
                ReviewedAt = null,
                ApprovedBy = null,
                ApprovedAt = null,
                ExecutionReference = null,
                ExecutedAt = null,
                Verification = null
            },
            "engineer-a");
        failedRecommendation = await workflow.ChangeRecommendationStatusAsync(
            failedRecommendation.RecommendationId,
            "reviewed",
            "engineer-b");
        failedRecommendation = await workflow.ChangeRecommendationStatusAsync(
            failedRecommendation.RecommendationId,
            "approved",
            "engineer-c");
        failedRecommendation = await workflow.ChangeRecommendationStatusAsync(
            failedRecommendation.RecommendationId,
            "executed",
            "operator-d",
            "MES-CHANGE-43");
        failedRecommendation = await workflow.ChangeRecommendationStatusAsync(
            failedRecommendation.RecommendationId,
            "verified",
            "engineer-b",
            verification: new RecommendationVerification
            {
                Outcomes =
                [
                    new RecommendationOutcome
                    {
                        MetricCode = "defect-rate",
                        BaselineValue = 0.08,
                        ActualValue = 0.09,
                        EffectValue = 0.01,
                        Unit = "ratio",
                        BaselineSampleCount = 100,
                        ActualSampleCount = 100,
                        SafetyPassed = false
                    }
                ],
                RealizedValue = RealizedValue(-5_000, 3_000)
            });
        Assert.Equal(RecommendationStatuses.RollbackRequired, failedRecommendation.Status);
        Assert.False(failedRecommendation.Verification!.ObjectivesMet);
        failedRecommendation = await workflow.ChangeRecommendationStatusAsync(
            failedRecommendation.RecommendationId,
            "rolled-back",
            "operator-d",
            "MES-ROLLBACK-43");
        Assert.Equal(RecommendationStatuses.RolledBack, failedRecommendation.Status);
        Assert.Equal("MES-ROLLBACK-43", failedRecommendation.RollbackExecutionReference);
    }

    [Fact]
    public void RecommendationValidator_RejectsValueOutsideAllowedRange()
    {
        var valid = ProcessImprovementValidator.TryValidate(
            new ParameterRecommendation
            {
                InvestigationId = Guid.CreateVersion7(),
                ConclusionId = Guid.CreateVersion7(),
                Title = "越界建议",
                CreatedBy = "engineer",
                ParameterSettings =
                [
                    new RecommendedParameterSetting
                    {
                        ParameterCode = "temperature",
                        CurrentValue = 180,
                        RecommendedValue = 200,
                        AllowedMinimum = 170,
                        AllowedMaximum = 185,
                        Unit = "°C"
                    }
                ],
                Constraints =
                [
                    new OperatingConstraint
                    {
                        Code = "maximum",
                        Description = "上限",
                        Limit = 185,
                        Unit = "°C"
                    }
                ],
                ExpectedOutcomes =
                [
                    new ExpectedOutcome
                    {
                        MetricCode = "quality",
                        BaselineValue = 0.9,
                        ExpectedValue = 0.95,
                        Unit = "ratio"
                    }
                ],
                ValueEstimate = new RecommendationValueEstimate
                {
                    ExpectedAnnualValue = 1,
                    CalculationNote = "测试"
                },
                RiskSummary = "风险",
                StopRule = "停止",
                RollbackPlan = "回退"
            },
            out _,
            out var error);

        Assert.False(valid);
        Assert.Contains("允许范围", error);
    }

    [Fact]
    public async Task KnowledgeSearch_ReturnsOnlyReviewedRecordsFromReviewedSources()
    {
        var store = new MemoryStore();
        var sourceId = Guid.CreateVersion7();
        store.SeedKnowledge(
            new KnowledgeSource
            {
                SourceId = sourceId,
                Title = "保压阶段作业指导书",
                Status = KnowledgeSourceStatuses.Reviewed,
                StorageRef = "process-knowledge://test",
                Sha256 = "abc",
                MediaType = "application/pdf",
                FileName = "holding.pdf",
                SizeBytes = 10,
                UploadedBy = "engineer",
                UploadedAt = DateTimeOffset.UtcNow
            },
            new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = sourceId,
                Category = "constraint",
                PageOrSheet = "12",
                Content = "保压阶段温度不得超过 185 °C。",
                HumanReviewed = true,
                CreatedBy = "engineer",
                CreatedAt = DateTimeOffset.UtcNow
            },
            new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = sourceId,
                Content = "未经复核的温度建议。",
                HumanReviewed = false,
                CreatedBy = "assistant",
                CreatedAt = DateTimeOffset.UtcNow
            });
        var tool = new SearchProcessKnowledgeTool(store);

        var result = await tool.ExecuteAsync(
            new AgentContracts.AnalysisToolCall
            {
                Tool = tool.Definition.Name,
                Arguments = new Dictionary<string, string?> { ["query"] = "保压温度上限" }
            },
            new AgentExecutionContext
            {
                RunId = "run-1",
                UserId = "engineer",
                EntryPoint = AgentContracts.ProductEntryPoints.Chat,
                Purpose = AgentContracts.RunPurposes.ReadOnlyAnalysis,
                Request = new AgentContracts.CreateChatRunRequest { Question = "保压温度上限是多少？" }
            });

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        var records = result.Data.GetProperty("records");
        Assert.Equal(1, records.GetArrayLength());
        Assert.Contains("185", records[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task DeterministicPlanner_UsesKnowledgeSearchForSiteInstructionQuestion()
    {
        var tool = new SearchProcessKnowledgeTool(new MemoryStore());
        var planner = new DeterministicModelClient();

        var result = await planner.ResolveIntentAsync(
            new AgentContracts.CreateChatRunRequest
            {
                Question = "作业指导书规定的保压温度上限是多少？"
            },
            [tool.Definition]);

        var call = Assert.Single(result.Value.ToolCalls);
        Assert.Equal("search_process_knowledge", call.Tool);
        Assert.Equal("作业指导书规定的保压温度上限是多少？", call.Arguments["query"]);
    }

    private static RealizedRecommendationValue RealizedValue(
        double grossValue,
        double implementationCost)
    {
        var now = DateTimeOffset.UtcNow;
        return new RealizedRecommendationValue
        {
            Currency = "CNY",
            WindowStart = now.AddMonths(-1),
            WindowEnd = now,
            GrossValue = grossValue,
            ImplementationCost = implementationCost,
            NetValue = grossValue - implementationCost,
            CalculationNote = "按观察窗口内产量和缺陷成本核算。"
        };
    }

    private static TrainingDatasetVersion Dataset()
    {
        var now = DateTimeOffset.UtcNow;
        return new TrainingDatasetVersion
        {
            DatasetId = "surface-training",
            Version = 1,
            Name = "表面质量训练集",
            AnalysisPlanId = "surface-plan",
            AnalysisPlanVersion = 1,
            DataModelId = "casting-model",
            DataModelVersion = 1,
            FeatureCodes = ["temperature.mean"],
            TargetCode = "quality.fail",
            WindowStart = now.AddMonths(-1),
            WindowEnd = now,
            RowCount = 1000,
            ContentHash = new string('a', 64),
            CreatedBy = "engineer",
            CreatedAt = now
        };
    }

    private sealed class MemoryStore : IProcessImprovementStore
    {
        private readonly Dictionary<(string, int), TrainingDatasetVersion> _datasets = [];
        private readonly Dictionary<(string, int), ProcessModelVersion> _models = [];
        private readonly List<ModelEvaluation> _evaluations = [];
        private readonly List<ModelDriftReading> _drift = [];
        private readonly Dictionary<(string, int), MechanismModelVersion> _mechanismModels = [];
        private readonly Dictionary<(string, int), MechanismFusionDefinition> _mechanismFusions = [];
        private readonly Dictionary<Guid, InvestigationCase> _investigations = [];
        private readonly Dictionary<Guid, PossibleCause> _causes = [];
        private readonly Dictionary<Guid, ProcessTrial> _trials = [];
        private readonly List<TrialResult> _results = [];
        private readonly Dictionary<Guid, InvestigationConclusion> _conclusions = [];
        private readonly Dictionary<Guid, ParameterRecommendation> _recommendations = [];
        private readonly Dictionary<Guid, KnowledgeSource> _knowledgeSources = [];
        private readonly Dictionary<Guid, KnowledgeRecord> _knowledgeRecords = [];
        public List<ImprovementAuditEntry> Audit { get; } = [];

        public void SeedKnowledge(KnowledgeSource source, params KnowledgeRecord[] records)
        {
            _knowledgeSources[source.SourceId] = source;
            foreach (var record in records)
                _knowledgeRecords[record.RecordId] = record;
        }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<TrainingDatasetVersion> AddDatasetAsync(TrainingDatasetVersion value, CancellationToken ct = default) { _datasets[(value.DatasetId, value.Version)] = value; return Task.FromResult(value); }
        public Task<TrainingDatasetVersion?> GetDatasetAsync(string datasetId, int version, CancellationToken ct = default) => Task.FromResult(_datasets.GetValueOrDefault((datasetId, version)));
        public Task<IReadOnlyList<TrainingDatasetVersion>> ListDatasetsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TrainingDatasetVersion>>(_datasets.Values.ToArray());
        public Task<ProcessModelVersion> SaveModelAsync(ProcessModelVersion value, CancellationToken ct = default) { _models[(value.ModelId, value.Version)] = value; return Task.FromResult(value); }
        public Task<ProcessModelVersion?> GetModelAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult(_models.GetValueOrDefault((modelId, version)));
        public Task<IReadOnlyList<ProcessModelVersion>> ListModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessModelVersion>>(_models.Values.ToArray());
        public Task<ModelEvaluation> AddEvaluationAsync(ModelEvaluation value, CancellationToken ct = default) { _evaluations.Add(value); return Task.FromResult(value); }
        public Task<IReadOnlyList<ModelEvaluation>> ListEvaluationsAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ModelEvaluation>>(_evaluations.Where(item => item.ModelId == modelId && item.ModelVersion == version).ToArray());
        public Task<ModelDriftReading> AddDriftReadingAsync(ModelDriftReading value, CancellationToken ct = default) { _drift.Add(value); return Task.FromResult(value); }
        public Task<IReadOnlyList<ModelDriftReading>> ListDriftReadingsAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ModelDriftReading>>(_drift.Where(item => item.ModelId == modelId && item.ModelVersion == version).ToArray());
        public Task<MechanismModelVersion> SaveMechanismModelAsync(MechanismModelVersion value, CancellationToken ct = default) { _mechanismModels[(value.ModelId, value.Version)] = value; return Task.FromResult(value); }
        public Task<MechanismModelVersion?> GetMechanismModelAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult(_mechanismModels.GetValueOrDefault((modelId, version)));
        public Task<IReadOnlyList<MechanismModelVersion>> ListMechanismModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MechanismModelVersion>>(_mechanismModels.Values.ToArray());
        public Task<MechanismFusionDefinition> SaveMechanismFusionAsync(MechanismFusionDefinition value, CancellationToken ct = default) { _mechanismFusions[(value.FusionId, value.Version)] = value; return Task.FromResult(value); }
        public Task<MechanismFusionDefinition?> GetMechanismFusionAsync(string fusionId, int version, CancellationToken ct = default) => Task.FromResult(_mechanismFusions.GetValueOrDefault((fusionId, version)));
        public Task<IReadOnlyList<MechanismFusionDefinition>> ListMechanismFusionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<MechanismFusionDefinition>>(_mechanismFusions.Values.ToArray());
        public Task<InvestigationCase> SaveInvestigationAsync(InvestigationCase value, CancellationToken ct = default) { _investigations[value.InvestigationId] = value; return Task.FromResult(value); }
        public Task<InvestigationCase?> GetInvestigationAsync(Guid investigationId, CancellationToken ct = default) => Task.FromResult(_investigations.GetValueOrDefault(investigationId));
        public Task<IReadOnlyList<InvestigationCase>> ListInvestigationsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InvestigationCase>>(_investigations.Values.ToArray());
        public Task<PossibleCause> SaveCauseAsync(PossibleCause value, CancellationToken ct = default) { _causes[value.CauseId] = value; return Task.FromResult(value); }
        public Task<PossibleCause?> GetCauseAsync(Guid causeId, CancellationToken ct = default) => Task.FromResult(_causes.GetValueOrDefault(causeId));
        public Task<IReadOnlyList<PossibleCause>> ListCausesAsync(Guid investigationId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PossibleCause>>(_causes.Values.Where(item => item.InvestigationId == investigationId).ToArray());
        public Task<ProcessTrial> SaveTrialAsync(ProcessTrial value, CancellationToken ct = default) { _trials[value.TrialId] = value; return Task.FromResult(value); }
        public Task<ProcessTrial?> GetTrialAsync(Guid trialId, CancellationToken ct = default) => Task.FromResult(_trials.GetValueOrDefault(trialId));
        public Task<IReadOnlyList<ProcessTrial>> ListTrialsAsync(Guid investigationId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessTrial>>(_trials.Values.Where(item => item.InvestigationId == investigationId).ToArray());
        public Task<TrialResult> AddTrialResultAsync(TrialResult value, CancellationToken ct = default) { _results.Add(value); return Task.FromResult(value); }
        public Task<IReadOnlyList<TrialResult>> ListTrialResultsAsync(Guid trialId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<TrialResult>>(_results.Where(item => item.TrialId == trialId).ToArray());
        public Task<InvestigationConclusion> AddConclusionAsync(InvestigationConclusion value, CancellationToken ct = default) { _conclusions[value.ConclusionId] = value; return Task.FromResult(value); }
        public Task<InvestigationConclusion?> GetConclusionAsync(Guid conclusionId, CancellationToken ct = default) => Task.FromResult(_conclusions.GetValueOrDefault(conclusionId));
        public Task<IReadOnlyList<InvestigationConclusion>> ListConclusionsAsync(Guid investigationId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InvestigationConclusion>>(_conclusions.Values.Where(item => item.InvestigationId == investigationId).ToArray());
        public Task<KnowledgeSource> AddKnowledgeSourceAsync(Stream content, string title, string sourceKind, string fileName, string mediaType, IReadOnlyDictionary<string, string> contextSelector, string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult(_knowledgeSources.GetValueOrDefault(sourceId));
        public Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KnowledgeSource>>(_knowledgeSources.Values.ToArray());
        public Task<Stream?> OpenKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task<KnowledgeSource> SaveKnowledgeSourceMetadataAsync(KnowledgeSource value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeRecord> SaveKnowledgeRecordAsync(KnowledgeRecord value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KnowledgeRecord>>(_knowledgeRecords.Values.Where(item => item.SourceId == sourceId).ToArray());
        public Task<ParameterRecommendation> SaveRecommendationAsync(ParameterRecommendation value, CancellationToken ct = default) { _recommendations[value.RecommendationId] = value; return Task.FromResult(value); }
        public Task<ParameterRecommendation?> GetRecommendationAsync(Guid recommendationId, CancellationToken ct = default) => Task.FromResult(_recommendations.GetValueOrDefault(recommendationId));
        public Task<IReadOnlyList<ParameterRecommendation>> ListRecommendationsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ParameterRecommendation>>(_recommendations.Values.ToArray());
        public Task AddAuditEntryAsync(ImprovementAuditEntry value, CancellationToken ct = default) { Audit.Add(value); return Task.CompletedTask; }
        public Task<IReadOnlyList<ImprovementAuditEntry>> ListAuditEntriesAsync(string resourceType, string resourceId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ImprovementAuditEntry>>(Audit.Where(item => item.ResourceType == resourceType && item.ResourceId == resourceId).ToArray());
    }

    private sealed class EmptyProcessConfigurationStore : IProcessConfigurationStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default) => Task.FromResult(value);
        public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessDataModel>>([]);
        public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult<ProcessDataModel?>(null);
        public Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult(false);
        public Task<RecipeVersion> UpsertRecipeVersionAsync(RecipeVersion value, CancellationToken ct = default) => Task.FromResult(value);
        public Task<IReadOnlyList<RecipeVersion>> ListRecipeVersionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RecipeVersion>>([]);
        public Task<RecipeVersion?> GetRecipeVersionAsync(string recipeId, int version, CancellationToken ct = default) => Task.FromResult<RecipeVersion?>(null);
        public Task<bool> DeleteRecipeVersionAsync(string recipeId, int version, CancellationToken ct = default) => Task.FromResult(false);
        public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default) => Task.FromResult(value);
        public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessAnalysisPlan>>([]);
        public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult<ProcessAnalysisPlan?>(null);
        public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult(false);
    }
}
