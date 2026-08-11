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
        var inspections = new FakeInspectionStore(
        [
            new InspectionRecord
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
            }
        ]);
        var executionService = new FakeProcessExecutionService(execution);
        var scenario = ResearchContextAdmissionEvaluatorTests.OpticalScenario();
        var assembler = new ResearchObservationAssembler(
            executionService,
            inspections,
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

        var missingMoldProcessExecution = execution with
        {
            Context = execution.Context
                .Where(static pair => pair.Key != "tooling_assembly_id")
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal)
        };
        var missingMoldResult = await new ResearchObservationAssembler(
                new FakeProcessExecutionService(missingMoldProcessExecution),
                inspections,
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

        public Task<IReadOnlyList<InspectionRecord>> QueryAsync(
            InspectionRecordQuery query,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InspectionRecord>>(records);

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
