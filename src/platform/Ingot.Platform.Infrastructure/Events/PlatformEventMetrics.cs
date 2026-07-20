using Prometheus;

namespace Ingot.Platform.Infrastructure.Events;

public sealed class PlatformEventMetrics
{
    private readonly Counter _ingested = Metrics.CreateCounter(
        "event_ingest_total",
        "中心成功摄入的生产事件数量",
        new CounterConfiguration { LabelNames = ["edge"] });

    private readonly Counter _duplicates = Metrics.CreateCounter(
        "event_ingest_duplicates_total",
        "中心摄入时确认的重复生产事件数量",
        new CounterConfiguration { LabelNames = ["edge"] });

    private readonly Counter _gaps = Metrics.CreateCounter(
        "edge_seq_gap_detected_total",
        "检测到的边缘事件序号缺口数量",
        new CounterConfiguration { LabelNames = ["edge"] });

    public void Record(string edgeId, int accepted, int duplicates, bool gapDetected)
    {
        if (accepted > 0)
            _ingested.WithLabels(edgeId).Inc(accepted);
        if (duplicates > 0)
            _duplicates.WithLabels(edgeId).Inc(duplicates);
        if (gapDetected)
            _gaps.WithLabels(edgeId).Inc();
    }
}
