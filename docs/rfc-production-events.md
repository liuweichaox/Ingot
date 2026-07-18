# 生产事件规范

`ProductionEvent` 是连接器、Connector Host、Central API 和 Chat 事实工具共享的不可变事件信封。

## JSON 契约

| 字段 | 类型 | 约束 |
|---|---|---|
| `eventId` | string | UUIDv7；事件全局标识 |
| `eventType` | string | 小写点分名称，例如 `cycle.started` |
| `eventTypeVersion` | integer | 大于 0；默认值为 1 |
| `occurredAt` | ISO 8601 timestamp | 连接器观察到事实发生的 UTC 时间 |
| `recordedAt` | ISO 8601 timestamp | 连接器请求必须提供；Connector Host 持久化时重写为主机 UTC 时间 |
| `source` | string | 连接器内的来源路径；Host 统一添加 `edge/{EdgeId}/` 前缀 |
| `subject` | object | 必须包含非空 `type` 和 `id` |
| `context` | object | 字符串键值；不能为空对象引用，但可以是 `{}` |
| `data` | object | 当前事件的业务字段；可以是 `{}` |
| `correlationId` | string 或 null | 可选的周期或业务链关联标识 |
| `seq` | integer | 连接器使用 0；Connector Host 持久化时分配单调本地顺序号 |

示例：

```json
{
  "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
  "eventType": "cycle.started",
  "eventTypeVersion": 1,
  "occurredAt": "2026-07-18T08:00:00Z",
  "recordedAt": "2026-07-18T08:00:00Z",
  "source": "connector/FURNACE-01",
  "subject": { "type": "asset", "id": "FURNACE-01" },
  "context": { "workpiece_id": "WP-001", "lot": "LOT-0718" },
  "data": {},
  "correlationId": "CYCLE-001",
  "seq": 0
}
```

## Connector Host 入口

```http
POST /api/v1/connector-events
Authorization: Bearer <connector-host-token>
Content-Type: application/json
```

请求体是 `ProductionEvent[]`。Host 执行以下步骤：

1. 校验 Bearer Token 和 `ConnectorHost:MaxBatchSize`。
2. 拒绝空来源或声明其他 Edge 的 `source`。
3. 将来源规范化为 `edge/{EdgeId}/{connector-source}`。
4. 把 `seq` 设为 0，并使用主机 UTC 时间设置 `recordedAt`。
5. 执行 `ProductionEventValidator`。
6. 写入 SQLite 事件日志，由数据库分配本地 `seq`。
7. 返回 `202 Accepted`、事件 ID 和本地顺序号。

## 上报与幂等

```text
Connector
  → Connector Host
  → SQLite event log + outbox
  → POST /api/v1/events:batch
  → Central PostgreSQL fact store
```

上报使用至少一次重试。Central 按 `EventId` 和 `(EdgeId, Seq)` 去重，返回已确认的 `AckSeq`；Connector Host 只在收到有效确认后标记对应 outbox 记录已上报。该机制不承诺端到端 exactly-once。

outbox 由 `Events:MaxBacklogRows` 设置硬上限，默认 500,000 条。达到上限时，Host 删除最旧的未上报事件，并写入 `diagnostic.backlog_dropped` 审计事件、丢弃数量、顺序号范围和指标；部署方必须监控该事件和 `event_backlog_dropped_total`。

Central 批次要求每条 `source` 以 `edge/{EdgeId}/` 开头。Connector Host 的来源规范化确保连接器提交的数据满足该契约。

## 查询与事实链

- `GET /api/v1/events` 支持按 Edge、事件类型、对象、上下文、`correlationId` 和时间查询。
- `GET /api/v1/events/stream` 提供事件 SSE。
- `GET /api/v1/cycles/{correlationId}` 返回同一关联 ID 的事件。
- `get_cycle_trace` 按发生时间和中心摄入顺序生成事件时间线与 `EvidenceRef`。
- `check_data_quality` 检查周期配对、空上下文、Edge 序号间断和最新事件时间。

检测记录使用独立的 `InspectionRecord` 契约和 API。当前 `get_cycle_trace` 不自动合并检测记录。

## 扩展规则

- `eventType` 和 `data` 字段使用稳定业务名称。
- 数值单位作为明确字段随事件传递，核心不推断单位。
- 源质量码和时间戳语义写入连接器规格。
- 语义不兼容时增加 `eventTypeVersion` 或使用新的事件类型。
- `context` 只存储建立查询关联所需的稳定字符串，不写入密钥或大对象。
