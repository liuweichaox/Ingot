using Ingot.Contracts.Events;

namespace Ingot.Platform.Application.Inspections;

/// <summary>
///     检验应用规则所需的生产运行读取端口。实现负责完整翻页，Application 不感知事件存储查询模型。
/// </summary>
public interface IInspectionProductionEventReader
{
    Task<IReadOnlyList<PlatformProductionEvent>> QueryCompletedAsync(
        string? executionId,
        CancellationToken ct = default);
}
