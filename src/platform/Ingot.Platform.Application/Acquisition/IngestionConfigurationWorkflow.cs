using Ingot.Contracts.Acquisition;
using Ingot.Contracts.ProcessConfiguration;
using Ingot.Platform.Application.ProcessConfiguration;

namespace Ingot.Platform.Application.Acquisition;

public enum AcquisitionWorkflowFailureKind
{
    Invalid,
    NotFound,
    Conflict,
    Timeout
}

public sealed record AcquisitionBatchFailure(
    string TaskId,
    IReadOnlyList<AcquisitionValidationError> Errors);

/// <summary>表示采集配置工作流拒绝了不满足业务规则的命令。</summary>
public sealed class AcquisitionWorkflowException : Exception
{
    public AcquisitionWorkflowException(
        AcquisitionWorkflowFailureKind kind,
        string message,
        IReadOnlyList<AcquisitionValidationError>? validation = null,
        IReadOnlyList<AcquisitionBatchFailure>? failures = null,
        Exception? innerException = null) : base(message, innerException)
    {
        Kind = kind;
        Validation = validation ?? [];
        Failures = failures ?? [];
    }

    public AcquisitionWorkflowFailureKind Kind { get; }
    public IReadOnlyList<AcquisitionValidationError> Validation { get; }
    public IReadOnlyList<AcquisitionBatchFailure> Failures { get; }
}

public sealed record PublishedIngestionTask(
    IngestionTask Task,
    AcquisitionProbeResult Validation);

