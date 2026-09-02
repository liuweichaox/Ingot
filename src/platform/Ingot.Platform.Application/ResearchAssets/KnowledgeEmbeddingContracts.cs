using Ingot.Contracts.ResearchAssets;
using System.Security.Cryptography;
using System.Text;

namespace Ingot.Platform.Application.ResearchAssets;

/// <summary>模型无关的知识片段嵌入端口；密钥和 HTTP Provider 保持在 Infrastructure 层。</summary>
public interface IKnowledgeEmbeddingClient
{
    bool IsConfigured { get; }
    string Model { get; }
    int Dimensions { get; }

    Task<KnowledgeEmbedding> EmbedAsync(string content, CancellationToken ct = default);
}

public sealed record KnowledgeEmbedding
{
    public required string Model { get; init; }
    public required IReadOnlyList<float> Values { get; init; }
}

public static class KnowledgeContentFingerprint
{
    public static string Normalize(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    public static string ComputeHash(string content)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(content))));

    public static KnowledgeRecord NormalizeAndStamp(KnowledgeRecord record)
    {
        var content = Normalize(record.Content);
        return record with
        {
            Content = content,
            Citation = record.Citation is null
                ? null
                : record.Citation with { ContentHash = ComputeHash(content) }
        };
    }
}

public interface IKnowledgeEmbeddingQueue
{
    Task EnqueueAsync(Guid sourceId, string requestedBy, CancellationToken ct = default);
}

public interface IKnowledgeEmbeddingJobStore : IKnowledgeEmbeddingQueue
{
    Task<int> EnqueueMissingAsync(CancellationToken ct = default);
    Task<KnowledgeEmbeddingJob?> ClaimAsync(TimeSpan leaseTimeout, CancellationToken ct = default);
    Task<bool> RenewLeaseAsync(KnowledgeEmbeddingJob job, CancellationToken ct = default);
    Task<bool> CompleteAsync(KnowledgeEmbeddingJob job, CancellationToken ct = default);
    Task<KnowledgeEmbeddingFailureDisposition?> FailAsync(
        KnowledgeEmbeddingJob job,
        string error,
        bool retryable,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken ct = default);
    Task<bool> UpsertAsync(
        KnowledgeEmbeddingJob job,
        KnowledgeRecord record,
        KnowledgeEmbedding embedding,
        CancellationToken ct = default);
}

public sealed record KnowledgeEmbeddingJob(
    Guid SourceId,
    string RequestedBy,
    string Model,
    Guid LeaseId,
    long LeaseGeneration,
    int AttemptCount);

public enum KnowledgeEmbeddingFailureDisposition
{
    RetryScheduled,
    DeadLettered
}
