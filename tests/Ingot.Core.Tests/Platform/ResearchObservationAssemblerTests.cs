using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Contracts.Inspections;
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Infrastructure.Cycles;
using Ingot.Platform.Infrastructure.Inspections;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ResearchObservationAssemblerTests
{
    [Fact]
    public async Task Assemble_ConnectsCycleFeaturesRecipeAndInspectionMeasurements()
    {
        var runKey = "bo-cycle-001";
        var cycle = new CycleComparisonRow
        {
            CorrelationId = runKey,
            MachineId = "FX3U-01",
            Context = new Dictionary<string, string>
            {
                ["material_lot"] = "GLASS-LOT-07",
                ["mold_id"] = "MOLD-A"
            },
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
            CompletedAt = DateTimeOffset.UtcNow,
            ProductSeries = "lens-a",
            ProcessDataQuality = new ProcessDataQualitySummary
            {
                Status = ProcessDataStatuses.Available,
                SampleCount = 120
            },
            RecipeParameters =
            [
                new CycleRecipeParameter
                {
                    Code = "holding-temperature",
                    Unit = "Cel",
                    Value = JsonSerializer.SerializeToElement(512d)
                }
            ],
            Signals =
            [
                new CycleSignalStatistic
                {
                    Code = "mold-temperature",
                    Name = "模具温度",
                    Unit = "Cel",
                    Average = 510,
                    Features =
                    [
                        new CycleSignalFeature
                        {
                            Code = "overshoot",
                            DefinitionHash = new string('a', 64),
                            ComputationHash = new string('b', 64),
                            Value = 2.4
                        }
                    ]
                }
            ],
            AnalysisMaterialization = new CycleAnalysisMaterialization
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
                WorkpieceId = "lens-001",
                OperationRunId = runKey,
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
        var cycleService = new FakeCycleService(cycle);
        var assembler = new ResearchObservationAssembler(cycleService, inspections);
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
                    DataSource = "recipe:holding-temperature"
                }
            ],
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
                    RunKey = runKey,
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

        Assert.Equal(1, cycleService.BatchQueryCount);
        Assert.Equal(1, inspections.BatchQueryCount);

        var observation = Assert.Single(result.Observations);
        Assert.True(observation.ValidForOptimization);
        Assert.Equal(512, Assert.Single(observation.ActualFactors).Value);
        Assert.Equal(0.38, observation.Outcomes["form"], 6);
        Assert.Equal(0.01, observation.ConstraintOutcomes["crack-safety"], 6);
        Assert.Equal(2.4, observation.ProcessFeatures["mold-temperature.cycle.overshoot"], 6);
        Assert.Equal("FX3U-01", observation.Context["machine_id"]);
        Assert.Equal("GLASS-LOT-07", observation.Context["material_lot"]);
        Assert.Equal("MOLD-A", observation.Context["mold_id"]);
        Assert.Matches("^[a-f0-9]{64}$", observation.SourceContentHash);

        var strictProject = project with
        {
            Variables =
            [
                project.Variables[0] with { DataSource = "recipe:not-collected" }
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

        var wrongUnitCycle = cycle with
        {
            RecipeParameters = [cycle.RecipeParameters[0] with { Unit = "K" }]
        };
        var wrongUnitAssembler = new ResearchObservationAssembler(
            new FakeCycleService(wrongUnitCycle),
            inspections);
        var wrongUnitResult = await wrongUnitAssembler.AssembleAsync(project, [experiment]);
        var unitConflict = Assert.Single(wrongUnitResult.Observations);
        Assert.False(unitConflict.ValidForOptimization);
        Assert.Empty(unitConflict.ActualFactors);
        Assert.Contains("单位冲突", unitConflict.ExclusionReason);
    }

    private sealed class FakeCycleService(CycleComparisonRow cycle) : ICycleComparisonService
    {
        public int BatchQueryCount { get; private set; }

        public Task<CycleComparisonRow?> GetCycleAsync(
            string correlationId,
            CancellationToken ct = default)
            => Task.FromResult<CycleComparisonRow?>(
                string.Equals(correlationId, cycle.CorrelationId, StringComparison.Ordinal)
                    ? cycle
                    : null);

        public Task<IReadOnlyDictionary<string, CycleComparisonRow>> GetCyclesAsync(
            IReadOnlyCollection<string> correlationIds,
            CancellationToken ct = default)
        {
            BatchQueryCount++;
            IReadOnlyDictionary<string, CycleComparisonRow> result = correlationIds
                .Where(id => string.Equals(id, cycle.CorrelationId, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .ToDictionary(static id => id, _ => cycle, StringComparer.Ordinal);
            return Task.FromResult(result);
        }

        public Task<CycleComparisonResult?> CompareWithHistoryAsync(
            string correlationId,
            int limit,
            CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<CycleComparisonResult?> CompareSelectedAsync(
            string baselineCycleId,
            IReadOnlyList<string> cycleIds,
            CancellationToken ct = default)
            => throw new NotSupportedException();
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

        public Task<IReadOnlyList<InspectionRecord>> QueryAllByOperationRunIdsAsync(
            IReadOnlyCollection<string> operationRunIds,
            CancellationToken ct = default)
        {
            BatchQueryCount++;
            return Task.FromResult<IReadOnlyList<InspectionRecord>>(
                records.Where(value => operationRunIds.Contains(value.OperationRunId)).ToArray());
        }
    }
}
