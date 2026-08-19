using Ingot.Platform.Application.ProcessConfiguration;
using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchObservationAssemblerTests
{
    [Fact]
    public async Task Assemble_ConnectsProcessExecutionFeaturesProcessSpecificationAndInspectionMeasurements()
    {
        var executionKey = "bo-execution-001";
        var execution = new ExecutionComparisonRow
        {
            ExecutionId = executionKey,
            EquipmentId = "FX3U-01",
            EdgeIds = ["EDGE-WORKSHOP-A"],
            Context = new Dictionary<string, string>
            {
                ["context_capture_status"] = "resolved",
                ["equipment_id"] = "FX3U-01",
                ["execution_id"] = executionKey,
                ["product_family_code"] = "lens-a",
                ["process_specification_id"] = "LENS-A",
                ["process_specification_version"] = "3",
                ["tooling_installation_id"] = Guid.NewGuid().ToString("D"),
                ["material_lot"] = "GLASS-LOT-07",
                ["material_lot_ref"] = "GLASS-LOT-07",
                ["tooling_assembly_id"] = "MOLD-A"
            },
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt = DateTimeOffset.UtcNow,
            ProductFamilyCode = "lens-a",
            OutputItemId = "lens-001",
            ExternalBatchRef = "BATCH-07",
            MaterialLotRef = "GLASS-LOT-07",
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Available,
                SampleCount = 120
            },
            ControlParameters =
            [
                new ExecutionControlParameterValue
                {
                    Code = "holding-temperature",
                    Unit = "Cel",
                    Value = JsonSerializer.SerializeToElement(512d)
                }
            ],
            Signals =
            [
                new ProcessSignalStatistic
                {
                    Code = "mold-temperature",
                    Name = "模具温度",
                    Unit = "Cel",
                    Average = 510,
                    Features =
                    [
                        new ProcessSignalFeature
                        {
                            Code = "overshoot",
                            DefinitionHash = new string('a', 64),
                            ComputationHash = new string('b', 64),
                            Value = 2.4
                        }
                    ]
                }
            ],
            AnalysisMaterialization = new ProcessExecutionAnalysisMaterialization
            {
                AlgorithmVersion = "stage-relative-v2",
                SourceMaxIngestId = 55,
                SourceEventCount = 122
            }
        };
        var inspectionRecord = new InspectionRecord
        {
            RecordId = Guid.CreateVersion7(),
            OutputItemId = "lens-001",
            ExecutionId = executionKey,
            DefinitionCode = "lens-final",
            DefinitionVersion = 1,
            MeasuredAt = DateTimeOffset.UtcNow,
            RecordedAt = DateTimeOffset.UtcNow,
            IngestedAt = DateTimeOffset.UtcNow,
            Outcome = "PASS",
            SubmittedBy = "station",
            SubmitterVerified = true,
            Measurements =
            [
                new InspectionCharacteristicResult
                {
                    CharacteristicCode = "form-error",
                    Outcome = "PASS",
                    NumericValue = 0.38m,
                    Unit = "um"
                },
                new InspectionCharacteristicResult
                {
                    CharacteristicCode = "crack-rate",
                    Outcome = "PASS",
                    NumericValue = 0.01m,
                    Unit = "ratio"
                }
            ]
        };
        var inspections = new FakeInspectionStore([inspectionRecord]);
        var executionService = new FakeProcessExecutionService(execution);
        var scenario = ResearchContextAdmissionEvaluatorTests.OpticalScenario();
        var reviewStore = new FakeReviewStore();
        var masterDataStore = new FakeMasterDataStore();
        var assembler = new ResearchObservationAssembler(
            executionService,
            inspections,
            reviewStore,
            masterDataStore,
            new FakeProcessConfigurationStore(scenario));
        var project = new ResearchProject
        {
            Code = "lens-a",
            Name = "镜片 A",
            ProcessName = "精密模压",
            Objectives =
            [
                new ResearchObjective
                {
                    Code = "form",
                    Name = "面形误差",
                    Unit = "um",
                    Target = 0.5,
                    Direction = "minimize",
                    DataSource = "inspection:form-error"
                },
                new ResearchObjective
                {
                    Code = "pass-rate",
                    Name = "最终检验合格率",
                    Unit = "1",
                    Target = 1,
                    Direction = "maximize",
                    DataSource = "inspection-outcome:lens-final"
                }
            ],
            Variables =
            [
                new ResearchVariable
                {
                    Code = "temperature",
                    Name = "保压温度",
                    Role = ResearchVariableRoles.Control,
                    Unit = "Cel",
                    LowerLimit = 480,
                    UpperLimit = 550,
                    DataSource = "control-parameter:holding-temperature"
                }
            ],
            Context = new Dictionary<string, string>
            {
                [ResearchContextAdmissionEvaluator.ScenarioPackageContextKey] =
                    $"{scenario.PackageId}:{scenario.Version}"
            },
            OutcomeConstraints =
            [
                new ResearchOutcomeConstraint
                {
                    Code = "crack-safety",
                    Description = "裂纹率安全边界",
                    OutcomeCode = "crack-rate",
                    Operator = "<=",
                    Limit = 0.05,
                    Unit = "ratio"
                }
            ]
        };
        var experiment = new ResearchExperiment
        {
            Name = "实验",
            StopRule = "完成",
            RollbackPlan = "回退",
            RunPlan =
            [
                new ExperimentRunPlan
                {
                    ExecutionKey = executionKey,
                    Sequence = 1,
                    Factors =
                    [
                        new ExperimentFactorSetting
                        {
                            VariableCode = "temperature",
                            Value = 505,
                            Unit = "Cel"
                        }
                    ]
                }
            ]
        };

        var result = await assembler.AssembleAsync(project, [experiment]);

        Assert.Equal(1, executionService.BatchQueryCount);
        Assert.Equal(1, inspections.BatchQueryCount);

        var observation = Assert.Single(result.Observations);
        Assert.True(observation.ValidForOptimization);
        Assert.Equal(512, Assert.Single(observation.ActualFactors).Value);
        Assert.True(observation.HasSettingDeviation);
        Assert.Equal(7, observation.SettingDeviationFromPlan["temperature"]);
        Assert.Equal(0.38, observation.Outcomes["form"], 6);
        Assert.Equal(1, observation.Outcomes["pass-rate"], 6);
        Assert.Equal(0.01, observation.ConstraintOutcomes["crack-safety"], 6);
        Assert.Equal(2.4, observation.ProcessFeatures["mold-temperature.execution.overshoot"], 6);
        Assert.Equal("FX3U-01", observation.Context["equipment_id"]);
        Assert.Equal("EDGE-WORKSHOP-A", observation.Context["edge_ids"]);
        Assert.Equal("BATCH-07", observation.Context["external_batch_ref"]);
        Assert.Equal("lens-001", observation.Context["output_item_id"]);
        Assert.Equal("GLASS-LOT-07", observation.Context["material_lot_ref"]);
        Assert.Equal("GLASS-LOT-07", observation.Context["material_lot"]);
        Assert.Equal("MOLD-A", observation.Context["tooling_assembly_id"]);
        Assert.Equal(
            ResearchContextAdmissionEvaluator.ComputePolicyHash(scenario),
            observation.Context[ResearchContextAdmissionEvaluator.ObservationPolicyHashContextKey]);
        Assert.Matches("^[a-f0-9]{64}$", observation.SourceContentHash);

        var failedInspectionAssembler = new ResearchObservationAssembler(
            executionService,
            new FakeInspectionStore([inspectionRecord with { RecordId = Guid.CreateVersion7(), Outcome = "FAIL" }]),
            reviewStore,
            masterDataStore,
            new FakeProcessConfigurationStore(scenario));
        var failedInspectionResult = await failedInspectionAssembler.AssembleAsync(project, [experiment]);
        Assert.Equal(0, Assert.Single(failedInspectionResult.Observations).Outcomes["pass-rate"], 6);

        var inconclusiveInspectionAssembler = new ResearchObservationAssembler(
            executionService,
            new FakeInspectionStore([inspectionRecord with { RecordId = Guid.CreateVersion7(), Outcome = "INCONCLUSIVE" }]),
            reviewStore,
            masterDataStore,
            new FakeProcessConfigurationStore(scenario));
        var inconclusiveInspectionResult = await inconclusiveInspectionAssembler.AssembleAsync(project, [experiment]);
        var inconclusive = Assert.Single(inconclusiveInspectionResult.Observations);
        Assert.False(inconclusive.ValidForOptimization);
        Assert.Contains("INCONCLUSIVE", inconclusive.ExclusionReason);

        var missingMoldProcessExecution = execution with
        {
            Context = execution.Context
                .Where(static pair => pair.Key != "tooling_assembly_id")
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
        };
        var missingMoldResult = await new ResearchObservationAssembler(
                new FakeProcessExecutionService(missingMoldProcessExecution),
                inspections,
                reviewStore,
                masterDataStore,
                new FakeProcessConfigurationStore(scenario))
            .AssembleAsync(project, [experiment]);
        var missingMold = Assert.Single(missingMoldResult.Observations);
        Assert.False(missingMold.ValidForOptimization);
        Assert.Contains("tooling_assembly_id", missingMold.ExclusionReason);

        var strictProject = project with
        {
            Variables =
            [
                project.Variables[0] with { DataSource = "control-parameter:not-collected" }
            ]
        };
        var strictResult = await assembler.AssembleAsync(strictProject, [experiment]);
        var excluded = Assert.Single(strictResult.Observations);
        Assert.False(excluded.ValidForOptimization);
        Assert.Empty(excluded.ActualFactors);
        Assert.Contains("控制变量:temperature", excluded.ExclusionReason);

        var unmappedProject = project with
        {
            Variables = [project.Variables[0] with { DataSource = null }]
        };
        var unmappedResult = await assembler.AssembleAsync(unmappedProject, [experiment]);
        var plannedOnly = Assert.Single(unmappedResult.Observations);
        Assert.False(plannedOnly.ValidForOptimization);
        Assert.Empty(plannedOnly.ActualFactors);
        Assert.Contains("缺少设备实际参数回读", plannedOnly.ExclusionReason);

        var wrongUnitProcessExecution = execution with
        {
            ControlParameters = [execution.ControlParameters[0] with { Unit = "K" }]
        };
        var wrongUnitAssembler = new ResearchObservationAssembler(
            new FakeProcessExecutionService(wrongUnitProcessExecution),
            inspections,
            reviewStore,
            masterDataStore,
            new FakeProcessConfigurationStore(scenario));
        var wrongUnitResult = await wrongUnitAssembler.AssembleAsync(project, [experiment]);
        var unitConflict = Assert.Single(wrongUnitResult.Observations);
        Assert.False(unitConflict.ValidForOptimization);
        Assert.Empty(unitConflict.ActualFactors);
        Assert.Contains("单位冲突", unitConflict.ExclusionReason);
    }

    private sealed class FakeProcessExecutionService(ExecutionComparisonRow execution) : IExecutionComparisonService
    {
        public int BatchQueryCount { get; private set; }

        public Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
            string executionId,
            CancellationToken ct = default)
            => Task.FromResult<ExecutionComparisonRow?>(
                string.Equals(executionId, execution.ExecutionId, StringComparison.Ordinal)
                    ? execution
                    : null);

        public Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default)
        {
            BatchQueryCount++;
            IReadOnlyDictionary<string, ExecutionComparisonRow> result = executionIds
                .Where(id => string.Equals(id, execution.ExecutionId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(static id => id, _ => execution, StringComparer.Ordinal);
            return Task.FromResult(result);
        }

        public Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
            string executionId,
            int limit,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ExecutionComparisonResult?> CompareSelectedAsync(
            string baselineProcessExecutionId,
            IReadOnlyList<string> executionIds,
            CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeReviewStore : IInspectionReviewStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<StoreInspectionReviewResult> CreateAsync(CreateInspectionReviewRequest request, string executionId, string reviewedBy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionReview?> GetAsync(Guid reviewId, CancellationToken ct = default) => Task.FromResult<InspectionReview?>(null);
        public Task<IReadOnlyList<InspectionReview>> QueryAsync(Guid? inspectionRecordId, string? executionId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionReview>>([]);
        public Task<IReadOnlyDictionary<Guid, InspectionReview>> GetLatestByInspectionRecordIdsAsync(IReadOnlyCollection<Guid> inspectionRecordIds, CancellationToken ct = default) => Task.FromResult<IReadOnlyDictionary<Guid, InspectionReview>>(new Dictionary<Guid, InspectionReview>());
        public Task LogAccessAsync(Guid? inspectionRecordId, Guid? attachmentId, string action, string actor, string? detail, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<InspectionAuditEntry>> QueryAuditAsync(Guid? inspectionRecordId, Guid? attachmentId, int limit, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionAuditEntry>>([]);
    }

    private sealed class FakeMasterDataStore : IInspectionMasterDataStore
    {
        private static readonly InspectionPlan Plan = new()
        {
            PlanId = "lens-quality",
            Version = 1,
            Name = "镜片质量方案",
            Status = InspectionPlanStatuses.Published,
            Scope = new InspectionPlanScope { ProductFamilyCode = "lens-a" },
            Items = [new InspectionPlanItem { DefinitionCode = "lens-final", DefinitionVersion = 1, Required = true }]
        };

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<InspectionDefinition> UpsertInspectionDefinitionAsync(InspectionDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InspectionDefinition>> ListInspectionDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionDefinition>>([]);
        public Task<InspectionDefinition?> GetInspectionDefinitionAsync(string code, int version, CancellationToken ct = default) => Task.FromResult<InspectionDefinition?>(null);
        public Task<bool> DeleteInspectionDefinitionAsync(string code, int version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<InspectionPlan> UpsertInspectionPlanAsync(InspectionPlan plan, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<InspectionPlan>> ListInspectionPlansAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<InspectionPlan>>([Plan]);
        public Task<InspectionPlan?> GetInspectionPlanAsync(string planId, int version, CancellationToken ct = default) => Task.FromResult<InspectionPlan?>(Plan);
        public Task<bool> DeleteInspectionPlanAsync(string planId, int version, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PhaseDefinition> UpsertPhaseDefinitionAsync(PhaseDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhaseDefinition>> ListPhaseDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PhaseDefinition>>([]);
        public Task<PhaseDefinition?> GetPhaseDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult<PhaseDefinition?>(null);
        public Task<bool> DeletePhaseDefinitionAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PhaseMapping> UpsertPhaseMappingAsync(PhaseMapping mapping, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PhaseMapping>> ListPhaseMappingsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<PhaseMapping>>([]);
        public Task<PhaseMapping?> GetPhaseMappingAsync(string mappingId, CancellationToken ct = default) => Task.FromResult<PhaseMapping?>(null);
        public Task<bool> DeletePhaseMappingAsync(string mappingId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<FeatureDefinition> UpsertFeatureDefinitionAsync(FeatureDefinition definition, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<FeatureDefinition>> ListFeatureDefinitionsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FeatureDefinition>>([]);
        public Task<FeatureDefinition?> GetFeatureDefinitionAsync(string code, CancellationToken ct = default) => Task.FromResult<FeatureDefinition?>(null);
        public Task<bool> DeleteFeatureDefinitionAsync(string code, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeProcessConfigurationStore(ScenarioPackage scenario) : IProcessConfigurationStore
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ProcessDataModel> UpsertDataModelAsync(ProcessDataModel value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessDataModel>> ListDataModelsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessDataModel?> GetDataModelAsync(string modelId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteDataModelAsync(string modelId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessSpecification> UpsertProcessSpecificationAsync(ProcessSpecification value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessSpecification>> ListProcessSpecificationsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessSpecification?> GetProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteProcessSpecificationAsync(string processSpecificationId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessAnalysisPlan> UpsertAnalysisPlanAsync(ProcessAnalysisPlan value, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<ProcessAnalysisPlan>> ListAnalysisPlansAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ProcessAnalysisPlan?> GetAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> DeleteAnalysisPlanAsync(string planId, int version, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ScenarioPackage?> GetScenarioPackageAsync(
            string packageId,
            int version,
            CancellationToken ct = default)
            => Task.FromResult<ScenarioPackage?>(
                packageId == scenario.PackageId && version == scenario.Version ? scenario : null);
    }

    private sealed class FakeInspectionStore(IReadOnlyList<InspectionRecord> records) :
        IInspectionRecordStore
    {
        public int BatchQueryCount { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<StoreInspectionRecordResult> CreateAsync(
            CreateInspectionRecordRequest request,
            bool submitterVerified,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InspectionRecord?> GetAsync(
            Guid recordId,
            CancellationToken ct = default)
            => Task.FromResult(records.FirstOrDefault(value => value.RecordId == recordId));

        public Task<InspectionRecord?> GetCorrectionForAsync(Guid recordId, CancellationToken ct = default)
            => Task.FromResult(records.FirstOrDefault(value => value.SupersedesRecordId == recordId));

        public Task<IReadOnlyList<InspectionScope>> ListScopesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionScope>>([]);

        public Task<InspectionScope?> GetScopeAsync(string scopeId, CancellationToken ct = default)
            => Task.FromResult<InspectionScope?>(null);

        public Task<InspectionScope> UpsertScopeAsync(InspectionScope scope, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteScopeAsync(string scopeId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<InspectionRecord>> QueryAsync(
            InspectionRecordQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionRecord>>(records);

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
            CancellationToken ct = default)
        {
            BatchQueryCount++;
            return Task.FromResult<IReadOnlyList<InspectionRecord>>(
                records.Where(value => executionIds.Contains(value.ExecutionId)).ToArray());
        }
    }
}
