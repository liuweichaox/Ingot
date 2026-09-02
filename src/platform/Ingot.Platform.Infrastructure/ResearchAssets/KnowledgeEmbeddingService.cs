using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ingot.Agent;
using Ingot.Contracts.ResearchAssets;
using Ingot.Platform.Application.ResearchAssets;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ResearchAssets;

public sealed class KnowledgeEmbeddingOptions
{
    public bool Enabled { get; init; }
    public string Model { get; init; } = "text-embedding-3-small";
    public int Dimensions { get; init; } = 1536;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>复用受保护的模型服务凭据访问 OpenAI-compatible embeddings 端点。</summary>
public sealed class OpenAiCompatibleKnowledgeEmbeddingClient(
    IHttpClientFactory httpClientFactory,
    IModelServiceConfigurationProvider configurationProvider,
    IOptions<KnowledgeEmbeddingOptions> options) : IKnowledgeEmbeddingClient
{
    private readonly KnowledgeEmbeddingOptions _options = options.Value;

    public bool IsConfigured
    {
        get
        {
            var settings = configurationProvider.Current;
            return _options.Enabled && _options.Dimensions == 1536 && settings.Enabled &&
                   !string.IsNullOrWhiteSpace(settings.ApiKey) &&
                   Uri.TryCreate(settings.BaseUrl?.TrimEnd('/') + "/embeddings", UriKind.Absolute, out var endpoint) &&
                   endpoint.Scheme is "http" or "https";
        }
    }

    public string Model => _options.Model.Trim();
    public int Dimensions => _options.Dimensions;

    public async Task<KnowledgeEmbedding> EmbedAsync(string content, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("知识嵌入服务尚未配置或未启用。");
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("知识片段不能为空。", nameof(content));
        if (content.Length > 24_000)
            throw new ArgumentException("知识片段超过嵌入服务的安全长度上限。", nameof(content));

        var settings = configurationProvider.Current;
        var endpoint = new Uri(settings.BaseUrl!.TrimEnd('/') + "/embeddings", UriKind.Absolute);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        request.Content = JsonContent.Create(new { model = Model, input = content });
        var client = httpClientFactory.CreateClient("knowledge-embeddings");
        using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"知识嵌入服务返回 HTTP {(int)response.StatusCode}。", null, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array ||
            data.GetArrayLength() != 1 || !data[0].TryGetProperty("embedding", out var vector) ||
            vector.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("知识嵌入响应缺少单个 embedding 向量。");
        var values = vector.EnumerateArray().Select(static item => item.GetSingle()).ToArray();
        if (values.Length != Dimensions || values.Any(static value => !float.IsFinite(value)))
            throw new InvalidDataException($"知识嵌入维度必须为 {Dimensions} 且所有值必须有限。");
        return new KnowledgeEmbedding { Model = Model, Values = values };
    }
}

public sealed class PostgresKnowledgeEmbeddingJobStore(
    NpgsqlDataSource dataSource,
    IKnowledgeEmbeddingClient embeddings) : IKnowledgeEmbeddingJobStore
{
    public async Task EnqueueAsync(Guid sourceId, string requestedBy, CancellationToken ct = default)
    {
        if (!embeddings.IsConfigured)
            return;
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO knowledge_embedding_jobs(
              source_id, requested_by, embedding_model, status, available_at, updated_at)
            VALUES(@source_id, @requested_by, @embedding_model, 'queued', now(), now())
            ON CONFLICT(source_id) DO UPDATE SET
              requested_by=EXCLUDED.requested_by,
              embedding_model=EXCLUDED.embedding_model,
              status='queued', attempt_count=0, available_at=now(), lease_id=NULL, leased_at=NULL,
              last_error=NULL, updated_at=now();
            """);
        command.Parameters.AddWithValue("source_id", sourceId);
        command.Parameters.AddWithValue("requested_by", requestedBy);
        command.Parameters.AddWithValue("embedding_model", embeddings.Model);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> EnqueueMissingAsync(CancellationToken ct = default)
    {
        if (!embeddings.IsConfigured)
            return 0;
        await RefreshTrustedContentHashesAsync(ct).ConfigureAwait(false);
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO knowledge_embedding_jobs(
              source_id, requested_by, embedding_model, status, available_at, updated_at)
            SELECT source.source_id, 'embedding-backfill', @embedding_model, 'queued', now(), now()
            FROM knowledge_sources source
            WHERE source.status='reviewed'
              AND EXISTS (
                SELECT 1 FROM knowledge_fragments fragment
                LEFT JOIN knowledge_fragment_embeddings embedding ON embedding.record_id=fragment.record_id
                WHERE fragment.source_id=source.source_id AND fragment.human_reviewed
                  AND (embedding.record_id IS NULL OR embedding.embedding_model <> @embedding_model
                    OR embedding.content_hash <> COALESCE(fragment.content_hash, '')))
            ON CONFLICT(source_id) DO UPDATE SET
              requested_by=EXCLUDED.requested_by, embedding_model=EXCLUDED.embedding_model,
              status='queued', attempt_count=0, available_at=now(), lease_id=NULL, leased_at=NULL,
              last_error=NULL, updated_at=now()
            WHERE knowledge_embedding_jobs.status IN ('completed', 'dead-letter')
               OR knowledge_embedding_jobs.embedding_model <> EXCLUDED.embedding_model;
            """);
        command.Parameters.AddWithValue("embedding_model", embeddings.Model);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task RefreshTrustedContentHashesAsync(CancellationToken ct)
    {
        Guid? cursor = null;
        while (true)
        {
            await using var read = dataSource.CreateCommand(
                """
                SELECT fragment.record_id, fragment.content, fragment.content_hash
                FROM knowledge_fragments fragment
                JOIN knowledge_sources source ON source.source_id=fragment.source_id
                WHERE source.status='reviewed' AND fragment.human_reviewed
                  AND (@cursor IS NULL OR fragment.record_id > @cursor)
                ORDER BY fragment.record_id
                LIMIT 500;
                """);
            AddNullableGuid(read, "cursor", cursor);
            var page = new List<(Guid RecordId, string Content, string? ContentHash)>(500);
            await using (var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                    page.Add((reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
            if (page.Count == 0)
                return;

            foreach (var record in page)
            {
                var trustedHash = KnowledgeContentFingerprint.ComputeHash(record.Content);
                if (string.Equals(record.ContentHash, trustedHash, StringComparison.Ordinal))
                    continue;
                await using var update = dataSource.CreateCommand(
                    """
                    UPDATE knowledge_fragments SET content_hash=@content_hash
                    WHERE record_id=@record_id AND content=@content
                      AND content_hash IS DISTINCT FROM @content_hash;
                    """);
                update.Parameters.AddWithValue("record_id", record.RecordId);
                update.Parameters.AddWithValue("content", record.Content);
                update.Parameters.AddWithValue("content_hash", trustedHash);
                await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            cursor = page[^1].RecordId;
        }
    }

    public async Task<KnowledgeEmbeddingJob?> ClaimAsync(TimeSpan leaseTimeout, CancellationToken ct = default)
    {
        var leaseId = Guid.CreateVersion7();
        await using var command = dataSource.CreateCommand(
            """
            WITH candidate AS (
              SELECT source_id FROM knowledge_embedding_jobs
              WHERE (status='queued' AND available_at <= now())
                 OR (status='running' AND leased_at < now() - @lease_timeout)
              ORDER BY available_at, updated_at
              FOR UPDATE SKIP LOCKED LIMIT 1)
            UPDATE knowledge_embedding_jobs job
            SET status='running', lease_id=@lease_id, lease_generation=job.lease_generation + 1,
                leased_at=now(), attempt_count=attempt_count + 1, updated_at=now()
            FROM candidate
            WHERE job.source_id=candidate.source_id
            RETURNING job.source_id, job.requested_by, job.embedding_model,
              job.lease_id, job.lease_generation, job.attempt_count;
            """);
        command.Parameters.AddWithValue("lease_timeout", leaseTimeout);
        command.Parameters.AddWithValue("lease_id", leaseId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new KnowledgeEmbeddingJob(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
                reader.GetInt64(4), reader.GetInt32(5))
            : null;
    }

    public async Task<bool> RenewLeaseAsync(KnowledgeEmbeddingJob job, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE knowledge_embedding_jobs SET leased_at=now(), updated_at=now()
            WHERE source_id=@source_id AND lease_id=@lease_id AND lease_generation=@generation
              AND status='running';
            """);
        BindLease(command, job);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<bool> CompleteAsync(KnowledgeEmbeddingJob job, CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE knowledge_embedding_jobs
            SET status='completed', lease_id=NULL, leased_at=NULL, last_error=NULL, updated_at=now()
            WHERE source_id=@source_id AND lease_id=@lease_id AND lease_generation=@generation
              AND status='running';
            """);
        BindLease(command, job);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    public async Task<KnowledgeEmbeddingFailureDisposition?> FailAsync(
        KnowledgeEmbeddingJob job,
        string error,
        bool retryable,
        int maxAttempts,
        TimeSpan retryDelay,
        CancellationToken ct = default)
    {
        await using var command = dataSource.CreateCommand(
            """
            UPDATE knowledge_embedding_jobs
            SET status=CASE WHEN @retryable AND attempt_count < @max_attempts THEN 'queued' ELSE 'dead-letter' END,
                available_at=CASE WHEN @retryable AND attempt_count < @max_attempts
                  THEN now() + @retry_delay ELSE available_at END,
                lease_id=NULL, leased_at=NULL, last_error=@error, updated_at=now()
            WHERE source_id=@source_id AND lease_id=@lease_id AND lease_generation=@generation
              AND status='running'
            RETURNING status;
            """);
        BindLease(command, job);
        command.Parameters.AddWithValue("retryable", retryable);
        command.Parameters.AddWithValue("max_attempts", Math.Max(1, maxAttempts));
        command.Parameters.AddWithValue("retry_delay", retryDelay);
        command.Parameters.AddWithValue("error", error[..Math.Min(error.Length, 1000)]);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is string status
            ? status == "queued"
                ? KnowledgeEmbeddingFailureDisposition.RetryScheduled
                : KnowledgeEmbeddingFailureDisposition.DeadLettered
            : null;
    }

    public async Task<bool> UpsertAsync(
        KnowledgeEmbeddingJob job,
        KnowledgeRecord record,
        KnowledgeEmbedding embedding,
        CancellationToken ct = default)
    {
        if (!string.Equals(job.Model, embedding.Model, StringComparison.Ordinal) ||
            embedding.Values.Count != embeddings.Dimensions)
            throw new InvalidDataException("嵌入模型或维度与任务契约不一致。");
        var trustedContentHash = KnowledgeContentFingerprint.ComputeHash(record.Content);
        await using var command = dataSource.CreateCommand(
            """
            WITH eligible AS (
              SELECT fragment.record_id, fragment.source_id
              FROM knowledge_embedding_jobs job
              JOIN knowledge_fragments fragment ON fragment.source_id=job.source_id
              JOIN knowledge_sources source ON source.source_id=fragment.source_id
              WHERE job.source_id=@source_id AND job.lease_id=@lease_id
                AND job.lease_generation=@generation AND job.status='running'
                AND fragment.record_id=@record_id AND fragment.content=@content
                AND source.status='reviewed' AND fragment.human_reviewed
            ), refreshed AS (
              UPDATE knowledge_fragments fragment SET content_hash=@content_hash
              FROM eligible
              WHERE fragment.record_id=eligible.record_id
              RETURNING fragment.record_id, fragment.source_id, fragment.content_hash)
            INSERT INTO knowledge_fragment_embeddings(
              record_id, source_id, content_hash, embedding_model, embedding_dimension, embedding, embedded_at)
            SELECT refreshed.record_id, refreshed.source_id, refreshed.content_hash,
              @embedding_model, @embedding_dimension, @embedding::vector, now()
            FROM refreshed
            ON CONFLICT(record_id) DO UPDATE SET
              source_id=EXCLUDED.source_id, content_hash=EXCLUDED.content_hash,
              embedding_model=EXCLUDED.embedding_model, embedding_dimension=EXCLUDED.embedding_dimension,
              embedding=EXCLUDED.embedding, embedded_at=EXCLUDED.embedded_at;
            """);
        BindLease(command, job);
        command.Parameters.AddWithValue("record_id", record.RecordId);
        command.Parameters.AddWithValue("content", record.Content);
        command.Parameters.AddWithValue("content_hash", trustedContentHash);
        command.Parameters.AddWithValue("embedding_model", embedding.Model);
        command.Parameters.AddWithValue("embedding_dimension", embedding.Values.Count);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(embedding.Values));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 1;
    }

    private static void BindLease(NpgsqlCommand command, KnowledgeEmbeddingJob job)
    {
        command.Parameters.AddWithValue("source_id", job.SourceId);
        command.Parameters.AddWithValue("lease_id", job.LeaseId);
        command.Parameters.AddWithValue("generation", job.LeaseGeneration);
    }

    private static void AddNullableGuid(NpgsqlCommand command, string name, Guid? value)
        => command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Uuid)
        { Value = value is null ? DBNull.Value : value.Value });

    internal static string ToVectorLiteral(IReadOnlyList<float> values)
        => "[" + string.Join(',', values.Select(static value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";
}

public sealed class KnowledgeEmbeddingWorkerOptions
{
    public TimeSpan LeaseTimeout { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxAttempts { get; init; } = 5;
    public TimeSpan InitialRetryDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromMinutes(30);
    public int RecordPageSize { get; init; } = 200;
    public TimeSpan ReconciliationInterval { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>异步生成嵌入，避免在 Chat 请求中调用外部模型服务。</summary>
public sealed class KnowledgeEmbeddingWorker(
    IResearchAssetStore assets,
    IKnowledgeEmbeddingJobStore jobs,
    IKnowledgeEmbeddingClient embeddings,
    IOptions<KnowledgeEmbeddingWorkerOptions> options,
    ILogger<KnowledgeEmbeddingWorker> logger) : BackgroundService
{
    private readonly KnowledgeEmbeddingWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        do
        {
            if (!embeddings.IsConfigured)
                continue;
            var job = await jobs.ClaimAsync(_options.LeaseTimeout, stoppingToken).ConfigureAwait(false);
            if (job is null)
                continue;
            try
            {
                await ProcessJobAsync(job, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                var retryable = IsRetryable(exception, stoppingToken);
                var disposition = await jobs.FailAsync(
                    job, exception.Message, retryable, _options.MaxAttempts,
                    RetryDelay(job.AttemptCount), stoppingToken).ConfigureAwait(false);
                logger.LogWarning(exception, "知识嵌入任务 {SourceId} 失败，处理结果 {Disposition}。", job.SourceId, disposition);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    internal static bool IsRetryable(Exception exception, CancellationToken stoppingToken)
        => exception is HttpRequestException or TimeoutException ||
           exception is OperationCanceledException && !stoppingToken.IsCancellationRequested;

    internal async Task ProcessJobAsync(KnowledgeEmbeddingJob job, CancellationToken ct)
    {
        if (!string.Equals(job.Model, embeddings.Model, StringComparison.Ordinal))
            throw new InvalidOperationException("知识嵌入模型配置已变更，请重新入队。 ");
        var source = await assets.GetKnowledgeSourceAsync(job.SourceId, ct).ConfigureAwait(false);
        if (source is null)
            throw new InvalidOperationException("知识来源不存在。 ");
        if (source.Status != KnowledgeSourceStatuses.Reviewed)
        {
            await jobs.CompleteAsync(job, ct).ConfigureAwait(false);
            return;
        }

        string? cursor = null;
        do
        {
            var page = await assets.ListKnowledgeRecordsForEmbeddingPageAsync(
                source.SourceId, _options.RecordPageSize, cursor, ct).ConfigureAwait(false);
            foreach (var record in page.Data.Where(static item => item.HumanReviewed))
            {
                if (!await jobs.RenewLeaseAsync(job, ct).ConfigureAwait(false))
                    throw new OperationCanceledException("知识嵌入任务租约已失效。");
                var embedding = await embeddings.EmbedAsync(
                    KnowledgeContentFingerprint.Normalize(record.Content), ct).ConfigureAwait(false);
                if (!await jobs.UpsertAsync(job, record, embedding, ct).ConfigureAwait(false))
                    throw new OperationCanceledException("知识嵌入任务租约已失效。");
            }
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        if (!await jobs.CompleteAsync(job, ct).ConfigureAwait(false))
            logger.LogWarning("知识嵌入任务 {SourceId} 完成时租约已失效。", job.SourceId);
    }

    private TimeSpan RetryDelay(int attemptCount)
    {
        var multiplier = Math.Pow(2, Math.Clamp(attemptCount - 1, 0, 20));
        var ticks = Math.Min(_options.MaxRetryDelay.Ticks, _options.InitialRetryDelay.Ticks * multiplier);
        return TimeSpan.FromTicks((long)ticks);
    }
}

/// <summary>周期巡检已复核知识，补偿发布入队失败并修复历史嵌入缺口。</summary>
public sealed class KnowledgeEmbeddingBackfillService(
    IKnowledgeEmbeddingJobStore jobs,
    IKnowledgeEmbeddingClient embeddings,
    IOptions<KnowledgeEmbeddingWorkerOptions> options,
    ILogger<KnowledgeEmbeddingBackfillService> logger) : IHostedService
{
    private readonly KnowledgeEmbeddingWorkerOptions _options = options.Value;
    private CancellationTokenSource? _stopping;
    private Task? _execution;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _execution = ExecuteAsync(_stopping.Token);
        return _execution.IsCompleted ? _execution : Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null || _execution is null)
            return;
        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _execution.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        finally
        {
            _stopping.Dispose();
            _stopping = null;
            _execution = null;
        }
    }

    internal async Task ReconcileOnceAsync(CancellationToken ct)
    {
        if (!embeddings.IsConfigured)
            return;
        var queued = await jobs.EnqueueMissingAsync(ct).ConfigureAwait(false);
        if (queued > 0)
            logger.LogInformation("已将 {Count} 个缺少当前嵌入的已复核知识来源加入回填队列。", queued);
    }

    private async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.ReconciliationInterval);
        try
        {
            do
            {
                try
                {
                    await ReconcileOnceAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "知识嵌入周期巡检失败，将在下一周期重试。");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
