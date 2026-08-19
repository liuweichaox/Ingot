namespace Ingot.Platform.Infrastructure.Events;

/// <summary>
///     表示 Edge 身份、单调序号或 EventId 已被不同载荷占用。该状态不会通过重试恢复，
///     必须修复 Edge 身份或本地 outbox。
/// </summary>
public sealed class EventIngestConflictException(string message) : Exception(message);
