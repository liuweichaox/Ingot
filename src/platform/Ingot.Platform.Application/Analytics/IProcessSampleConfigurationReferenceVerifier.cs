using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.Analytics;

/// <summary>Checks reported sample configuration references against site-scoped configuration metadata.</summary>
public interface IProcessSampleConfigurationReferenceVerifier
{
    /// <summary>Returns verification-enriched evidence in the same order without adding deployment or causal claims.</summary>
    Task<IReadOnlyList<ProcessSampleConfigurationEvidence>> VerifyAsync(
        string siteId,
        IReadOnlyList<ProcessSampleConfigurationEvidence> evidence,
        CancellationToken ct = default);
}
