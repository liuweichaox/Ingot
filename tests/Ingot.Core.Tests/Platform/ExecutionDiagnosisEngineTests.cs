// 验证平台组件 ExecutionDiagnosisEngine 的成功、拒绝和安全边界。

using System.Text.Json;
using Ingot.Contracts.Events;
using Ingot.Platform.Infrastructure.ProcessExecutions;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public sealed class ExecutionDiagnosisEngineTests
{
    [Fact]
    public void Analyze_RanksActualProcessSpecificationAndProcessFeatureCandidates()
    {
        var rows = new[]
        {
            ProcessExecution("PASS-1", "PASS", 500, 1.0, "PRESS-A"),
            ProcessExecution("PASS-2", "PASS", 501, 1.1, "PRESS-A"),
            ProcessExecution("PASS-3", "PASS", 502, 1.2, "PRESS-A"),
            ProcessExecution("FAIL-1", "FAIL", 515, 3.5, "PRESS-A"),
            ProcessExecution("FAIL-2", "FAIL", 516, 3.6, "PRESS-A"),
            ProcessExecution("FAIL-3", "FAIL", 517, 3.7, "PRESS-A")
        };

        var result = new ExecutionDiagnosisEngine().Analyze(rows);

        Assert.Equal(ExecutionDiagnosisEngine.AlgorithmVersion, result.AlgorithmVersion);
        Assert.Equal("exploratory", result.EvidenceLevel);
        Assert.Equal(3, result.PassProcessExecutionCount);
        Assert.Equal(3, result.FailProcessExecutionCount);
        var processSpecification = Assert.Single(result.Candidates, candidate =>
            candidate.SourceKind == ExecutionCauseSourceKinds.ProcessSpecificationParameter);
        Assert.Equal("control-parameter:holding-temperature", processSpecification.DataSource);
        Assert.Equal(ExecutionCauseActionability.Controllable, processSpecification.Actionability);
        Assert.Equal(501d, processSpecification.PassMedian);
        Assert.Equal(516d, processSpecification.FailMedian);
        Assert.True(processSpecification.CandidateScore > 0);
        var feature = Assert.Single(result.Candidates, candidate =>
            candidate.SourceKind == ExecutionCauseSourceKinds.ProcessFeature);
        Assert.Equal("signal:mold-temperature:overshoot:press", feature.DataSource);
        Assert.Equal(ExecutionCauseActionability.Observable, feature.Actionability);
    }

    [Fact]
    public void Analyze_ExposesContextDifferencesInsteadOfClaimingCausation()
    {
        var rows = new[]
        {
            ProcessExecution("PASS-1", "PASS", 500, 1.0, "PRESS-A"),
            ProcessExecution("PASS-2", "PASS", 501, 1.1, "PRESS-A"),
            ProcessExecution("FAIL-1", "FAIL", 515, 3.5, "PRESS-B"),
            ProcessExecution("FAIL-2", "FAIL", 516, 3.6, "PRESS-B")
        };

        var result = new ExecutionDiagnosisEngine().Analyze(rows);

        Assert.All(result.Candidates, candidate =>
            Assert.Contains("equipment_id", candidate.PossibleConfounders));
        Assert.Contains(result.Limitations, limitation =>
            limitation.Contains("混杂", StringComparison.Ordinal));
    }

    private static ExecutionComparisonRow ProcessExecution(
        string id,
        string outcome,
        double holdingTemperature,
        double overshoot,
        string equipmentId)
        => new()
        {
            ExecutionId = id,
            EquipmentId = equipmentId,
            StartedAt = DateTimeOffset.Parse("2026-07-20T08:00:00Z"),
            CompletedAt = DateTimeOffset.Parse("2026-07-20T08:10:00Z"),
            ProductFamilyCode = "lens-a",
            EvidenceWeight = 1,
            InspectionOutcomes = [outcome],
            ProcessSpecificationId = "lens-molding",
            ProcessSpecificationVersion = "1",
            ControlParameters =
            [
                new ExecutionControlParameterValue
                {
                    Code = "holding-temperature",
                    Name = "保压温度",
                    Unit = "Cel",
                    Value = JsonSerializer.SerializeToElement(holdingTemperature)
                }
            ],
            Signals =
            [
                new ProcessSignalStatistic
                {
                    Code = "mold-temperature",
                    Name = "模具温度",
                    Unit = "Cel",
                    Features =
                    [
                        new ProcessSignalFeature
                        {
                            Code = "overshoot",
                            PhaseCode = "press",
                            PhaseName = "压制",
                            PhaseOrder = 1,
                            Value = overshoot
                        }
                    ]
                }
            ]
        };
}
