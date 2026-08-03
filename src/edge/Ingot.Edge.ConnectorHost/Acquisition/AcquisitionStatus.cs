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
            var deployments = _deployments.Values.OrderBy(static item => item.ProfileId, StringComparer.Ordinal).ToArray();
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
                tasks.Select(static item => item.LastSuccessAt).Max(),
                tasks.Sum(static item => item.SamplesCollected),
                tasks.OrderByDescending(static item => item.LastSuccessAt)
                    .Select(static item => item.LastReadDurationMs).FirstOrDefault(),
                tasks.OrderByDescending(static item => item.LastSuccessAt)
                    .Select(static item => item.ObservedIntervalMs).FirstOrDefault(),
                tasks.Select(static item => item.ActiveRecipe).FirstOrDefault(static value => value is not null),
                _configurationError ??
                deployments.Select(static item => item.LastError).FirstOrDefault(static value => value is not null) ??
                tasks.Select(static item => item.LastError).FirstOrDefault(static value => value is not null),
                tasks,
                deployments);
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
            var activeProfileIds = deployments.Select(static item => item.Profile.ProfileId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var removed in _deployments.Keys.Where(item => !activeProfileIds.Contains(item)).ToArray())
                _deployments.Remove(removed);

            foreach (var deployment in deployments)
            {
                var profile = deployment.Profile;
                var hash = AcquisitionDeploymentFingerprint.Compute(deployment);
                if (_deployments.TryGetValue(profile.ProfileId, out var existing))
                {
                    _deployments[profile.ProfileId] = existing with
                    {
                        DesiredVersion = profile.Version,
                        DesiredConfigurationHash = hash,
                        DesiredAt = existing.DesiredVersion == profile.Version &&
                                    string.Equals(existing.DesiredConfigurationHash, hash, StringComparison.Ordinal)
                            ? existing.DesiredAt
                            : now,
                        State = existing.AppliedVersion == profile.Version &&
                                string.Equals(existing.AppliedConfigurationHash, hash, StringComparison.Ordinal)
                            ? AcquisitionApplicationStates.Applied
                            : AcquisitionApplicationStates.Pending,
                        LastError = null
                    };
                }
                else
                {
                    _deployments[profile.ProfileId] = new AcquisitionDeploymentApplicationStatus(
                        profile.ProfileId,
                        profile.Version,
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

    public void RecordApplicationState(string profileId, string state, string? error = null)
    {
        lock (_gate)
        {
            if (_deployments.TryGetValue(profileId, out var deployment))
                _deployments[profileId] = deployment with
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
            if (_tasks.TryGetValue(configurationKey, out var task) && task.LastSuccessAt.HasValue)
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

    public void RecordCycleState(string configurationKey, bool active)
    {
        lock (_gate)
        {
            if (_tasks.TryGetValue(configurationKey, out var task))
                _tasks[configurationKey] = task with { CycleActive = active };
        }
    }

    public bool IsSafeToReplace(string configurationKey)
    {
        lock (_gate)
            return !_tasks.TryGetValue(configurationKey, out var task) || !task.CycleActive;
    }

    public bool IsApplied(string configurationKey)
    {
        lock (_gate)
        {
            if (!_taskIdentities.TryGetValue(configurationKey, out var identity) ||
                !_deployments.TryGetValue(identity.ProfileId, out var deployment))
                return string.Equals(configurationKey, "local", StringComparison.Ordinal) &&
                       _tasks.TryGetValue(configurationKey, out var local) &&
                       local.LastSuccessAt.HasValue;
            return deployment.AppliedVersion == identity.Version &&
                   string.Equals(deployment.AppliedConfigurationHash, identity.Hash, StringComparison.Ordinal);
        }
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

    public void RecordSuccess(
        string configurationKey,
        DateTimeOffset timestamp,
        string? recipe,
        bool incrementSample = true,
        double? readDurationMs = null)
    {
        lock (_gate)
        {
            if (!_tasks.TryGetValue(configurationKey, out var task))
                return;
            _tasks[configurationKey] = task with
            {
                State = "running",
                LastSuccessAt = timestamp,
                SamplesCollected = task.SamplesCollected + (incrementSample ? 1 : 0),
                LastReadDurationMs = readDurationMs ?? task.LastReadDurationMs,
                ObservedIntervalMs = task.LastSuccessAt.HasValue
                    ? (timestamp - task.LastSuccessAt.Value).TotalMilliseconds
                    : task.ObservedIntervalMs,
                ActiveRecipe = recipe,
                LastError = null
            };
            if (_firstSuccessSignals.TryGetValue(configurationKey, out var signal))
                signal.TrySetResult(timestamp);

            if (_taskIdentities.TryGetValue(configurationKey, out var identity) &&
                _deployments.TryGetValue(identity.ProfileId, out var deployment))
            {
                var converged = deployment.DesiredVersion == identity.Version &&
                                string.Equals(deployment.DesiredConfigurationHash, identity.Hash, StringComparison.Ordinal);
                _deployments[identity.ProfileId] = deployment with
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

    public void RecordFailure(string configurationKey, string error)
    {
        lock (_gate)
        {
            if (_tasks.TryGetValue(configurationKey, out var task))
                _tasks[configurationKey] = task with { State = "degraded", LastError = error };
            if (_taskIdentities.TryGetValue(configurationKey, out var identity) &&
                _deployments.TryGetValue(identity.ProfileId, out var deployment) &&
                deployment.AppliedVersion != identity.Version)
            {
                _deployments[identity.ProfileId] = deployment with
                {
                    State = deployment.AppliedVersion.HasValue
                        ? AcquisitionApplicationStates.Rollback
                        : AcquisitionApplicationStates.Failed,
                    LastError = error
                };
            }
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
                0,
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
                deployment.Profile.ProfileId,
                deployment.Profile.Version,
                AcquisitionDeploymentFingerprint.Compute(deployment));
        }
    }

    private static string? ComputeAppliedSetHash(
        IReadOnlyList<AcquisitionDeploymentApplicationStatus> deployments)
    {
        var applied = deployments
            .Where(static item => item.AppliedVersion.HasValue && item.AppliedConfigurationHash is not null)
            .Select(static item => $"{item.ProfileId}@{item.AppliedVersion}:{item.AppliedConfigurationHash}")
            .OrderBy(static item => item, StringComparer.Ordinal)
            .ToArray();
        if (applied.Length == 0)
            return null;
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join('\n', applied))));
    }

    private sealed record TaskIdentity(string ProfileId, int Version, string Hash);
}
