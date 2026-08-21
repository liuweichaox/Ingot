namespace Ingot.Edge.Application.Abstractions;

public interface IMetricsCollector
{

    void RecordCollectionLatency(string sourceCode, string? channelCode, string measurement, double latencyMs);

    void RecordCollectionRate(string sourceCode, string? channelCode, string measurement, double pointsPerSecond);

    void RecordQueueDepth(int depth);

    void RecordProcessingLatency(double latencyMs);

    void RecordWriteLatency(string measurement, double latencyMs);

    void RecordBatchWriteEfficiency(int batchSize, double latencyMs);

    void RecordError(string sourceCode, string? channelCode = null, string? measurement = null);

    void RecordConnectionStatus(string sourceCode, bool isConnected);

    void RecordConnectionDuration(string sourceCode, double durationSeconds);

    void RecordEventEmitted(string eventType, double latencyMs);

    void RecordEventOutboxBacklog(long count);

    void RecordEventBacklogDropped(long count);

    void RecordContextStateEntries(long count);

    void RecordEventPersistenceFailure(string eventType);

    void RecordEventShipFailure(string edgeId);

    void RecordEventsShipped(string edgeId, int count, double latencyMs);
}
