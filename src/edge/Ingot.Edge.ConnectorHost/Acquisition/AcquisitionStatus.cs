namespace Ingot.Edge.ConnectorHost.Acquisition;

public sealed record AcquisitionStatusSnapshot(
    bool Enabled,
    string State,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    long SamplesCollected,
    string? ActiveRecipe,
    string? LastError,
    IReadOnlyList<AcquisitionTaskStatusSnapshot> Tasks);

public sealed record AcquisitionTaskStatusSnapshot(
    string ConfigurationKey,
    string State,
    DateTimeOffset LoadedAt,
    DateTimeOffset? LastAttemptAt,
    DateTimeOffset? LastSuccessAt,
    long SamplesCollected,
    string? ActiveRecipe,
    string? LastError);

public sealed class AcquisitionStatus
{
    private readonly object _gate = new();
    private readonly Dictionary<string, AcquisitionTaskStatusSnapshot> _tasks = new(StringComparer.Ordinal);
    private bool _enabled;

    public AcquisitionStatusSnapshot Get()
    {
        lock (_gate)
        {
            var tasks = _tasks.Values.OrderBy(static item => item.ConfigurationKey, StringComparer.Ordinal).ToArray();
            var state = !_enabled
                ? "disabled"
                : tasks.Length == 0
                    ? "starting"
                    : tasks.Any(static item => item.State == "degraded")
                        ? "degraded"
                        : tasks.Any(static item => item.State == "running")
                            ? "running"
                            : "starting";
            return new AcquisitionStatusSnapshot(
                _enabled,
                state,
                tasks.Select(static item => item.LastAttemptAt).Max(),
                tasks.Select(static item => item.LastSuccessAt).Max(),
                tasks.Sum(static item => item.SamplesCollected),
                tasks.Select(static item => item.ActiveRecipe).FirstOrDefault(static value => value is not null),
                tasks.Select(static item => item.LastError).FirstOrDefault(static value => value is not null),
                tasks);
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
            _enabled = enabled;
    }

    public void RegisterTask(string configurationKey)
    {
        lock (_gate)
        {
            if (!_tasks.ContainsKey(configurationKey))
                _tasks[configurationKey] = new AcquisitionTaskStatusSnapshot(
                    configurationKey,
                    "starting",
                    DateTimeOffset.UtcNow,
                    null,
                    null,
                    0,
                    null,
                    null);
        }
    }

    public void RemoveTask(string configurationKey)
    {
        lock (_gate)
            _tasks.Remove(configurationKey);
    }

    public void RecordAttempt(string configurationKey, DateTimeOffset timestamp)
    {
        lock (_gate)
        {
            if (_tasks.TryGetValue(configurationKey, out var task))
                _tasks[configurationKey] = task with { LastAttemptAt = timestamp };
        }
    }

    public void RecordSuccess(
        string configurationKey,
        DateTimeOffset timestamp,
        string? recipe,
        bool incrementSample = true)
    {
        lock (_gate)
        {
            if (_tasks.TryGetValue(configurationKey, out var task))
            {
                _tasks[configurationKey] = task with
                {
                    State = "running",
                    LastSuccessAt = timestamp,
                    SamplesCollected = task.SamplesCollected + (incrementSample ? 1 : 0),
                    ActiveRecipe = recipe,
                    LastError = null
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
        }
    }
}
