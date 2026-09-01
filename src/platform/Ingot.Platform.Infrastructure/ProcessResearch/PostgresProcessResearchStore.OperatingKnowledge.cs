using Ingot.Contracts.ProcessResearch;
using Ingot.Platform.Application.ProcessResearch;

namespace Ingot.Platform.Infrastructure.ProcessResearch;

// Persists operating-region and knowledge records without reintroducing retired validation workflow state.
public sealed partial class PostgresProcessResearchStore
{
    public Task<ResearchOperatingRegion?> GetOperatingRegionAsync(
        Guid operatingRegionId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchOperatingRegion>(
            "SELECT payload FROM research_operating_regions WHERE operating_region_id = $1",
            operatingRegionId,
            ct);

    public Task<IReadOnlyList<ResearchOperatingRegion>> ListOperatingRegionsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchOperatingRegion>(
            """
            SELECT payload
            FROM research_operating_regions
            WHERE project_id = $1
            ORDER BY updated_at DESC, operating_region_id
            """,
            projectId,
            ct);

    public async Task<ResearchOperatingRegion> SaveOperatingRegionAsync(
        ResearchOperatingRegion value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SaveChildAsync(
            connection,
            transaction,
            """
            INSERT INTO research_operating_regions
              (operating_region_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (operating_region_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """,
            value.OperatingRegionId,
            value.ProjectId,
            value.Status,
            value,
            value.CreatedAt,
            value.UpdatedAt,
            ct).ConfigureAwait(false);
        await SyncEvidenceAsync(
            connection,
            transaction,
            "operating-region",
            value.OperatingRegionId.ToString(),
            value.Evidence,
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<ResearchKnowledgeClaim?> GetKnowledgeClaimAsync(
        Guid claimId,
        CancellationToken ct = default)
        => GetOneAsync<ResearchKnowledgeClaim>(
            "SELECT payload FROM research_knowledge_claims WHERE claim_id = $1",
            claimId,
            ct);

    public Task<IReadOnlyList<ResearchKnowledgeClaim>> ListKnowledgeClaimsAsync(
        Guid projectId,
        CancellationToken ct = default)
        => ListAsync<ResearchKnowledgeClaim>(
            """
            SELECT payload
            FROM research_knowledge_claims
            WHERE project_id = $1
            ORDER BY updated_at DESC, claim_id
            """,
            projectId,
            ct);

    public async Task<ResearchKnowledgeClaim> SaveKnowledgeClaimAsync(
        ResearchKnowledgeClaim value,
        CancellationToken ct = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SaveChildAsync(
            connection,
            transaction,
            """
            INSERT INTO research_knowledge_claims
              (claim_id, project_id, status, payload, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6)
            ON CONFLICT (claim_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at
            """,
            value.ClaimId,
            value.ProjectId,
            value.Status,
            value,
            value.CreatedAt,
            value.UpdatedAt,
            ct).ConfigureAwait(false);
        await SyncEvidenceAsync(
            connection,
            transaction,
            "knowledge-claim",
            value.ClaimId.ToString(),
            value.Evidence,
            ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return value;
    }
}
