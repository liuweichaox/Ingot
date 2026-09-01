// 提供日常配方建议闭环测试使用的真实运行观察桩。
using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;

namespace Ingot.Core.Tests.Platform;

public abstract partial class ProcessResearchWorkflowTestBase
{
    protected sealed class StubObservationAssembler(ResearchRunObservation? observation)
        : IResearchObservationAssembler
    {
        public string? RequestedExecutionKey { get; private set; }

        public Task<ResearchObservationAssembly> AssembleProductionRunsAsync(
            ResearchProject project,
            CancellationToken ct = default)
            => Task.FromResult(new ResearchObservationAssembly(
                observation is null ? [] : [observation], observation is null ? 0 : 1));

        public Task<ResearchObservationAssembly> AssembleProductionRunAsync(
            ResearchProject project,
            string executionKey,
            CancellationToken ct = default)
        {
            RequestedExecutionKey = executionKey;
            return Task.FromResult(new ResearchObservationAssembly(
                observation is null ? [] : [observation], observation is null ? 0 : 1));
        }
    }
}
