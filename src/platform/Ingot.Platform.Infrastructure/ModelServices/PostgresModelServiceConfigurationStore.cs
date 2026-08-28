// 使用 PostgreSQL 与 ASP.NET Data Protection 持久化模型配置和加密 API key。
using System.Security.Cryptography;
using Ingot.Agent;
using Ingot.Platform.Application.ModelServices;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Ingot.Platform.Infrastructure.ModelServices;

public sealed class PostgresModelServiceConfigurationStore :
    IModelServiceConfigurationStore,
    IModelServiceConfigurationProvider
{
    private const string EntryPoint = "chat";
    private readonly NpgsqlDataSource _dataSource;
    private readonly IDataProtector _protector;
    private readonly object _gate = new();
    private ModelServiceConnectionSettings _current;
    private ModelServiceConfigurationView _view;

    public PostgresModelServiceConfigurationStore(
        NpgsqlDataSource dataSource,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<ChatOptions> options)
    {
        _dataSource = dataSource;
        _protector = dataProtectionProvider.CreateProtector(
            "Ingot.Platform.ModelServiceConfiguration.ApiKey.v1");
        var deployment = options.Value;
        _current = new ModelServiceConnectionSettings
        {
            Enabled = deployment.Enabled,
            Provider = deployment.Provider,
            Protocol = deployment.Protocol,
            BaseUrl = deployment.BaseUrl,
            FastModel = deployment.FastModel,
            ReasoningModel = deployment.ReasoningModel,
            ApiKey = null,
            Revision = "deployment"
        };
        _view = ToView(_current, null, null, null, "deployment");
    }

    public ModelServiceConnectionSettings Current
    {
        get
        {
            lock (_gate) return _current;
        }
    }

    public ModelServiceConfigurationView GetCurrent()
    {
        lock (_gate) return _view;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var command = _dataSource.CreateCommand(
            """
            SELECT enabled, provider, protocol, base_url, fast_model, reasoning_model,
                   protected_api_key, api_key_hint, updated_at, updated_by
            FROM model_service_configurations
            WHERE entry_point = @entry_point;
            """);
        command.Parameters.AddWithValue("entry_point", EntryPoint);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
            return;

        var protectedApiKey = reader.IsDBNull(6) ? null : reader.GetString(6);
        string? apiKey = null;
        if (!string.IsNullOrWhiteSpace(protectedApiKey))
        {
            try
            {
                apiKey = _protector.Unprotect(protectedApiKey);
            }
            catch (CryptographicException exception)
            {
                throw new InvalidOperationException(
                    "模型服务 API key 无法使用当前 Platform 数据保护密钥解密。",
                    exception);
            }
        }

        var updatedAt = reader.GetFieldValue<DateTimeOffset>(8);
        var settings = new ModelServiceConnectionSettings
        {
            Enabled = reader.GetBoolean(0),
            Provider = reader.GetString(1),
            Protocol = reader.GetString(2),
            BaseUrl = reader.IsDBNull(3) ? null : reader.GetString(3),
            FastModel = reader.GetString(4),
            ReasoningModel = reader.GetString(5),
            ApiKey = apiKey,
            Revision = updatedAt.ToUnixTimeMilliseconds().ToString()
        };
        var view = new ModelServiceConfigurationView
        {
            Enabled = settings.Enabled,
            Provider = settings.Provider,
            Protocol = settings.Protocol,
            BaseUrl = settings.BaseUrl,
            FastModel = settings.FastModel,
            ReasoningModel = settings.ReasoningModel,
            HasApiKey = !string.IsNullOrWhiteSpace(apiKey),
            ApiKeyHint = reader.IsDBNull(7) ? null : reader.GetString(7),
            UpdatedAt = updatedAt,
            UpdatedBy = reader.GetString(9),
            Source = "platform"
        };
        lock (_gate)
        {
            _current = settings;
            _view = view;
        }
    }

    public Task RefreshAsync(CancellationToken ct = default) => InitializeAsync(ct);

    public async Task<ModelServiceConfigurationView> SaveAsync(
        SaveModelServiceConfigurationCommand command,
        string actorUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var previous = Current;
        var apiKey = command.ClearApiKey
            ? null
            : string.IsNullOrWhiteSpace(command.ApiKey) ? previous.ApiKey : command.ApiKey.Trim();
        if (command.Enabled && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("启用模型服务前必须配置 API key。");

        var now = DateTimeOffset.UtcNow;
        var provider = command.Provider.Trim();
        var protocol = NormalizeProtocol(command.Protocol);
        var baseUrl = string.IsNullOrWhiteSpace(command.BaseUrl) ? null : command.BaseUrl.Trim().TrimEnd('/');
        var fastModel = command.FastModel.Trim();
        var reasoningModel = command.ReasoningModel.Trim();
        var protectedApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : _protector.Protect(apiKey);
        var apiKeyHint = ApiKeyHint(apiKey);

        await using var db = _dataSource.CreateCommand(
            """
            INSERT INTO model_service_configurations (
              entry_point, enabled, provider, protocol, base_url, fast_model, reasoning_model,
              protected_api_key, api_key_hint, updated_at, updated_by)
            VALUES (
              @entry_point, @enabled, @provider, @protocol, @base_url, @fast_model, @reasoning_model,
              @protected_api_key, @api_key_hint, @updated_at, @updated_by)
            ON CONFLICT (entry_point) DO UPDATE SET
              enabled = EXCLUDED.enabled,
              provider = EXCLUDED.provider,
              protocol = EXCLUDED.protocol,
              base_url = EXCLUDED.base_url,
              fast_model = EXCLUDED.fast_model,
              reasoning_model = EXCLUDED.reasoning_model,
              protected_api_key = EXCLUDED.protected_api_key,
              api_key_hint = EXCLUDED.api_key_hint,
              updated_at = EXCLUDED.updated_at,
              updated_by = EXCLUDED.updated_by;
            """);
        db.Parameters.AddWithValue("entry_point", EntryPoint);
        db.Parameters.AddWithValue("enabled", command.Enabled);
        db.Parameters.AddWithValue("provider", provider);
        db.Parameters.AddWithValue("protocol", protocol);
        db.Parameters.AddWithValue("base_url", (object?)baseUrl ?? DBNull.Value);
        db.Parameters.AddWithValue("fast_model", fastModel);
        db.Parameters.AddWithValue("reasoning_model", reasoningModel);
        db.Parameters.AddWithValue("protected_api_key", (object?)protectedApiKey ?? DBNull.Value);
        db.Parameters.AddWithValue("api_key_hint", (object?)apiKeyHint ?? DBNull.Value);
        db.Parameters.AddWithValue("updated_at", now);
        db.Parameters.AddWithValue("updated_by", actorUserId);
        await db.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        var settings = new ModelServiceConnectionSettings
        {
            Enabled = command.Enabled,
            Provider = provider,
            Protocol = protocol,
            BaseUrl = baseUrl,
            FastModel = fastModel,
            ReasoningModel = reasoningModel,
            ApiKey = apiKey,
            Revision = now.ToUnixTimeMilliseconds().ToString()
        };
        var view = ToView(settings, apiKey, now, actorUserId, "platform");
        lock (_gate)
        {
            _current = settings;
            _view = view;
        }
        return view;
    }

    private static string NormalizeProtocol(string protocol)
        => string.Equals(protocol, "ChatCompletions", StringComparison.OrdinalIgnoreCase)
            ? "ChatCompletions"
            : "Responses";

    private static string? ApiKeyHint(string? apiKey)
        => string.IsNullOrWhiteSpace(apiKey)
            ? null
            : $"••••{apiKey[^Math.Min(4, apiKey.Length)..]}";

    private static ModelServiceConfigurationView ToView(
        ModelServiceConnectionSettings settings,
        string? apiKey,
        DateTimeOffset? updatedAt,
        string? updatedBy,
        string source)
        => new()
        {
            Enabled = settings.Enabled,
            Provider = settings.Provider,
            Protocol = settings.Protocol,
            BaseUrl = settings.BaseUrl,
            FastModel = settings.FastModel,
            ReasoningModel = settings.ReasoningModel,
            HasApiKey = !string.IsNullOrWhiteSpace(apiKey),
            ApiKeyHint = ApiKeyHint(apiKey),
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy,
            Source = source
        };
}

public sealed class ModelServiceConfigurationInitializerHostedService(
    PostgresModelServiceConfigurationStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => store.InitializeAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
