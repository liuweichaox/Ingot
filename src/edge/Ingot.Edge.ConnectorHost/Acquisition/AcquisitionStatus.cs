
using Ingot.Contracts.Acquisition;

namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed class AcquisitionStatus
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AcquisitionTaskRuntimeStatus> _tasks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskIdentity> _taskIdentities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TaskCompletionSource<DateTimeOffset>> _firstSuccessSignals =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, AcquisitionDeploymentApplicationStatus> _deployments =
        new(StringComparer.Ordinal);
    private bool _enabled;
    private string? _configurationError;
    private string? _configurationSource;
    private string? _desiredConfigurationSetHash;

    public EdgeAcquisitionRuntimeStatus Get()
    {
        lock (_gate)
        {
            var tasks = _tasks.Values.OrderBy(static item => item.ConfigurationKey, StringComparer.Ordinal).ToArray();
            var deployments = _deployments.Values.OrderBy(static item => item.TaskId, StringComparer.Ordinal).ToArray();
            var state = !_enabled
                ? "disabled"
                : _configurationError is not null ||
                  deployments.Any(static item => item.State is AcquisitionApplicationStates.Failed or
                      AcquisitionApplicationStates.Rollback) ||
                  tasks.Any(static item => item.State == "degraded")
                    ? "degraded"
                    : tasks.Length == 0
                        ? "starting"
                        : tasks.Any(static item => item.State == "running")
                            ? "running"
                            : "starting";
            return new EdgeAcquisitionRuntimeStatus(
                _enabled,
                state,
                DateTimeOffset.UtcNow,
                _configurationSource,
                _desiredConfigurationSetHash,
                ComputeAppliedSetHash(deployments),
                tasks.Select(static item => item.LastAttemptAt).Max(),
                tasks.Select(static item => item.LastReadSuccessAt).Max(),
                tasks.Select(static item => item.LastValidSnapshotAt).Max(),
                tasks.Sum(static item => item.ReadSuccessCount),
                tasks.Sum(static item => item.ValidSnapshotCount),
                tasks.Sum(static item => item.EmittedEventCount),
                tasks.Sum(static item => item.DuplicateSuppressionCount),
                tasks.Sum(static item => item.InactiveSnapshotCount),
                tasks.Sum(static item => item.SourceIdentityStallCount),
                tasks.OrderByDescending(static item => item.LastReadSuccessAt)
                    .Select(static item => item.LastReadDurationMs).FirstOrDefault(),
                tasks.OrderByDescending(static item => item.LastReadSuccessAt)
                    .Select(static item => item.ObservedIntervalMs).FirstOrDefault(),
                tasks.Select(static item => item.ActiveProcessSpecification).FirstOrDefault(static value => value is not null),
                _configurationError ??
                deployments.Select(static item => item.LastError).FirstOrDefault(static value => value is not null) ??
                tasks.Select(static item => item.LastError).FirstOrDefault(static value => value is not null),
                tasks,
                deployments,
                tasks.Sum(static item => item.StaleSnapshotRejectionCount),
                tasks.Sum(static item => item.StaleValueRejectionCount));
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
            _enabled = enabled;
    }

    public void SetConfigurationError(string? error)
    {
        lock (_gate)
            _configurationError = string.IsNullOrWhiteSpace(error) ? null : error.Trim();
    }

    public void SetDesiredDeployments(
        IReadOnlyList<AcquisitionDeployment> deployments,
        string configurationSource)
    {
        ArgumentNullException.ThrowIfNull(deployments);
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationSource);
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            _configurationSource = configurationSource;
            _desiredConfigurationSetHash = AcquisitionDeploymentFingerprint.ComputeSet(deployments);
            var activeTaskIds = deployments.Select(static item => item.Task.TaskId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var removed in _deployments.Keys.Where(item => !activeTaskIds.Contains(item)).ToArray())
                _deployments.Remove(removed);

            foreach (var deployment in deployments)
            {
                var task = deployment.Task;
                var hash = AcquisitionDeploymentFingerprint.Compute(deployment);
                if (_deployments.TryGetValue(task.TaskId, out var existing))
                {
                    _deployments[task.TaskId] = existing with
                    {
                        DesiredVersion = task.Version,
                        DesiredConfigurationHash = hash,
                        DesiredAt = existing.DesiredVersion == task.Version &&
                                    string.Equals(existing.DesiredConfigurationHash, hash, StringComparison.Ordinal)
                            ? existing.DesiredAt
                            : now,
                        State = existing.AppliedVersion == task.Version &&
                                string.Equals(existing.AppliedConfigurationHash, hash, StringComparison.Ordinal)
                            ? AcquisitionApplicationStates.Applied
                            : AcquisitionApplicationStates.Pending,
                        LastError = null
                    };
                }
                else
                {
                    _deployments[task.TaskId] = new AcquisitionDeploymentApplicationStatus(
                        task.TaskId,
                        task.Version,
                        hash,
                        null,
                        null,
                        AcquisitionApplicationStates.Pending,
                        now,
                        null,
                        null);
                }
            }
        }
    }

    public void RecordApplicationState(string taskId, string state, string? error = null)
    {
        lock (_gate)
        {
            if (_deployments.TryGetValue(taskId, out var deployment))
                _deployments[taskId] = deployment with
                {
                    State = state,
                    LastError = string.IsNullOrWhiteSpace(error) ? null : error.Trim()
                };
        }
    }

    public void RegisterTask(string configurationKey)
    {
        lock (_gate)
            RegisterTaskCore(configurationKey, null);
    }

    public void RegisterTask(string configurationKey, AcquisitionDeployment deployment)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        lock (_gate)
            RegisterTaskCore(configurationKey, deployment);
    }

    public void RemoveTask(string configurationKey)
    {
        lock (_gate)
        {
            _tasks.Remove(configurationKey);
            _taskIdentities.Remove(configurationKey);
            if (_firstSuccessSignals.Remove(configurationKey, out var signal))
                signal.TrySetCanceled();
        }
    }

    public async Task<bool> WaitForFirstSuccessAsync(
        string configurationKey,
        TimeSpan timeout,
        CancellationToken ct)
    {
        Task<DateTimeOffset> signal;
        lock (_gate)
        {
            if (_tasks.TryGetValue(configurationKey, out var task) && task.LastValidSnapshotAt.HasValue)
                return true;
            if (!_firstSuccessSignals.TryGetValue(configurationKey, out var source))
                return false;
            signal = source.Task;
        }

        try
        {
            await signal.WaitAsync(timeout, ct).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    public void RecordAttempt(string configurationKey, DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            if (_tasks.TryGetValue(configurationKey, out var task))
                _tasks[configurationKey] = task with { LastAttemptAt = timestamp };
        }
    }

    public void RecordProcessExecutionState(string configurationKey, bool active)
    {
        lock (_gate)
        {
            if (_tasks.TryGetValue(configurationKey, out var task))
                _tasks[configurationKey] = task with { ProcessExecutionActive = active };
        }
    }

    public bool IsSafeToReplace(string configurationKey)
    {
        lock (_gate)
            return !_tasks.TryGetValue(configurationKey, out var task) || !task.ProcessExecutionActive;
    }

    public bool AreDesiredDeploymentsApplied()
    {
        lock (_gate)
            return _deployments.Count > 0 && _deployments.Values.All(static item =>
                item.AppliedVersion == item.DesiredVersion &&
                string.Equals(
                    item.AppliedConfigurationHash,
                    item.DesiredConfigurationHash,
                    StringComparison.Ordinal));
    }

    public void RecordReadSuccess(
        string configurationKey,
        DateTimeOffset timestamp,
        double? readDurationMs = null)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(configurationKey, out var task))
                return;
            _tasks[configurationKey] = task with
            {
                LastReadSuccessAt = timestamp,
                ReadSuccessCount = task.ReadSuccessCount + 1,
                LastReadDurationMs = readDurationMs ?? task.LastReadDurationMs,
                ObservedIntervalMs = task.LastReadSuccessAt.HasValue
                    ? (timestamp - task.LastReadSuccessAt.Value).TotalMilliseconds
                    : task.ObservedIntervalMs
            };
        }
    }

    public void RecordValidSnapshot(
        string configurationKey,
        DateTimeOffset timestamp,
        string? processSpecification,
        DateTimeOffset? sourceIdentityChangedAt = null)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(configurationKey, out var task))
                return;
            _tasks[configurationKey] = task with
            {
                State = "running",
                LastValidSnapshotAt = timestamp,
                ValidSnapshotCount = task.ValidSnapshotCount + 1,
                LastSourceIdentityChangeAt = sourceIdentityChangedAt ?? task.LastSourceIdentityChangeAt,
                ActiveProcessSpecification = processSpecification,
                LastError = null
            };
            if (_firstSuccessSignals.TryGetValue(configurationKey, out var signal))
                signal.TrySetResult(timestamp);

            if (_taskIdentities.TryGetValue(configurationKey, out var identity) &&
                _deployments.TryGetValue(identity.TaskId, out var deployment))
            {
                var converged = deployment.DesiredVersion == identity.Version &&
                                string.Equals(deployment.DesiredConfigurationHash, identity.Hash, StringComparison.Ordinal);
                _deployments[identity.TaskId] = deployment with
                {
                    AppliedVersion = identity.Version,
                    AppliedConfigurationHash = identity.Hash,
                    AppliedAt = deployment.AppliedVersion == identity.Version &&
                                string.Equals(deployment.AppliedConfigurationHash, identity.Hash, StringComparison.Ordinal)
                        ? deployment.AppliedAt
                        : timestamp,
                    State = converged
                        ? AcquisitionApplicationStates.Applied
                        : AcquisitionApplicationStates.Rollback,
                    LastError = converged ? null : deployment.LastError
                };
            }
        }
    }

    public void RecordDuplicateSnapshot(
        string configurationKey,
        bool stalled,
        string? error = null)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(configurationKey, out var task))
                return;
            _tasks[configurationKey] = task with
            {
                State = stalled ? "degraded" : "running",
                DuplicateSuppressionCount = task.DuplicateSuppressionCount + 1,
                SourceIdentityStallCount = task.SourceIdentityStallCount + (stalled ? 1 : 0),
                LastError = stalled
                    ? string.IsNullOrWhiteSpace(error) ? "设备源序号或源时间戳停止变化。" : error.Trim()
                    : null
            };
        }
    }

    public void RecordEmissionOutcome(string configurationKey, int emittedEventCount, bool inactive)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(configurationKey, out var task))
                return;
            _tasks[configurationKey] = task with
            {
                EmittedEventCount = task.EmittedEventCount + Math.Max(0, emittedEventCount),
                InactiveSnapshotCount = task.InactiveSnapshotCount + (inactive ? 1 : 0)
            };
        }
    }

    public void RecordFailure(string configurationKey, string error)
    {
        lock (_gate)
        {
            if (_tasks.TryGetValue(configurationKey, out var task))
                _tasks[configurationKey] = task with { State = "degraded", LastError = error };
            if (_taskIdentities.TryGetValue(configurationKey, out var identity) &&
                _deployments.TryGetValue(identity.TaskId, out var deployment) &&
                deployment.AppliedVersion != identity.Version)
            {
                _deployments[identity.TaskId] = deployment with
                {
                    State = deployment.AppliedVersion.HasValue
                        ? AcquisitionApplicationStates.Rollback
                        : AcquisitionApplicationStates.Failed,
                    LastError = error
                };
            }
        }
    }

    public void RecordStaleSnapshotRejection(
        string configurationKey,
        int staleValueCount,
        string error)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(configurationKey, out var task))
                return;
            _tasks[configurationKey] = task with
            {
                State = "degraded",
                LastError = error,
                StaleSnapshotRejectionCount = task.StaleSnapshotRejectionCount + 1,
                StaleValueRejectionCount = task.StaleValueRejectionCount + Math.Max(0, staleValueCount)
            };
        }
    }

    private void RegisterTaskCore(string configurationKey, AcquisitionDeployment? deployment)
    {
        if (!_tasks.ContainsKey(configurationKey))
        {
            _tasks[configurationKey] = new AcquisitionTaskRuntimeStatus(
                configurationKey,
                "starting",
                DateTimeOffset.UtcNow,
                null,
                null,
                null,
                0,
                0,
                0,
                0,
                0,
                0,
                null,
                null,
                null,
                null,
                null,
                false);
            _firstSuccessSignals[configurationKey] =
                new TaskCompletionSource<DateTimeOffset>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        if (deployment is not null)
        {
            _taskIdentities[configurationKey] = new TaskIdentity(
                deployment.Task.TaskId,
                deployment.Task.Version,
                AcquisitionDeploymentFingerprint.Compute(deployment));
        }
    }

    private static string? ComputeAppliedSetHash(
        IReadOnlyList<AcquisitionDeploymentApplicationStatus> deployments)
    {
        var applied = deployments
            .Where(static item => item.AppliedVersion.HasValue && item.AppliedConfigurationHash is not null)
            .Select(static item => $"{item.TaskId}@{item.AppliedVersion}:{item.AppliedConfigurationHash}")
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        if (applied.Length == 0)
            return null;
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join('\n', applied))));
    }

    private sealed record TaskIdentity(string TaskId, int Version, string Hash);
}
