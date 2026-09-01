using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessResearch;

namespace Ingot.Platform.Application.ProcessResearch;

// Freezes project-scoped evidence so downstream decisions retain their original engineering context.
internal static class ResearchProjectEvidenceSnapshots
{
    public static ResearchProjectEvidenceSnapshot Freeze(ResearchProject project)
        => new()
        {
            ProjectId = project.ProjectId,
            Revision = project.Revision,
            Code = project.Code,
            Name = project.Name,
            ProcessName = project.ProcessName,
            ProductName = project.ProductName,
            MaterialName = project.MaterialName,
            SiteCode = project.SiteCode,
            Variables = project.Variables.OrderBy(static value => value.Code, StringComparer.Ordinal).ToArray(),
            Objectives = project.Objectives.OrderBy(static value => value.Code, StringComparer.Ordinal).ToArray(),
            Constraints = project.Constraints.OrderBy(static value => value.Code, StringComparer.Ordinal).ToArray(),
            OutcomeConstraints = project.OutcomeConstraints
                .OrderBy(static value => value.Code, StringComparer.Ordinal).ToArray(),
            OptimizationFeatures = project.OptimizationFeatures with
            {
                DerivedFeatures = project.OptimizationFeatures.DerivedFeatures
                    .OrderBy(static value => value.Name, StringComparer.Ordinal).ToArray()
            },
            Context = project.Context.OrderBy(static value => value.Key, StringComparer.Ordinal)
                .ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal)
        };

    public static string Hash(ResearchProjectEvidenceSnapshot snapshot)
        => Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(snapshot)));

    public static ResearchProject Restore(ResearchProjectEvidenceSnapshot snapshot)
        => new()
        {
            ProjectId = snapshot.ProjectId,
            Code = snapshot.Code,
            Name = snapshot.Name,
            ProcessName = snapshot.ProcessName,
            ProductName = snapshot.ProductName,
            MaterialName = snapshot.MaterialName,
            Status = ResearchProjectStatuses.Active,
            Variables = snapshot.Variables,
            Objectives = snapshot.Objectives,
            Constraints = snapshot.Constraints,
            OutcomeConstraints = snapshot.OutcomeConstraints,
            OptimizationFeatures = snapshot.OptimizationFeatures,
            Context = snapshot.Context,
            SiteCode = snapshot.SiteCode,
            Revision = snapshot.Revision
        };
}
