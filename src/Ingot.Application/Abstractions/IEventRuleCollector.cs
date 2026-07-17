using Ingot.Domain.Events;

namespace Ingot.Application.Abstractions;

/// <summary>
///     执行显式 EventRules 的采集与求值。
/// </summary>
public interface IEventRuleCollector
{
    Task CollectAsync(
        DeviceConfig config,
        EventRule rule,
        IPlcDataAccessClient client,
        CancellationToken ct = default);
}
