using Ingot.Application.Abstractions;
using Ingot.Domain.Events;
using Ingot.Domain.Models;
using Ingot.Infrastructure.Acquisition;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ingot.Infrastructure.Events;

/// <summary>
///     显式 EventRules 的轮询执行器。它复用现有 PLC 读取能力，但产物是源中立事件。
/// </summary>
public sealed class EventRuleCollector : IEventRuleCollector
{
    private readonly IHeartbeatMonitor _heartbeat;
    private readonly IEdgeStateStore _cycles;
    private readonly IEdgeContextStore _context;
    private readonly IEventSink _events;
    private readonly ILogger<EventRuleCollector> _logger;
    private readonly int _connectionRetryMs;
    private readonly int _triggerDelayMs;
    private readonly string _edgeId;

    public EventRuleCollector(
        IHeartbeatMonitor heartbeat,
        IEdgeStateStore cycles,
        IEdgeContextStore context,
        IEventSink events,
        IConfiguration configuration,
        IOptions<AcquisitionOptions> options,
        ILogger<EventRuleCollector> logger)
    {
        _heartbeat = heartbeat;
        _cycles = cycles;
        _context = context;
        _events = events;
        _logger = logger;
        _edgeId = configuration["Edge:EdgeId"]?.Trim() ?? Environment.MachineName;
        _connectionRetryMs = options.Value.ChannelCollector.ConnectionCheckRetryDelayMs;
        _triggerDelayMs = options.Value.ChannelCollector.TriggerWaitDelayMs;
    }

