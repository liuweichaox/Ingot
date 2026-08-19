using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ResearchAssets;
using Ingot.Agent;
using Ingot.Platform.Infrastructure.AgentTools;
using AgentContracts = Ingot.Contracts.Agents;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchAssetWorkflowTests
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
        var workflow = new ResearchAssetWorkflow(store, new EmptyProcessConfigurationStore());
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
    public async Task KnowledgeSearch_ReturnsOnlyReviewedRecordsFromReviewedSources()
    {
        var store = new MemoryStore();
        var sourceId = Guid.CreateVersion7();
        var projectId = Guid.CreateVersion7();
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
                ContextSelector = new Dictionary<string, string>
                {
                    ["research-project-id"] = projectId.ToString()
                },
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
        var otherSourceId = Guid.CreateVersion7();
        store.SeedKnowledge(
            new KnowledgeSource
            {
                SourceId = otherSourceId,
                Title = "其他项目作业指导书",
                Status = KnowledgeSourceStatuses.Reviewed,
                StorageRef = "process-knowledge://other",
                Sha256 = "def",
                MediaType = "application/pdf",
                FileName = "other.pdf",
                SizeBytes = 10,
                ContextSelector = new Dictionary<string, string>
                {
                    ["research-project-id"] = Guid.CreateVersion7().ToString()
                },
                UploadedBy = "engineer",
                UploadedAt = DateTimeOffset.UtcNow
            },
            new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = otherSourceId,
                Content = "保压温度上限为 999 °C。",
                HumanReviewed = true,
                CreatedBy = "engineer",
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
                Request = new AgentContracts.CreateChatRunRequest
                {
                    Question = "保压温度上限是多少？",
                    PageContext = new AgentContracts.PageContextRef
                    {
                        Kind = "research-project",
                        Id = projectId.ToString()
                    }
                }
            });

        Assert.Equal(AnalysisToolOutcomes.Sufficient, result.Outcome);
        var records = result.Data.GetProperty("records");
        Assert.Equal(1, records.GetArrayLength());
        Assert.Contains("185", records[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task KnowledgeSearch_WithoutResearchProjectContext_ReturnsNoRecords()
    {
        var store = new MemoryStore();
        var sourceId = Guid.CreateVersion7();
        store.SeedKnowledge(
            new KnowledgeSource
            {
                SourceId = sourceId,
                Title = "项目内知识",
                Status = KnowledgeSourceStatuses.Reviewed,
                StorageRef = "process-knowledge://scoped",
                Sha256 = "abc",
                MediaType = "text/plain",
                FileName = "scoped.txt",
                SizeBytes = 10,
                ContextSelector = new Dictionary<string, string>
                {
                    ["research-project-id"] = Guid.CreateVersion7().ToString()
                },
                UploadedBy = "engineer",
                UploadedAt = DateTimeOffset.UtcNow
            },
            new KnowledgeRecord
            {
                RecordId = Guid.CreateVersion7(),
                SourceId = sourceId,
                Content = "保压温度上限为 185 °C。",
                HumanReviewed = true,
                CreatedBy = "engineer",
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
                RunId = "run-unscoped",
                UserId = "engineer",
                EntryPoint = AgentContracts.ProductEntryPoints.Chat,
                Purpose = AgentContracts.RunPurposes.ReadOnlyAnalysis,
                Request = new AgentContracts.CreateChatRunRequest { Question = "保压温度上限是多少？" }
            });

        Assert.Equal(AnalysisToolOutcomes.InsufficientData, result.Outcome);
        Assert.Equal(0, result.Data.GetProperty("records").GetArrayLength());
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

    private sealed class MemoryStore : IResearchAssetStore
    {
        private readonly Dictionary<(string, int), TrainingDatasetVersion> _datasets = [];
        private readonly Dictionary<(string, int), ProcessModelVersion> _models = [];
        private readonly List<ModelEvaluation> _evaluations = [];
        private readonly List<ModelDriftReading> _drift = [];
        private readonly Dictionary<(string, int), MechanismModelVersion> _mechanismModels = [];
        private readonly Dictionary<(string, int), MechanismFusionDefinition> _mechanismFusions = [];
        private readonly Dictionary<Guid, KnowledgeSource> _knowledgeSources = [];
        private readonly Dictionary<Guid, KnowledgeRecord> _knowledgeRecords = [];
        public List<ResearchAssetAuditEntry> Audit { get; } = [];

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
        public Task<KnowledgeSource> AddKnowledgeSourceAsync(Stream content, string title, string sourceKind, string fileName, string mediaType, IReadOnlyDictionary<string, string> contextSelector, string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult(_knowledgeSources.GetValueOrDefault(sourceId));
        public Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KnowledgeSource>>(_knowledgeSources.Values.ToArray());
        public Task<Stream?> OpenKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult<Stream?>(null);
        public Task<KnowledgeSource> SaveKnowledgeSourceMetadataAsync(KnowledgeSource value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<KnowledgeRecord> SaveKnowledgeRecordAsync(KnowledgeRecord value, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(Guid sourceId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<KnowledgeRecord>>(_knowledgeRecords.Values.Where(item => item.SourceId == sourceId).ToArray());
        public Task AddAuditEntryAsync(ResearchAssetAuditEntry value, CancellationToken ct = default) { Audit.Add(value); return Task.CompletedTask; }
        public Task<IReadOnlyList<ResearchAssetAuditEntry>> ListAuditEntriesAsync(string resourceType, string resourceId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ResearchAssetAuditEntry>>(Audit.Where(item => item.ResourceType == resourceType && item.ResourceId == resourceId).ToArray());
    }

    private sealed class EmptyProcessConfigurationStore : IProcessConfigurationStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default) => Task.FromResult(value);
        public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessDataModel>>([]);
        public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult<ProcessDataModel?>(null);
        public Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default) => Task.FromResult(false);
        public Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default) => Task.FromResult(value);
        public Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessSpecification>>([]);
        public Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default) => Task.FromResult<ProcessSpecification?>(null);
        public Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default) => Task.FromResult(false);
        public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default) => Task.FromResult(value);
        public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ProcessAnalysisPlan>>([]);
        public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult<ProcessAnalysisPlan?>(null);
        public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult(false);
    }
}
