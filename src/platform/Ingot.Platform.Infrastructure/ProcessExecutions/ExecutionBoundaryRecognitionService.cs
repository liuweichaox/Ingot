using Ingot.Domain.Events;
using Ingot.Platform.Application.ProcessExecutions;
using Microsoft.Extensions.Logging;

namespace Ingot.Platform.Infrastructure.ProcessExecutions;

/// <summary>
/// 事件驱动的运行边界识别服务。
/// 监听新事件，识别运行边界，并保存到存储。
/// </summary>
public sealed class ExecutionBoundaryRecognitionService
{
    private readonly IExecutionBoundaryRecognizer _recognizer;
    private readonly IExecutionBoundaryStore _store;
    private readonly ILogger<ExecutionBoundaryRecognitionService> _logger;

    public ExecutionBoundaryRecognitionService(
        IExecutionBoundaryRecognizer recognizer,
        IExecutionBoundaryStore store,
        ILogger<ExecutionBoundaryRecognitionService> logger)
    {
        _recognizer = recognizer ?? throw new ArgumentNullException(nameof(recognizer));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 处理新摄入的事件，识别或修正运行边界。
    /// 此方法应在事件被持久化到 IPlatformEventStore 后调用。
    /// </summary>
    /// <param name="siteId">生产单元。</param>
    /// <param name="edgeId">采集节点。</param>
    /// <param name="events">新摄入的事件（已排序）。</param>
    /// <param name="options">识别配置选项。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task ProcessIngestedEventsAsync(
        string siteId,
        string edgeId,
        IReadOnlyList<ProductionEvent> events,
        ExecutionBoundaryRecognitionOptions options,
        CancellationToken ct)
    {
        if (events.Count == 0)
            return;

        try
        {
            // 识别这批事件包含的运行边界
            var boundaries = await _recognizer.RecognizeBoundariesAsync(
                siteId, edgeId, events, options, ct).ConfigureAwait(false);

            foreach (var boundary in boundaries)
            {
                // 检查是否已存在相同的 (site_id, source_execution_id) 记录
                var existing = await _store.GetBoundaryAsync(
                    siteId, boundary.SourceExecutionId, ct).ConfigureAwait(false);

                if (existing is null)
                {
                    // 新的运行，保存
                    await _store.SaveBoundaryAsync(boundary, ct).ConfigureAwait(false);
                    _logger.LogInformation(
                        "新运行边界已识别：Site={SiteId}, Edge={EdgeId}, ExecutionId={ExecutionId}, " +
                        "Events={EventCount}, Confidence={Confidence}",
                        siteId, edgeId, boundary.SourceExecutionId, boundary.EventCount, boundary.Confidence);
                }
                else
                {
                    // 已存在的运行，检查是否需要修正
                    if (boundary.EventCount > existing.EventCount ||
                        (boundary.EndedAt.HasValue && !existing.EndedAt.HasValue))
                    {
                        // 新事件包含了之前没有的信息，更新
                        var updated = existing with
                        {
                            EventCount = boundary.EventCount,
                            MinIngestId = Math.Min(existing.MinIngestId, boundary.MinIngestId),
                            MaxIngestId = Math.Max(existing.MaxIngestId, boundary.MaxIngestId),
                            EndedAt = boundary.EndedAt ?? existing.EndedAt,
                            Status = boundary.EndedAt.HasValue ? ExecutionBoundaryStatus.Completed : existing.Status,
                            LastObservedAt = boundary.LastObservedAt,
                            UpdatedAt = DateTimeOffset.UtcNow
                        };

                        await _store.UpdateBoundaryAsync(updated, ct).ConfigureAwait(false);
                        _logger.LogInformation(
                            "运行边界已修正：Site={SiteId}, ExecutionId={ExecutionId}, " +
                            "NewEventCount={NewEventCount}, Status={Status}",
                            siteId, existing.SourceExecutionId, updated.EventCount, updated.Status);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "运行边界识别处理失败：Site={SiteId}, Edge={EdgeId}, EventCount={EventCount}",
                siteId, edgeId, events.Count);
            throw;
        }
    }

    /// <summary>
    /// 处理晚到的事件（超出顺序的事件）。
    /// 检查事件是否属于现有运行，或需要开启新运行。
    /// </summary>
    /// <param name="siteId">生产单元。</param>
    /// <param name="edgeId">采集节点。</param>
    /// <param name="lateEvent">晚到的事件。</param>
    /// <param name="options">识别配置选项。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task ProcessLateArrivalEventAsync(
        string siteId,
        string edgeId,
        ProductionEvent lateEvent,
        ExecutionBoundaryRecognitionOptions options,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(lateEvent.ExecutionId))
        {
            _logger.LogWarning(
                "晚到事件缺少 ExecutionId，无法关联到运行：Site={SiteId}, EventId={EventId}, EventType={EventType}",
                siteId, lateEvent.EventId, lateEvent.EventType);
            return;
        }

        try
        {
            var existingBoundary = await _store.GetBoundaryAsync(
                siteId, lateEvent.ExecutionId, ct).ConfigureAwait(false);

            if (existingBoundary is null)
            {
                _logger.LogWarning(
                    "晚到事件对应的运行不存在，忽略：Site={SiteId}, ExecutionId={ExecutionId}",
                    siteId, lateEvent.ExecutionId);
                return;
            }

            // 检查晚到事件是否应该属于现有运行
            var adjustment = _recognizer.AdjustForLateArrival(existingBoundary, lateEvent, options);

            if (adjustment.NewBoundary is not null)
            {
                // 新事件应分入新运行
                await _store.SaveBoundaryAsync(adjustment.NewBoundary, ct).ConfigureAwait(false);
                _logger.LogInformation(
                    "晚到事件分入新运行：Site={SiteId}, ExecutionId={ExecutionId}, Reason={Reason}",
                    siteId, lateEvent.ExecutionId, adjustment.NewBoundary.ConfidenceReason);
            }
            else
            {
                // 晚到事件属于现有运行，更新
                await _store.UpdateBoundaryAsync(adjustment.AdjustedExisting, ct).ConfigureAwait(false);
                _logger.LogDebug(
                    "晚到事件已关联到现有运行：Site={SiteId}, ExecutionId={ExecutionId}, NewEventCount={EventCount}",
                    siteId, lateEvent.ExecutionId, adjustment.AdjustedExisting.EventCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "晚到事件处理失败：Site={SiteId}, ExecutionId={ExecutionId}",
                siteId, lateEvent.ExecutionId);
            throw;
        }
    }
}