    public async Task CollectAsync(
        DeviceConfig config,
        EventRule rule,
        IPlcDataAccessClient client,
        CancellationToken ct = default)
    {
        object? previous = null;
        var subject = rule.Subject ?? config.Asset ?? new ObjectRef("equipment", config.SourceCode);
        var stateChannel = $"event-rule:{rule.RuleId}";

        while (!ct.IsCancellationRequested)
        {
            if (!_heartbeat.TryGetConnectionHealth(config.SourceCode, out var connected) || !connected)
            {
                await Task.Delay(_connectionRetryMs, ct).ConfigureAwait(false);
                continue;
            }

            try
            {
                var current = await PlcValueAccessor.ReadAsync(
                    client,
                    rule.Trigger.Tag,
                    rule.Trigger.DataType,
                    rule.Trigger.StringByteLength,
                    rule.Trigger.Encoding).ConfigureAwait(false);

                if (previous is null)
                {
                    await RecoverPairStateAsync(
                            config,
                            rule,
                            subject,
                            stateChannel,
                            current,
                            ct)
                        .ConfigureAwait(false);
                }
                else
                {
                    var evaluation = EventRuleEvaluator.Evaluate(rule, previous, current);
                    var occurredAt = DateTimeOffset.UtcNow;

                    if (evaluation.ShouldEmit)
                    {
                        await ApplyContextAsync(subject, rule, current, ct).ConfigureAwait(false);
                        await EmitAsync(
                            config,
                            rule,
                            subject,
                            rule.GetEventType(),
                            null,
                            current,
                            occurredAt,
                            null,
                            ct).ConfigureAwait(false);
                    }

                    if (evaluation.ShouldComplete)
                    {
                        var cycle = _cycles.EndCycle(config.SourceCode, stateChannel, rule.Category);
                        if (cycle is not null)
                        {
                            var snapshotData = await ReadSnapshotAsync(
                                    config,
                                    rule,
                                    client,
                                    rule.SnapshotOnEnd,
                                    ct)
                                .ConfigureAwait(false);
                            await EmitAsync(
                                config,
                                rule,
                                subject,
                                rule.GetCompletedEventType(),
                                cycle.CycleId,
                                current,
                                occurredAt,
                                snapshotData,
                                ct).ConfigureAwait(false);
                        }
                    }

                    if (evaluation.ShouldStart)
                    {
                        var cycle = _cycles.StartCycle(config.SourceCode, stateChannel, rule.Category);
                        var snapshotData = await ReadSnapshotAsync(
                                config,
                                rule,
                                client,
                                rule.SnapshotOnStart,
                                ct)
                            .ConfigureAwait(false);
                        await EmitAsync(
                            config,
                            rule,
                            subject,
                            rule.GetStartedEventType(),
                            cycle.CycleId,
                            current,
                            occurredAt,
                            snapshotData,
                            ct).ConfigureAwait(false);
                    }
                }

                previous = current;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "事件规则求值失败：Source={SourceCode}, Rule={RuleId}",
                    config.SourceCode,
                    rule.RuleId);
            }

            await Task.Delay(_triggerDelayMs, ct).ConfigureAwait(false);
        }
    }

    private async Task RecoverPairStateAsync(
        DeviceConfig config,
        EventRule rule,
        ObjectRef subject,
        string stateChannel,
        object? current,
        CancellationToken ct)
    {
        if (rule.Trigger.Kind == EventTriggerKind.ValueChanged)
            return;

        var active = EventRuleEvaluator.IsPairActive(rule, current);
        var existing = _cycles.GetActiveCycle(config.SourceCode, stateChannel, rule.Category);
        if (active)
        {
            var recovered = existing ??
                            _cycles.StartCycle(config.SourceCode, stateChannel, rule.Category);
            await EmitAsync(
                    config,
                    rule,
                    subject,
                    "diagnostic.cycle_recovered",
                    recovered.CycleId,
                    current,
                    DateTimeOffset.UtcNow,
                    null,
                    ct)
                .ConfigureAwait(false);
            return;
        }

        if (existing is not null)
        {
            var interrupted = _cycles.EndCycle(config.SourceCode, stateChannel, rule.Category);
            if (interrupted is not null)
            {
                await EmitAsync(
                        config,
                        rule,
                        subject,
                        "diagnostic.cycle_interrupted",
                        interrupted.CycleId,
                        current,
                        DateTimeOffset.UtcNow,
                        null,
                        ct)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task EmitAsync(
        DeviceConfig config,
        EventRule rule,
        ObjectRef subject,
        string eventType,
        string? correlationId,
        object? triggerValue,
        DateTimeOffset occurredAt,
        IReadOnlyDictionary<string, object?>? snapshotData,
        CancellationToken ct)
    {
        var snapshot = _context.Snapshot(subject, rule.ContextKeys);
        var data = new Dictionary<string, object?>(rule.Data, StringComparer.Ordinal)
        {
            ["rule_id"] = rule.RuleId,
            ["trigger_tag"] = rule.Trigger.Tag,
            ["trigger_value"] = triggerValue
        };
        if (snapshotData is not null)
        {
            foreach (var pair in snapshotData)
                data[pair.Key] = pair.Value;
        }

        var missingContext = rule.ContextKeys
            .Where(key => !snapshot.ContainsKey(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingContext.Length > 0)
        {
            data["context_complete"] = false;
            data["missing_context_keys"] = missingContext;
        }

        var evt = ProductionEvent.Create(
            eventType,
            occurredAt,
            $"edge/{_edgeId}/{config.SourceCode}/{rule.RuleId}",
            subject,
            correlationId,
            snapshot,
            data);
        await _events.EmitAsync(evt, ct).ConfigureAwait(false);
    }

    private async Task<IReadOnlyDictionary<string, object?>> ReadSnapshotAsync(
        DeviceConfig config,
        EventRule rule,
        IPlcDataAccessClient client,
        IReadOnlyCollection<EventSnapshotField> fields,
        CancellationToken ct)
    {
        if (fields.Count == 0)
            return new Dictionary<string, object?>();

        var data = new Dictionary<string, object?>(StringComparer.Ordinal);
        var failures = new List<string>();
        foreach (var field in fields)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                data[field.FieldName] = await PlcValueAccessor.ReadAsync(
                        client,
                        field.Tag,
                        field.DataType,
                        field.StringByteLength,
                        field.Encoding)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(field.FieldName);
                _logger.LogWarning(
                    ex,
                    "事件快照字段读取失败，事件仍会发出：Source={SourceCode}, Rule={RuleId}, Field={FieldName}, Tag={Tag}",
                    config.SourceCode,
                    rule.RuleId,
                    field.FieldName,
                    field.Tag);
            }
        }

        if (failures.Count > 0)
            data["snapshot_errors"] = failures;
        return data;
    }

    private async Task ApplyContextAsync(
        ObjectRef subject,
        EventRule rule,
        object? triggerValue,
        CancellationToken ct)
    {
        foreach (var pair in rule.SetContext)
        {
            var value = string.Equals(pair.Value, "$value", StringComparison.OrdinalIgnoreCase)
                ? triggerValue?.ToString() ?? string.Empty
                : pair.Value;
            await _context.SetAsync(subject, pair.Key, value, ct).ConfigureAwait(false);
        }
    }
}
