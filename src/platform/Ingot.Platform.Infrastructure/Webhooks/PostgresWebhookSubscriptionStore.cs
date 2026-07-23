using System.Globalization;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.Webhooks;

public sealed class PostgresWebhookSubscriptionStore : IWebhookSubscriptionStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresWebhookSubscriptionStore(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized)
            return;

        await _initializeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized)
                return;

            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS webhook_subscriptions (
                  subscription_id      UUID PRIMARY KEY,
                  name                 TEXT NOT NULL,
                  endpoint             TEXT NOT NULL,
                  event_types          JSONB NOT NULL DEFAULT '[]'::jsonb,
                  subject_type         TEXT,
                  subject_id           TEXT,
                  context_filter       JSONB NOT NULL DEFAULT '{}'::jsonb,
                  secret               TEXT,
                  cursor               BIGINT NOT NULL DEFAULT 0,
                  enabled              BOOLEAN NOT NULL DEFAULT TRUE,
                  created_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
                  updated_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
                  last_success_at      TIMESTAMPTZ,
                  last_error           TEXT,
                  consecutive_failures INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS idx_webhook_subscriptions_enabled
                  ON webhook_subscriptions(enabled, created_at);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<WebhookSubscription> CreateAsync(
        CreateWebhookSubscriptionRequest request,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var subscriptionId = Guid.NewGuid();
        var cursor = request.StartAfterIngestId ?? await GetLatestIngestIdAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO webhook_subscriptions(
              subscription_id, name, endpoint, event_types, subject_type, subject_id,
              context_filter, secret, cursor)
            VALUES (
              @subscription_id, @name, @endpoint, @event_types, @subject_type, @subject_id,
              @context_filter, @secret, @cursor);
            """);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("endpoint", request.Endpoint.Trim());
        command.Parameters.AddWithValue(
            "event_types",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(
                request.EventTypes
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                JsonOptions));
        command.Parameters.AddWithValue(
            "subject_type",
            (object?)Normalize(request.SubjectType) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "subject_id",
            (object?)Normalize(request.SubjectId) ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "context_filter",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(request.Context, JsonOptions));
        command.Parameters.AddWithValue("secret", (object?)request.Secret ?? DBNull.Value);
        command.Parameters.AddWithValue("cursor", Math.Max(0, cursor));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return (await GetAsync(subscriptionId, ct).ConfigureAwait(false))!;
    }

    public async Task<IReadOnlyList<WebhookSubscription>> ListAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"{SelectColumns} ORDER BY created_at ASC;");
        var subscriptions = new List<WebhookSubscription>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            subscriptions.Add(Read(reader));
        return subscriptions;
    }

    public async Task<WebhookSubscription?> UpdateAsync(
        Guid subscriptionId,
        UpdateWebhookSubscriptionRequest request,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        var replaceSecret = request.ClearSecret || !string.IsNullOrWhiteSpace(request.Secret);
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE webhook_subscriptions
            SET name = @name,
                endpoint = @endpoint,
                event_types = @event_types,
                subject_type = @subject_type,
                subject_id = @subject_id,
                context_filter = @context_filter,
                secret = CASE WHEN @replace_secret THEN @secret ELSE secret END,
                updated_at = now()
            WHERE subscription_id = @subscription_id;
            """);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("name", request.Name.Trim());
        command.Parameters.AddWithValue("endpoint", request.Endpoint.Trim());
        command.Parameters.AddWithValue(
            "event_types",
            NpgsqlDbType.Jsonb,
            JsonSerializer.Serialize(
                request.EventTypes
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                JsonOptions));
        command.Parameters.AddWithValue("subject_type", (object?)Normalize(request.SubjectType) ?? DBNull.Value);
        command.Parameters.AddWithValue("subject_id", (object?)Normalize(request.SubjectId) ?? DBNull.Value);
        command.Parameters.AddWithValue("context_filter", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(request.Context, JsonOptions));
        command.Parameters.AddWithValue("replace_secret", replaceSecret);
        command.Parameters.AddWithValue("secret", request.ClearSecret ? DBNull.Value : (object?)request.Secret ?? DBNull.Value);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            return null;
        return await GetAsync(subscriptionId, ct).ConfigureAwait(false);
    }

    public async Task<WebhookSubscription?> GetAsync(
        Guid subscriptionId,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            $"{SelectColumns} WHERE subscription_id = @subscription_id;");
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
    }

    public async Task<bool> DeleteAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            "DELETE FROM webhook_subscriptions WHERE subscription_id = @subscription_id;");
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async Task<bool> SetEnabledAsync(
        Guid subscriptionId,
        bool enabled,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE webhook_subscriptions
            SET enabled = @enabled, updated_at = now()
            WHERE subscription_id = @subscription_id;
            """);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("enabled", enabled);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    public async Task AdvanceAsync(Guid subscriptionId, long ingestId, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE webhook_subscriptions
            SET cursor = GREATEST(cursor, @cursor),
                updated_at = now(),
                last_success_at = now(),
                last_error = NULL,
                consecutive_failures = 0
            WHERE subscription_id = @subscription_id;
            """);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("cursor", ingestId);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task RecordFailureAsync(
        Guid subscriptionId,
        string error,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE webhook_subscriptions
            SET updated_at = now(),
                last_error = @error,
                consecutive_failures = consecutive_failures + 1
            WHERE subscription_id = @subscription_id;
            """);
        command.Parameters.AddWithValue("subscription_id", subscriptionId);
        command.Parameters.AddWithValue("error", error.Length > 2_000 ? error[..2_000] : error);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<long> GetLatestIngestIdAsync(CancellationToken ct)
    {
        await using var command = _dataSource.CreateCommand(
            "SELECT COALESCE(MAX(ingest_id), 0) FROM production_events;");
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(ct).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static WebhookSubscription Read(NpgsqlDataReader reader)
    {
        var eventTypes = JsonSerializer.Deserialize<string[]>(reader.GetString(3), JsonOptions) ?? [];
        var context = JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(6), JsonOptions)
                      ?? new Dictionary<string, string>();
        return new WebhookSubscription
        {
            SubscriptionId = reader.GetGuid(0),
            Name = reader.GetString(1),
            Endpoint = new Uri(reader.GetString(2), UriKind.Absolute),
            EventTypes = eventTypes,
            SubjectType = reader.IsDBNull(4) ? null : reader.GetString(4),
            SubjectId = reader.IsDBNull(5) ? null : reader.GetString(5),
            Context = context,
            Secret = reader.IsDBNull(7) ? null : reader.GetString(7),
            Cursor = reader.GetInt64(8),
            Enabled = reader.GetBoolean(9),
            CreatedAt = new DateTimeOffset(reader.GetDateTime(10).ToUniversalTime()),
            LastSuccessAt = reader.IsDBNull(11)
                ? null
                : new DateTimeOffset(reader.GetDateTime(11).ToUniversalTime()),
            LastError = reader.IsDBNull(12) ? null : reader.GetString(12),
            ConsecutiveFailures = reader.GetInt32(13)
        };
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string SelectColumns = """
        SELECT subscription_id, name, endpoint, event_types::text, subject_type, subject_id,
               context_filter::text, secret, cursor, enabled, created_at, last_success_at,
               last_error, consecutive_failures
        FROM webhook_subscriptions
        """;
}
