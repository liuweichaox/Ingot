// 提供流程测试使用的可靠性与执行比较桩。
using System.Text.Json;
using Ingot.Contracts.Analytics;
using Ingot.Contracts.Events;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Contracts.ProcessResearch;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Api.Controllers;
using Ingot.Platform.Application.Analytics;
using Ingot.Platform.Application.ProcessConfiguration;
using Ingot.Platform.Application.ProcessExecutions;
using Ingot.Platform.Application.ProcessResearch;
using Ingot.Platform.Application.ResearchAssets;
using Ingot.Platform.Infrastructure.Analytics;
using Ingot.Platform.Infrastructure.ProcessConfiguration;
using Ingot.Platform.Infrastructure.ProcessResearch;
using Ingot.Platform.Infrastructure.ResearchAssets;
using Xunit;

namespace Ingot.Core.Tests.Platform;

public abstract partial class ProcessResearchWorkflowTestBase
{
    protected sealed class StubReliabilityBaselineService : IDataReliabilityBaselineService
    {
        public Task<DataReliabilityBaseline> CalculateAsync(
            DataReliabilityBaselineQuery query,
            CancellationToken ct = default)
            => Task.FromResult(new DataReliabilityBaseline
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                From = query.From,
                To = query.To,
                EdgeId = query.EdgeId,
                EquipmentId = query.EquipmentId,
                MatchingCompletedRunCount = 12,
                AnalyzedRunCount = 12,
                Rates =
                [
                    new ReliabilityRate
                    {
                        Code = "analysis_admission",
                        Name = "正式分析准入率",
                        Numerator = 9,
                        Denominator = 12,
                        Rate = 0.75,
                        Definition = "测试快照"
                    }
                ]
            });
    }

    protected sealed class RejectingExecutionComparisonService : IExecutionComparisonService
    {
        public int CallCount { get; private set; }

        public Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
            string executionId,
            CancellationToken ct = default,
            string? siteId = null)
        {
            CallCount++;
            return Task.FromResult<ExecutionComparisonRow?>(null);
        }

        public Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null)
        {
            CallCount++;
            return Task.FromResult<IReadOnlyDictionary<string, ExecutionComparisonRow>>(
                new Dictionary<string, ExecutionComparisonRow>());
        }

        public Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
            string executionId,
            int limit,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
        {
            CallCount++;
            return Task.FromResult<ExecutionComparisonResult?>(null);
        }

        public Task<ExecutionComparisonResult?> CompareSelectedAsync(
            string baselineProcessExecutionId,
            IReadOnlyList<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
        {
            CallCount++;
            return Task.FromResult<ExecutionComparisonResult?>(null);
        }
    }

    protected sealed class FixedExecutionComparisonService(ExecutionComparisonResult result)
        : IExecutionComparisonService
    {
        public Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
            string executionId,
            CancellationToken ct = default,
            string? siteId = null)
            => Task.FromResult<ExecutionComparisonRow?>(
                executionId == result.Baseline.ExecutionId ? result.Baseline : null);

        public Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null)
            => Task.FromResult<IReadOnlyDictionary<string, ExecutionComparisonRow>>(
                new[] { result.Baseline }.Concat(result.HistoricalProcessExecutions)
                    .Where(row => executionIds.Contains(row.ExecutionId))
                    .ToDictionary(row => row.ExecutionId, StringComparer.Ordinal));

        public Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
            string executionId,
            int limit,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
            => Task.FromResult<ExecutionComparisonResult?>(result);

        public Task<ExecutionComparisonResult?> CompareSelectedAsync(
            string baselineProcessExecutionId,
            IReadOnlyList<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
            => Task.FromResult<ExecutionComparisonResult?>(result);
    }

    protected sealed class MutableExecutionComparisonService : IExecutionComparisonService
    {
        private readonly Dictionary<string, ExecutionComparisonRow> rows =
            new(StringComparer.Ordinal);

        public void Set(ExecutionComparisonRow row) => rows[row.ExecutionId] = row;

        public Task<ExecutionComparisonRow?> GetProcessExecutionAsync(
            string executionId,
            CancellationToken ct = default,
            string? siteId = null)
            => Task.FromResult(rows.GetValueOrDefault(executionId));

        public Task<IReadOnlyDictionary<string, ExecutionComparisonRow>> GetProcessExecutionsAsync(
            IReadOnlyCollection<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null)
            => Task.FromResult<IReadOnlyDictionary<string, ExecutionComparisonRow>>(
                rows.Where(value => executionIds.Contains(value.Key, StringComparer.Ordinal))
                    .ToDictionary(static value => value.Key, static value => value.Value,
                        StringComparer.Ordinal));

        public Task<ExecutionComparisonResult?> CompareWithHistoryAsync(
            string executionId,
            int limit,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
            => Task.FromResult<ExecutionComparisonResult?>(null);

        public Task<ExecutionComparisonResult?> CompareSelectedAsync(
            string baselineProcessExecutionId,
            IReadOnlyList<string> executionIds,
            CancellationToken ct = default,
            string? siteId = null,
            IReadOnlyList<string>? additionalKnownUnmeasuredConfounders = null)
            => Task.FromResult<ExecutionComparisonResult?>(null);
    }

    protected static ExecutionComparisonResult Comparison(
        string readinessMode,
        double crossValidationScore,
        string candidateEvidenceLevel)
    {
        var row = new ExecutionComparisonRow
        {
            ExecutionId = "run-a",
            EquipmentId = "press-01",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow,
            ProductFamilyCode = "lens-a"
        };
        return new ExecutionComparisonResult
        {
            BaselineProcessExecutionId = row.ExecutionId,
            ProductFamilyCode = row.ProductFamilyCode,
            Baseline = row,
            HistoricalProcessExecutions = [row with { ExecutionId = "run-b" }],
            Acceptance = new ExecutionComparisonAcceptance { ProcessExecutionCount = 2 },
            Diagnosis = new ExecutionDiagnosisSummary
            {
                EvidenceLevel = candidateEvidenceLevel,
                CrossValidationScore = crossValidationScore,
                Readiness = new ExecutionAnalysisReadiness { Mode = readinessMode },
                Candidates =
                [
                    new ExecutionCauseCandidate
                    {
                        CandidateId = "control-parameter:holding-temperature",
                        SourceKind = ExecutionCauseSourceKinds.ProcessSpecificationParameter,
                        Actionability = ExecutionCauseActionability.Controllable,
                        VariableCode = "holding-temperature",
                        DataSource = "control-parameter:holding-temperature",
                        DisplayName = "保压温度",
                        MedianDifference = 2.5,
                        EvidenceLevel = candidateEvidenceLevel,
                        CandidateScore = 0.8
                    }
                ]
            }
        };
    }
}