/// <summary>
///     Owns the application rules for extracting, versioning, materializing and publishing
///     reusable ingestion configuration. HTTP controllers only translate transport concerns.
/// </summary>
public sealed class IngestionConfigurationWorkflow(
    IIngestionConfigurationStore store,
    IIngestionTaskStore taskStore,
    IProcessConfigurationStore processStore,
    AcquisitionProbeTaskCoordinator probeTasks)
{
    public async Task<ReusableIngestionConfiguration> ExtractReusableAsync(
        string taskId,
        int version,
        string templateId,
        int templateVersion,
        string dataSourceId,
        int dataSourceVersion,
        CancellationToken ct = default)
    {
        var task = await taskStore.GetAsync(NormalizeCode(taskId), version, ct).ConfigureAwait(false)
            ?? throw Failure(AcquisitionWorkflowFailureKind.NotFound, "指定的数据摄取任务不存在。");
        var model = await processStore.GetDataModelAsync(
            task.DataModelId, task.DataModelVersion, ct).ConfigureAwait(false);
        if (model is null || model.Status != ConfigurationStatuses.Published)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid, "任务引用的工艺数据模型必须已经发布。");
        if (!IngestionTaskDecomposer.TryCreate(
                task,
                model,
                templateId,
                templateVersion < 1 ? 1 : templateVersion,
                dataSourceId,
                dataSourceVersion < 1 ? 1 : dataSourceVersion,
                out var extracted,
                out var errors))
            throw ValidationFailure(errors);
        return await StoreAsync(
            () => store.SaveExtractedAsync(extracted!, ct)).ConfigureAwait(false);
    }

    public async Task<IngestionTaskTemplate> SaveTemplateAsync(
        IngestionTaskTemplate request,
        CancellationToken ct = default)
    {
        var model = await processStore.GetDataModelAsync(
            NormalizeCode(request.DataModelId), request.DataModelVersion, ct).ConfigureAwait(false);
        if (model is null)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid, "引用的标准数据模型版本不存在。");
        if (request.Status == ConfigurationStatuses.Published && model.Status != ConfigurationStatuses.Published)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid, "发布任务模板前，引用的标准数据模型必须已经发布。");
        if (!IngestionTaskValidator.TryValidateTemplate(request, model, out var normalized, out var errors))
            throw ValidationFailure(errors);
        await EnsureMutableAsync(
            await store.GetTemplateAsync(normalized!.TemplateId, normalized.Version, ct).ConfigureAwait(false),
            normalized.Status).ConfigureAwait(false);
        return await StoreAsync(() => normalized.Status == ConfigurationStatuses.Published
            ? store.PublishTemplateExclusiveAsync(normalized, ct)
            : store.UpsertTemplateAsync(normalized, ct)).ConfigureAwait(false);
    }

    public async Task DeleteTemplateAsync(string templateId, int version, CancellationToken ct = default)
    {
        var current = await store.GetTemplateAsync(NormalizeCode(templateId), version, ct).ConfigureAwait(false)
            ?? throw Failure(AcquisitionWorkflowFailureKind.NotFound, "任务模板不存在。");
        if (current.Status != ConfigurationStatuses.Draft)
            throw Failure(AcquisitionWorkflowFailureKind.Conflict, "只有草稿任务模板可以删除。");
        if (!await store.DeleteTemplateAsync(current.TemplateId, version, ct).ConfigureAwait(false))
            throw Failure(AcquisitionWorkflowFailureKind.NotFound, "任务模板不存在。");
    }

    public async Task<IReadOnlyList<DataSourceInstance>> ImportDataSourcesAsync(
        IReadOnlyList<DataSourceInstance> parsed,
        CancellationToken ct = default)
    {
        if (parsed.Count is 0 or > 500)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid, "一次必须导入 1-500 个数据源。");
        var duplicate = parsed.GroupBy(static item => (NormalizeCode(item.DataSourceId), item.Version))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid,
                $"数据源 {duplicate.Key.Item1} v{duplicate.Key.Version} 在文件中重复。");

        var normalized = new List<DataSourceInstance>();
        var failures = new List<AcquisitionBatchFailure>();
        foreach (var source in parsed)
        {
            if (!IngestionTaskValidator.TryValidateDataSource(source, out var valid, out var errors))
            {
                failures.Add(new AcquisitionBatchFailure(source.DataSourceId, errors));
                continue;
            }
            var existing = await store.GetDataSourceAsync(
                valid!.DataSourceId, valid.Version, ct).ConfigureAwait(false);
            if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
            {
                failures.Add(new AcquisitionBatchFailure(source.DataSourceId,
                    [new AcquisitionValidationError("status", "已发布或停用的数据源版本不可覆盖。")]));
                continue;
            }
            normalized.Add(valid);
        }
        if (failures.Count > 0)
            throw new AcquisitionWorkflowException(
                AcquisitionWorkflowFailureKind.Invalid,
                "CSV 存在无效数据，未写入任何数据源。",
                failures: failures);
        return await StoreAsync(() => store.SaveDataSourcesAsync(normalized, ct)).ConfigureAwait(false);
    }

    public async Task<DataSourceInstance> SaveDataSourceAsync(
        DataSourceInstance? request,
        CancellationToken ct = default)
    {
        if (!IngestionTaskValidator.TryValidateDataSource(request, out var normalized, out var errors))
            throw ValidationFailure(errors);
        await EnsureMutableAsync(
            await store.GetDataSourceAsync(normalized!.DataSourceId, normalized.Version, ct).ConfigureAwait(false),
            normalized.Status).ConfigureAwait(false);
        return await StoreAsync(() => normalized.Status == ConfigurationStatuses.Published
            ? store.PublishDataSourceExclusiveAsync(normalized, ct)
            : store.UpsertDataSourceAsync(normalized, ct)).ConfigureAwait(false);
    }

    public async Task DeleteDataSourceAsync(string dataSourceId, int version, CancellationToken ct = default)
    {
        var current = await store.GetDataSourceAsync(NormalizeCode(dataSourceId), version, ct).ConfigureAwait(false)
            ?? throw Failure(AcquisitionWorkflowFailureKind.NotFound, "数据源不存在。");
        if (current.Status != ConfigurationStatuses.Draft)
            throw Failure(AcquisitionWorkflowFailureKind.Conflict, "只有草稿数据源可以删除。");
        if (!await store.DeleteDataSourceAsync(current.DataSourceId, version, ct).ConfigureAwait(false))
            throw Failure(AcquisitionWorkflowFailureKind.NotFound, "数据源不存在。");
    }

    public async Task<PublishedIngestionTask> PublishBindingAsync(
        string taskId,
        int version,
        CancellationToken ct = default)
    {
        var existing = await store.GetBindingAsync(NormalizeCode(taskId), version, ct).ConfigureAwait(false)
            ?? throw Failure(AcquisitionWorkflowFailureKind.NotFound, "任务绑定不存在。");
        if (existing.Status != ConfigurationStatuses.Draft)
            throw Failure(AcquisitionWorkflowFailureKind.Conflict, "只有草稿任务绑定可以执行验证并发布。");
        var binding = existing with { Status = ConfigurationStatuses.Published, UpdatedAt = DateTimeOffset.UtcNow };
        var (template, source, model) = await ResolveDependenciesAsync(binding, ct).ConfigureAwait(false);
        if (model.Status != ConfigurationStatuses.Published)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid, "引用的工艺数据模型必须已经发布。");
        if (!IngestionTaskMaterializer.TryCreate(template, source, binding, model, out var task, out var errors))
            throw ValidationFailure(errors);

        AcquisitionProbeResult result;
        try
        {
            result = await probeTasks.QueueAndWaitAsync(
                new AcquisitionDeployment { Task = task!, DataModel = model },
                TimeSpan.FromMilliseconds(Math.Clamp(task!.Execution.TimeoutMs + 15_000, 15_000, 120_000)),
                new SourceDiscoveryQuery(),
                ct).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new AcquisitionWorkflowException(
                AcquisitionWorkflowFailureKind.Timeout,
                "现场节点设备验证超时。",
                innerException: exception);
        }
        if (!result.Success || !result.MappingsValidated)
            throw new AcquisitionWorkflowException(
                AcquisitionWorkflowFailureKind.Invalid,
                result.Message,
                validation: [new AcquisitionValidationError("probe", result.Message)]);
        var saved = await StoreAsync(
            () => store.SaveMaterializedTasksAsync([(binding, task!)], ct)).ConfigureAwait(false);
        if (saved.Count != 1)
            throw Failure(AcquisitionWorkflowFailureKind.Conflict, "任务发布事务没有返回唯一结果。");
        return new PublishedIngestionTask(saved[0], result);
    }

    public async Task<IReadOnlyList<IngestionTask>> MaterializeAsync(
        IReadOnlyList<IngestionTaskBinding> bindings,
        CancellationToken ct = default)
    {
        if (bindings.Count == 0)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid, "至少需要一个任务绑定。");
        if (bindings.Count > 500)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid, "单次最多实例化 500 个任务。");
        if (bindings.Any(static item => item.Status != ConfigurationStatuses.Draft))
            throw Failure(AcquisitionWorkflowFailureKind.Invalid, "批量实例化只创建草稿；发布前必须逐个完成真实数据验证。");
        var duplicate = bindings.GroupBy(static item => (NormalizeCode(item.TaskId), item.Version))
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid,
                $"任务 {duplicate.Key.Item1} v{duplicate.Key.Version} 在请求中重复。");

        var materialized = new List<(IngestionTaskBinding Binding, IngestionTask Task)>();
        var failures = new List<AcquisitionBatchFailure>();
        foreach (var raw in bindings)
        {
            if (!IngestionTaskValidator.TryValidateBinding(raw, out var binding, out var bindingErrors))
            {
                failures.Add(new AcquisitionBatchFailure(raw.TaskId, bindingErrors));
                continue;
            }
            try
            {
                var (template, source, model) = await ResolveDependenciesAsync(binding!, ct).ConfigureAwait(false);
                if (!IngestionTaskMaterializer.TryCreate(
                        template, source, binding!, model, out var task, out var materializationErrors))
                {
                    failures.Add(new AcquisitionBatchFailure(binding!.TaskId, materializationErrors));
                    continue;
                }
                var existing = await store.GetBindingAsync(binding!.TaskId, binding.Version, ct).ConfigureAwait(false);
                if (existing is not null && existing.Status != ConfigurationStatuses.Draft)
                {
                    failures.Add(new AcquisitionBatchFailure(binding.TaskId,
                        [new AcquisitionValidationError("status", "已发布或停用的任务绑定不可修改。") ]));
                    continue;
                }
                materialized.Add((binding, task!));
            }
            catch (AcquisitionWorkflowException exception)
            {
                failures.Add(new AcquisitionBatchFailure(binding!.TaskId,
                    exception.Validation.Count > 0
                        ? exception.Validation
                        : [new AcquisitionValidationError(string.Empty, exception.Message)]));
            }
        }
        if (failures.Count > 0)
            throw new AcquisitionWorkflowException(
                AcquisitionWorkflowFailureKind.Invalid,
                "批量实例化存在无效项目，未写入任何任务。",
                failures: failures);
        return await StoreAsync(() => store.SaveMaterializedTasksAsync(materialized, ct)).ConfigureAwait(false);
    }

    private async Task<(IngestionTaskTemplate Template, DataSourceInstance Source, ProcessDataModel Model)>
        ResolveDependenciesAsync(IngestionTaskBinding binding, CancellationToken ct)
    {
        var template = await store.GetTemplateAsync(
            binding.TemplateId, binding.TemplateVersion, ct).ConfigureAwait(false);
        var source = await store.GetDataSourceAsync(
            binding.DataSourceId, binding.DataSourceVersion, ct).ConfigureAwait(false);
        if (template is null || source is null)
            throw Failure(AcquisitionWorkflowFailureKind.Invalid,
                template is null ? "引用的任务模板不存在。" : "引用的数据源不存在。");
        var model = await processStore.GetDataModelAsync(
            template.DataModelId, template.DataModelVersion, ct).ConfigureAwait(false)
            ?? throw Failure(AcquisitionWorkflowFailureKind.Invalid, "模板引用的数据模型不存在。");
        return (template, source, model);
    }

    private static Task EnsureMutableAsync(object? existing, string nextStatus)
    {
        if (existing is null) return Task.CompletedTask;
        var status = existing switch
        {
            IngestionTaskTemplate template => template.Status,
            DataSourceInstance source => source.Status,
            _ => ConfigurationStatuses.Retired
        };
        if (status != ConfigurationStatuses.Draft)
            throw Failure(AcquisitionWorkflowFailureKind.Conflict,
                $"已发布或停用的配置不可修改，请创建新版本；请求状态为 {nextStatus}。");
        return Task.CompletedTask;
    }

    private static async Task<T> StoreAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action().ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            throw new AcquisitionWorkflowException(
                AcquisitionWorkflowFailureKind.Conflict,
                exception.Message,
                innerException: exception);
        }
    }

    private static AcquisitionWorkflowException ValidationFailure(
        IReadOnlyList<AcquisitionValidationError> errors)
        => new(AcquisitionWorkflowFailureKind.Invalid, Join(errors), errors);

    private static AcquisitionWorkflowException Failure(AcquisitionWorkflowFailureKind kind, string message)
        => new(kind, message);

    private static string Join(IEnumerable<AcquisitionValidationError> errors)
        => string.Join("；", errors.Select(static item => item.ToString()));

    private static string NormalizeCode(string? value)
        => value?.Trim().ToLowerInvariant() ?? string.Empty;
}
