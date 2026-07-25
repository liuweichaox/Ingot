using System.Security.Cryptography;
using System.Text.Json;
using Ingot.Contracts.ProcessImprovement;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Ingot.Platform.Infrastructure.ProcessImprovement;

public sealed class PostgresProcessImprovementStore : IProcessImprovementStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NpgsqlDataSource _dataSource;
    private readonly ProcessKnowledgeOptions _options;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile bool _initialized;

    public PostgresProcessImprovementStore(
        IConfiguration configuration,
        IOptions<ProcessKnowledgeOptions> options)
    {
        var connectionString = configuration.GetConnectionString("Events")
            ?? throw new InvalidOperationException("缺少 ConnectionStrings:Events PostgreSQL 连接字符串。");
        _dataSource = NpgsqlDataSource.Create(connectionString);
        _options = options.Value;
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
            Directory.CreateDirectory(GetRootPath());
            if (GetArchiveRootPath() is { } archiveRoot)
                Directory.CreateDirectory(archiveRoot);
            await using var command = _dataSource.CreateCommand(
                """
                CREATE TABLE IF NOT EXISTS training_dataset_versions (
                  dataset_id TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  payload JSONB NOT NULL,
                  created_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (dataset_id, version),
                  CHECK (version > 0)
                );

                CREATE TABLE IF NOT EXISTS process_model_versions (
                  model_id TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  status TEXT NOT NULL,
                  dataset_id TEXT NOT NULL,
                  dataset_version INTEGER NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (model_id, version),
                  FOREIGN KEY (dataset_id, dataset_version)
                    REFERENCES training_dataset_versions(dataset_id, version),
                  CHECK (version > 0),
                  CHECK (status IN ('draft', 'validated', 'active', 'suspended', 'retired'))
                );
                CREATE UNIQUE INDEX IF NOT EXISTS uq_process_model_active
                  ON process_model_versions(model_id) WHERE status = 'active';

                CREATE TABLE IF NOT EXISTS model_evaluations (
                  evaluation_id UUID PRIMARY KEY,
                  model_id TEXT NOT NULL,
                  model_version INTEGER NOT NULL,
                  passed BOOLEAN NOT NULL,
                  payload JSONB NOT NULL,
                  created_at TIMESTAMPTZ NOT NULL,
                  FOREIGN KEY (model_id, model_version)
                    REFERENCES process_model_versions(model_id, version)
                );

                CREATE TABLE IF NOT EXISTS model_drift_readings (
                  reading_id UUID PRIMARY KEY,
                  model_id TEXT NOT NULL,
                  model_version INTEGER NOT NULL,
                  value DOUBLE PRECISION NOT NULL,
                  stop_threshold DOUBLE PRECISION NOT NULL,
                  payload JSONB NOT NULL,
                  created_at TIMESTAMPTZ NOT NULL,
                  FOREIGN KEY (model_id, model_version)
                    REFERENCES process_model_versions(model_id, version)
                );
                CREATE INDEX IF NOT EXISTS idx_model_drift_readings_model
                  ON model_drift_readings(model_id, model_version, created_at DESC);

                CREATE TABLE IF NOT EXISTS mechanism_model_versions (
                  model_id TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  status TEXT NOT NULL,
                  content_hash TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (model_id, version),
                  CHECK (version > 0),
                  CHECK (status IN ('draft', 'validated', 'active', 'retired'))
                );
                CREATE UNIQUE INDEX IF NOT EXISTS uq_mechanism_model_active
                  ON mechanism_model_versions(model_id) WHERE status = 'active';

                CREATE TABLE IF NOT EXISTS mechanism_fusion_definitions (
                  fusion_id TEXT NOT NULL,
                  version INTEGER NOT NULL,
                  status TEXT NOT NULL,
                  mode TEXT NOT NULL,
                  mechanism_model_id TEXT NOT NULL,
                  mechanism_model_version INTEGER NOT NULL,
                  content_hash TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  PRIMARY KEY (fusion_id, version),
                  FOREIGN KEY (mechanism_model_id, mechanism_model_version)
                    REFERENCES mechanism_model_versions(model_id, version),
                  CHECK (version > 0),
                  CHECK (status IN ('draft', 'validated', 'active', 'retired')),
                  CHECK (mode IN ('calibration', 'post-processing', 'mechanism-as-feature', 'ensemble'))
                );
                CREATE UNIQUE INDEX IF NOT EXISTS uq_mechanism_fusion_active
                  ON mechanism_fusion_definitions(fusion_id) WHERE status = 'active';

                CREATE TABLE IF NOT EXISTS scientific_validation_reports (
                  report_id UUID PRIMARY KEY,
                  dataset_id TEXT NOT NULL,
                  dataset_version INTEGER NOT NULL,
                  industry TEXT NOT NULL,
                  status TEXT NOT NULL,
                  source_sha256 TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  created_at TIMESTAMPTZ NOT NULL,
                  CHECK (dataset_version > 0),
                  CHECK (status IN ('passed', 'rejected'))
                );
                CREATE INDEX IF NOT EXISTS idx_scientific_validation_dataset
                  ON scientific_validation_reports(dataset_id, dataset_version, created_at DESC);

                CREATE TABLE IF NOT EXISTS process_knowledge_sources (
                  source_id UUID PRIMARY KEY,
                  status TEXT NOT NULL,
                  storage_ref TEXT NOT NULL,
                  sha256 TEXT NOT NULL UNIQUE,
                  file_name TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL,
                  CHECK (status IN ('uploaded', 'indexed', 'reviewed', 'retired'))
                );

                CREATE TABLE IF NOT EXISTS process_knowledge_records (
                  record_id UUID PRIMARY KEY,
                  source_id UUID NOT NULL REFERENCES process_knowledge_sources(source_id),
                  human_reviewed BOOLEAN NOT NULL,
                  payload JSONB NOT NULL,
                  updated_at TIMESTAMPTZ NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_process_knowledge_records_source
                  ON process_knowledge_records(source_id, updated_at DESC);

                CREATE TABLE IF NOT EXISTS process_improvement_audit (
                  entry_id UUID PRIMARY KEY,
                  resource_type TEXT NOT NULL,
                  resource_id TEXT NOT NULL,
                  action TEXT NOT NULL,
                  payload JSONB NOT NULL,
                  created_at TIMESTAMPTZ NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_process_improvement_audit_resource
                  ON process_improvement_audit(resource_type, resource_id, created_at);
                """);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<TrainingDatasetVersion> AddDatasetAsync(
        TrainingDatasetVersion value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO training_dataset_versions(dataset_id, version, payload, created_at)
            VALUES (@dataset_id, @version, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("dataset_id", value.DatasetId);
        command.Parameters.AddWithValue("version", value.Version);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<TrainingDatasetVersion?> GetDatasetAsync(
        string datasetId,
        int version,
        CancellationToken ct = default)
        => GetSingleAsync<TrainingDatasetVersion>(
            "SELECT payload::text FROM training_dataset_versions WHERE dataset_id = @id AND version = @version;",
            command =>
            {
                command.Parameters.AddWithValue("id", datasetId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<IReadOnlyList<TrainingDatasetVersion>> ListDatasetsAsync(CancellationToken ct = default)
        => ListAsync<TrainingDatasetVersion>(
            "SELECT payload::text FROM training_dataset_versions ORDER BY created_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<ProcessModelVersion> SaveModelAsync(
        ProcessModelVersion value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_model_versions(
              model_id, version, status, dataset_id, dataset_version, payload, updated_at)
            VALUES (@model_id, @version, @status, @dataset_id, @dataset_version, @payload, @updated_at)
            ON CONFLICT (model_id, version) DO UPDATE SET
              status = EXCLUDED.status,
              dataset_id = EXCLUDED.dataset_id,
              dataset_version = EXCLUDED.dataset_version,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("model_id", value.ModelId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("dataset_id", value.DatasetId);
        command.Parameters.AddWithValue("dataset_version", value.DatasetVersion);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<ProcessModelVersion?> GetModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => GetSingleAsync<ProcessModelVersion>(
            "SELECT payload::text FROM process_model_versions WHERE model_id = @id AND version = @version;",
            command =>
            {
                command.Parameters.AddWithValue("id", modelId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<IReadOnlyList<ProcessModelVersion>> ListModelsAsync(CancellationToken ct = default)
        => ListAsync<ProcessModelVersion>(
            "SELECT payload::text FROM process_model_versions ORDER BY updated_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<ModelEvaluation> AddEvaluationAsync(
        ModelEvaluation value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO model_evaluations(
              evaluation_id, model_id, model_version, passed, payload, created_at)
            VALUES (@id, @model_id, @model_version, @passed, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.EvaluationId);
        command.Parameters.AddWithValue("model_id", value.ModelId);
        command.Parameters.AddWithValue("model_version", value.ModelVersion);
        command.Parameters.AddWithValue("passed", value.Passed);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.EvaluatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ModelEvaluation>> ListEvaluationsAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => ListAsync<ModelEvaluation>(
            """
            SELECT payload::text FROM model_evaluations
            WHERE model_id = @id AND model_version = @version
            ORDER BY created_at DESC
            LIMIT 200;
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", modelId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public async Task<ModelDriftReading> AddDriftReadingAsync(
        ModelDriftReading value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO model_drift_readings(
              reading_id, model_id, model_version, value, stop_threshold, payload, created_at)
            VALUES (@id, @model_id, @model_version, @value, @stop_threshold, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.ReadingId);
        command.Parameters.AddWithValue("model_id", value.ModelId);
        command.Parameters.AddWithValue("model_version", value.ModelVersion);
        command.Parameters.AddWithValue("value", value.Value);
        command.Parameters.AddWithValue("stop_threshold", value.StopThreshold);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.RecordedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ModelDriftReading>> ListDriftReadingsAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => ListAsync<ModelDriftReading>(
            """
            SELECT payload::text FROM model_drift_readings
            WHERE model_id = @id AND model_version = @version
            ORDER BY created_at DESC
            LIMIT 200;
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", modelId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public async Task<MechanismModelVersion> SaveMechanismModelAsync(
        MechanismModelVersion value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO mechanism_model_versions(
              model_id, version, status, content_hash, payload, updated_at)
            VALUES (@id, @version, @status, @content_hash, @payload, @updated_at)
            ON CONFLICT (model_id, version) DO UPDATE SET
              status = EXCLUDED.status,
              content_hash = EXCLUDED.content_hash,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.ModelId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("content_hash", value.ContentHash);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<MechanismModelVersion?> GetMechanismModelAsync(
        string modelId,
        int version,
        CancellationToken ct = default)
        => GetSingleAsync<MechanismModelVersion>(
            """
            SELECT payload::text FROM mechanism_model_versions
            WHERE model_id = @id AND version = @version;
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", modelId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<IReadOnlyList<MechanismModelVersion>> ListMechanismModelsAsync(
        CancellationToken ct = default)
        => ListAsync<MechanismModelVersion>(
            "SELECT payload::text FROM mechanism_model_versions ORDER BY updated_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<MechanismFusionDefinition> SaveMechanismFusionAsync(
        MechanismFusionDefinition value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO mechanism_fusion_definitions(
              fusion_id, version, status, mode, mechanism_model_id,
              mechanism_model_version, content_hash, payload, updated_at)
            VALUES (
              @id, @version, @status, @mode, @model_id,
              @model_version, @content_hash, @payload, @updated_at)
            ON CONFLICT (fusion_id, version) DO UPDATE SET
              status = EXCLUDED.status,
              mode = EXCLUDED.mode,
              mechanism_model_id = EXCLUDED.mechanism_model_id,
              mechanism_model_version = EXCLUDED.mechanism_model_version,
              content_hash = EXCLUDED.content_hash,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.FusionId);
        command.Parameters.AddWithValue("version", value.Version);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("mode", value.Mode);
        command.Parameters.AddWithValue("model_id", value.MechanismModelId);
        command.Parameters.AddWithValue("model_version", value.MechanismModelVersion);
        command.Parameters.AddWithValue("content_hash", value.ContentHash);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<MechanismFusionDefinition?> GetMechanismFusionAsync(
        string fusionId,
        int version,
        CancellationToken ct = default)
        => GetSingleAsync<MechanismFusionDefinition>(
            """
            SELECT payload::text FROM mechanism_fusion_definitions
            WHERE fusion_id = @id AND version = @version;
            """,
            command =>
            {
                command.Parameters.AddWithValue("id", fusionId);
                command.Parameters.AddWithValue("version", version);
            },
            ct);

    public Task<IReadOnlyList<MechanismFusionDefinition>> ListMechanismFusionsAsync(
        CancellationToken ct = default)
        => ListAsync<MechanismFusionDefinition>(
            "SELECT payload::text FROM mechanism_fusion_definitions ORDER BY updated_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<ScientificValidationReport> SaveScientificValidationReportAsync(
        ScientificValidationReport value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO scientific_validation_reports(
              report_id, dataset_id, dataset_version, industry, status,
              source_sha256, payload, created_at)
            VALUES (
              @id, @dataset_id, @dataset_version, @industry, @status,
              @source_sha256, @payload, @created_at)
            ON CONFLICT (report_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload;
            """);
        command.Parameters.AddWithValue("id", value.ReportId);
        command.Parameters.AddWithValue("dataset_id", value.DatasetId);
        command.Parameters.AddWithValue("dataset_version", value.DatasetVersion);
        command.Parameters.AddWithValue("industry", value.Industry);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("source_sha256", value.SourceSha256);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<ScientificValidationReport>> ListScientificValidationReportsAsync(
        CancellationToken ct = default)
        => ListAsync<ScientificValidationReport>(
            "SELECT payload::text FROM scientific_validation_reports ORDER BY created_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<InvestigationCase> SaveInvestigationAsync(
        InvestigationCase value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_investigations(
              investigation_id, status, problem_code, payload, updated_at)
            VALUES (@id, @status, @problem_code, @payload, @updated_at)
            ON CONFLICT (investigation_id) DO UPDATE SET
              status = EXCLUDED.status,
              problem_code = EXCLUDED.problem_code,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.InvestigationId);
        command.Parameters.AddWithValue("status", value.Status);
        command.Parameters.AddWithValue("problem_code", value.ProblemCode);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<InvestigationCase?> GetInvestigationAsync(
        Guid investigationId,
        CancellationToken ct = default)
        => GetByGuidAsync<InvestigationCase>(
            "SELECT payload::text FROM process_investigations WHERE investigation_id = @id;",
            investigationId,
            ct);

    public Task<IReadOnlyList<InvestigationCase>> ListInvestigationsAsync(CancellationToken ct = default)
        => ListAsync<InvestigationCase>(
            "SELECT payload::text FROM process_investigations ORDER BY updated_at DESC;",
            null,
            ct);

    public async Task<PossibleCause> SaveCauseAsync(PossibleCause value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_possible_causes(
              cause_id, investigation_id, status, payload, updated_at)
            VALUES (@id, @investigation_id, @status, @payload, @updated_at)
            ON CONFLICT (cause_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.CauseId);
        command.Parameters.AddWithValue("investigation_id", value.InvestigationId);
        command.Parameters.AddWithValue("status", value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<PossibleCause?> GetCauseAsync(Guid causeId, CancellationToken ct = default)
        => GetByGuidAsync<PossibleCause>(
            "SELECT payload::text FROM process_possible_causes WHERE cause_id = @id;",
            causeId,
            ct);

    public Task<IReadOnlyList<PossibleCause>> ListCausesAsync(
        Guid investigationId,
        CancellationToken ct = default)
        => ListByGuidAsync<PossibleCause>(
            """
            SELECT payload::text FROM process_possible_causes
            WHERE investigation_id = @id ORDER BY updated_at DESC;
            """,
            investigationId,
            ct);

    public async Task<ProcessTrial> SaveTrialAsync(ProcessTrial value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_trials(
              trial_id, investigation_id, cause_id, status, payload, updated_at)
            VALUES (@id, @investigation_id, @cause_id, @status, @payload, @updated_at)
            ON CONFLICT (trial_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.TrialId);
        command.Parameters.AddWithValue("investigation_id", value.InvestigationId);
        command.Parameters.AddWithValue("cause_id", value.CauseId);
        command.Parameters.AddWithValue("status", value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<ProcessTrial?> GetTrialAsync(Guid trialId, CancellationToken ct = default)
        => GetByGuidAsync<ProcessTrial>(
            "SELECT payload::text FROM process_trials WHERE trial_id = @id;",
            trialId,
            ct);

    public Task<IReadOnlyList<ProcessTrial>> ListTrialsAsync(
        Guid investigationId,
        CancellationToken ct = default)
        => ListByGuidAsync<ProcessTrial>(
            """
            SELECT payload::text FROM process_trials
            WHERE investigation_id = @id ORDER BY updated_at DESC;
            """,
            investigationId,
            ct);

    public async Task<TrialResult> AddTrialResultAsync(TrialResult value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_trial_results(result_id, trial_id, payload, created_at)
            VALUES (@id, @trial_id, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.ResultId);
        command.Parameters.AddWithValue("trial_id", value.TrialId);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.RecordedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<TrialResult>> ListTrialResultsAsync(
        Guid trialId,
        CancellationToken ct = default)
        => ListByGuidAsync<TrialResult>(
            """
            SELECT payload::text FROM process_trial_results
            WHERE trial_id = @id ORDER BY created_at;
            """,
            trialId,
            ct);

    public async Task<InvestigationConclusion> AddConclusionAsync(
        InvestigationConclusion value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_investigation_conclusions(
              conclusion_id, investigation_id, cause_id, trial_id, payload, created_at)
            VALUES (@id, @investigation_id, @cause_id, @trial_id, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.ConclusionId);
        command.Parameters.AddWithValue("investigation_id", value.InvestigationId);
        command.Parameters.AddWithValue("cause_id", value.CauseId);
        command.Parameters.AddWithValue("trial_id", value.TrialId);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.ReviewedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<InvestigationConclusion?> GetConclusionAsync(
        Guid conclusionId,
        CancellationToken ct = default)
        => GetByGuidAsync<InvestigationConclusion>(
            "SELECT payload::text FROM process_investigation_conclusions WHERE conclusion_id = @id;",
            conclusionId,
            ct);

    public Task<IReadOnlyList<InvestigationConclusion>> ListConclusionsAsync(
        Guid investigationId,
        CancellationToken ct = default)
        => ListByGuidAsync<InvestigationConclusion>(
            """
            SELECT payload::text FROM process_investigation_conclusions
            WHERE investigation_id = @id ORDER BY created_at DESC;
            """,
            investigationId,
            ct);

    public async Task<KnowledgeSource> AddKnowledgeSourceAsync(
        Stream content,
        string title,
        string sourceKind,
        string fileName,
        string mediaType,
        IReadOnlyDictionary<string, string> contextSelector,
        string userId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        await InitializeAsync(ct).ConfigureAwait(false);
        var safeFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName) || safeFileName.Length > 255)
            throw new ArgumentException("文件名不能为空且最长 255 个字符。", nameof(fileName));
        var tempPath = Path.Combine(GetRootPath(), $"{Guid.CreateVersion7():N}.uploading");
        long size = 0;
        string hash;
        using var sha = SHA256.Create();
        try
        {
            await using var temp = File.Create(tempPath);
            var buffer = new byte[81920];
            while (true)
            {
                var read = await content.ReadAsync(buffer, ct).ConfigureAwait(false);
                if (read == 0)
                    break;
                size += read;
                if (size > _options.MaxFileBytes)
                    throw new InvalidDataException($"知识来源文件超过 {_options.MaxFileBytes} 字节上限。");
                sha.TransformBlock(buffer, 0, read, null, 0);
                await temp.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }
            sha.TransformFinalBlock([], 0, 0);
            hash = Convert.ToHexStringLower(sha.Hash!);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
        if (size <= 0)
        {
            File.Delete(tempPath);
            throw new InvalidDataException("知识来源文件不能为空。");
        }

        var finalPath = GetKnowledgePath(hash, safeFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);
        if (!File.Exists(finalPath))
            File.Move(tempPath, finalPath);
        else
            File.Delete(tempPath);
        if (GetArchivePath(hash, safeFileName) is { } archivePath)
            await CopyToArchiveAsync(finalPath, archivePath, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var source = new KnowledgeSource
        {
            SourceId = Guid.CreateVersion7(),
            Title = title.Trim(),
            SourceKind = sourceKind.Trim().ToLowerInvariant(),
            Status = KnowledgeSourceStatuses.Uploaded,
            StorageRef = $"process-knowledge://sha256/{hash}/{Uri.EscapeDataString(safeFileName)}",
            Sha256 = hash,
            MediaType = string.IsNullOrWhiteSpace(mediaType)
                ? "application/octet-stream"
                : mediaType.Trim().ToLowerInvariant(),
            FileName = safeFileName,
            SizeBytes = size,
            ContextSelector = contextSelector,
            UploadedBy = userId,
            UploadedAt = now
        };

        await using var insert = _dataSource.CreateCommand(
            """
            INSERT INTO process_knowledge_sources(
              source_id, status, storage_ref, sha256, file_name, payload, updated_at)
            VALUES (@id, @status, @storage_ref, @sha256, @file_name, @payload, @updated_at)
            ON CONFLICT (sha256) DO NOTHING;
            """);
        insert.Parameters.AddWithValue("id", source.SourceId);
        insert.Parameters.AddWithValue("status", source.Status);
        insert.Parameters.AddWithValue("storage_ref", source.StorageRef);
        insert.Parameters.AddWithValue("sha256", source.Sha256);
        insert.Parameters.AddWithValue("file_name", source.FileName);
        AddJson(insert, source);
        insert.Parameters.AddWithValue("updated_at", now);
        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return await GetKnowledgeSourceByHashAsync(hash, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("知识来源写入后无法读取。");
    }

    public Task<KnowledgeSource?> GetKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default)
        => GetByGuidAsync<KnowledgeSource>(
            "SELECT payload::text FROM process_knowledge_sources WHERE source_id = @id;",
            sourceId,
            ct);

    public Task<IReadOnlyList<KnowledgeSource>> ListKnowledgeSourcesAsync(CancellationToken ct = default)
        => ListAsync<KnowledgeSource>(
            "SELECT payload::text FROM process_knowledge_sources ORDER BY updated_at DESC LIMIT 200;",
            null,
            ct);

    public async Task<Stream?> OpenKnowledgeSourceAsync(Guid sourceId, CancellationToken ct = default)
    {
        var source = await GetKnowledgeSourceAsync(sourceId, ct).ConfigureAwait(false);
        if (source is null)
            return null;
        var path = GetKnowledgePath(source.Sha256, source.FileName);
        if (!File.Exists(path) && GetArchivePath(source.Sha256, source.FileName) is { } archivePath)
            path = archivePath;
        if (!File.Exists(path))
            return null;
        return new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    public async Task<KnowledgeSource> SaveKnowledgeSourceMetadataAsync(
        KnowledgeSource value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            UPDATE process_knowledge_sources
            SET status = @status, payload = @payload, updated_at = @updated_at
            WHERE source_id = @id;
            """);
        command.Parameters.AddWithValue("id", value.SourceId);
        command.Parameters.AddWithValue("status", value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.ReviewedAt ?? DateTimeOffset.UtcNow);
        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            throw new KeyNotFoundException("知识来源不存在。");
        return value;
    }

    public async Task<KnowledgeRecord> SaveKnowledgeRecordAsync(
        KnowledgeRecord value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_knowledge_records(
              record_id, source_id, human_reviewed, payload, updated_at)
            VALUES (@id, @source_id, @human_reviewed, @payload, @updated_at)
            ON CONFLICT (record_id) DO UPDATE SET
              human_reviewed = EXCLUDED.human_reviewed,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.RecordId);
        command.Parameters.AddWithValue("source_id", value.SourceId);
        command.Parameters.AddWithValue("human_reviewed", value.HumanReviewed);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.ReviewedAt ?? value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<IReadOnlyList<KnowledgeRecord>> ListKnowledgeRecordsAsync(
        Guid sourceId,
        CancellationToken ct = default)
        => ListByGuidAsync<KnowledgeRecord>(
            """
            SELECT payload::text FROM process_knowledge_records
            WHERE source_id = @id ORDER BY updated_at DESC
            LIMIT 500;
            """,
            sourceId,
            ct);

    public async Task<ParameterRecommendation> SaveRecommendationAsync(
        ParameterRecommendation value,
        CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO parameter_recommendations(
              recommendation_id, investigation_id, conclusion_id, status, payload, updated_at)
            VALUES (@id, @investigation_id, @conclusion_id, @status, @payload, @updated_at)
            ON CONFLICT (recommendation_id) DO UPDATE SET
              status = EXCLUDED.status,
              payload = EXCLUDED.payload,
              updated_at = EXCLUDED.updated_at;
            """);
        command.Parameters.AddWithValue("id", value.RecommendationId);
        command.Parameters.AddWithValue("investigation_id", value.InvestigationId);
        command.Parameters.AddWithValue("conclusion_id", value.ConclusionId);
        command.Parameters.AddWithValue("status", value.Status);
        AddJson(command, value);
        command.Parameters.AddWithValue("updated_at", value.UpdatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return value;
    }

    public Task<ParameterRecommendation?> GetRecommendationAsync(
        Guid recommendationId,
        CancellationToken ct = default)
        => GetByGuidAsync<ParameterRecommendation>(
            "SELECT payload::text FROM parameter_recommendations WHERE recommendation_id = @id;",
            recommendationId,
            ct);

    public Task<IReadOnlyList<ParameterRecommendation>> ListRecommendationsAsync(CancellationToken ct = default)
        => ListAsync<ParameterRecommendation>(
            "SELECT payload::text FROM parameter_recommendations ORDER BY updated_at DESC;",
            null,
            ct);

    public async Task AddAuditEntryAsync(ImprovementAuditEntry value, CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(
            """
            INSERT INTO process_improvement_audit(
              entry_id, resource_type, resource_id, action, payload, created_at)
            VALUES (@id, @resource_type, @resource_id, @action, @payload, @created_at);
            """);
        command.Parameters.AddWithValue("id", value.EntryId);
        command.Parameters.AddWithValue("resource_type", value.ResourceType);
        command.Parameters.AddWithValue("resource_id", value.ResourceId);
        command.Parameters.AddWithValue("action", value.Action);
        AddJson(command, value);
        command.Parameters.AddWithValue("created_at", value.CreatedAt);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ImprovementAuditEntry>> ListAuditEntriesAsync(
        string resourceType,
        string resourceId,
        CancellationToken ct = default)
        => ListAsync<ImprovementAuditEntry>(
            """
            SELECT payload::text FROM process_improvement_audit
            WHERE resource_type = @resource_type AND resource_id = @resource_id
            ORDER BY created_at;
            """,
            command =>
            {
                command.Parameters.AddWithValue("resource_type", resourceType);
                command.Parameters.AddWithValue("resource_id", resourceId);
            },
            ct);

    public async ValueTask DisposeAsync()
    {
        _initializeLock.Dispose();
        await _dataSource.DisposeAsync().ConfigureAwait(false);
    }

    private async Task<T?> GetSingleAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configure,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(sql);
        configure?.Invoke(command);
        var payload = await command.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        return payload is null ? default : Deserialize<T>(payload);
    }

    private Task<T?> GetByGuidAsync<T>(string sql, Guid id, CancellationToken ct)
        => GetSingleAsync<T>(
            sql,
            command => command.Parameters.AddWithValue("id", id),
            ct);

    private Task<IReadOnlyList<T>> ListByGuidAsync<T>(string sql, Guid id, CancellationToken ct)
        => ListAsync<T>(
            sql,
            command => command.Parameters.AddWithValue("id", id),
            ct);

    private async Task<IReadOnlyList<T>> ListAsync<T>(
        string sql,
        Action<NpgsqlCommand>? configure,
        CancellationToken ct)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        await using var command = _dataSource.CreateCommand(sql);
        configure?.Invoke(command);
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var values = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
            values.Add(Deserialize<T>(reader.GetString(0)));
        return values;
    }

    private async Task<KnowledgeSource?> GetKnowledgeSourceByHashAsync(
        string sha256,
        CancellationToken ct)
        => await GetSingleAsync<KnowledgeSource>(
            "SELECT payload::text FROM process_knowledge_sources WHERE sha256 = @sha256;",
            command => command.Parameters.AddWithValue("sha256", sha256),
            ct).ConfigureAwait(false);

    private static T Deserialize<T>(string payload)
        => JsonSerializer.Deserialize<T>(payload, JsonOptions)
           ?? throw new InvalidDataException($"无法读取 {typeof(T).Name} 数据。");

    private static void AddJson<T>(NpgsqlCommand command, T value)
        => command.Parameters.Add(new NpgsqlParameter(
            "payload",
            NpgsqlDbType.Jsonb)
        {
            Value = JsonSerializer.Serialize(value, JsonOptions)
        });

    private string GetRootPath()
        => Path.GetFullPath(_options.RootPath, AppContext.BaseDirectory);

    private string? GetArchiveRootPath()
        => string.IsNullOrWhiteSpace(_options.ArchiveRootPath)
            ? null
            : Path.GetFullPath(_options.ArchiveRootPath, AppContext.BaseDirectory);

    private string GetKnowledgePath(string sha256, string fileName)
        => Path.Combine(GetRootPath(), sha256[..2], sha256, fileName);

    private string? GetArchivePath(string sha256, string fileName)
        => GetArchiveRootPath() is { } root
            ? Path.Combine(root, sha256[..2], sha256, fileName)
            : null;

    private static async Task CopyToArchiveAsync(
        string sourcePath,
        string archivePath,
        CancellationToken ct)
    {
        if (File.Exists(archivePath))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(archivePath)!);
        var tempPath = $"{archivePath}.{Guid.CreateVersion7():N}.archiving";
        try
        {
            await using (var source = new FileStream(
                             sourcePath,
                             FileMode.Open,
                             FileAccess.Read,
                             FileShare.Read,
                             81920,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var target = new FileStream(
                             tempPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             81920,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await source.CopyToAsync(target, ct).ConfigureAwait(false);
                await target.FlushAsync(ct).ConfigureAwait(false);
            }
            if (!File.Exists(archivePath))
                File.Move(tempPath, archivePath);
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }
}
