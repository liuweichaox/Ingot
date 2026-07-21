# 生产事件规范

`ProductionEvent` 是使用方数据源适配、Platform API、查询和 Ingot Chat 共享的不可变事件信封。使用方负责将设备、仪器或业务系统的含义映射为该契约，并通过 Platform API 提交。

## 批次请求

```http
POST /api/v1/events:batch
Authorization: Bearer <edge-token>
Content-Type: application/json
```

```json
{
  "edgeId": "EDGE-001",
  "events": [
    {
      "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
      "eventType": "cycle.started",
      "eventTypeVersion": 1,
      "occurredAt": "2026-07-18T08:00:00Z",
      "recordedAt": "2026-07-18T08:00:00Z",
      "source": "edge/EDGE-001/furnace/FURNACE-01",
      "subject": { "type": "asset", "id": "FURNACE-01" },
      "context": {
        "workpiece_id": "WP-001",
        "product_code": "LENS-A",
        "operation_code": "molding",
        "recipe_id": "RCP-LENS-A",
        "recipe_version": "3",
        "recipe_template": "optical-molding",
        "recipe_step": "4",
        "recipe_step_name": "anneal",
        "mold_id": "MOLD-02",
        "mold_shot_count": "12880",
        "preform_lot": "PF-0718",
        "cavity_id": "C1"
      },
      "data": {},
      "correlationId": "CYCLE-001",
      "seq": 1
    }
  ]
}
```

## `ProductionEvent` 字段

| 字段 | 类型 | 约束 |
|---|---|---|
| `eventId` | string | UUIDv7；事件全局标识 |
| `eventType` | string | 小写点分名称，例如 `cycle.started` |
| `eventTypeVersion` | integer | 大于 0；默认值为 1 |
| `occurredAt` | ISO 8601 timestamp | 记录发生的 UTC 时间 |
| `recordedAt` | ISO 8601 timestamp | 适配程序记录或提交事件的 UTC 时间 |
| `source` | string | 必须以 `edge/{edgeId}/` 开头 |
| `subject` | object | 必须包含非空 `type` 和 `id` |
| `context` | object | 查询关联所需的字符串键值；可为 `{}` |
| `data` | object | 当前事件的业务字段；可为 `{}` |
| `correlationId` | string 或 null | 可选的周期或业务链关联标识 |
| `seq` | integer | 同一 `edgeId` 内单调递增的持久化序号 |

## 校验与响应

- `edgeId` 只能包含字母、数字、点、下划线和连字符，长度为 1 至 128；
- 每批必须有 1 至 500 条事件；
- 批次内 `eventId` 与 `seq` 不能重复；
- 所有 `source` 必须与请求 `edgeId` 匹配；
- Bearer Token 必须与该 `edgeId` 的配置令牌匹配；
- Platform 按 `eventId` 与 `(edgeId, seq)` 去重。

成功响应包含：

```json
{
  "accepted": 1,
  "duplicates": 0,
  "ackSeq": 1,
  "gapDetected": false
}
```

`ackSeq` 表示此前连续序号均已安全接收或确认重复。调用方应持久化待确认事件，按确认结果重试；该机制不承诺端到端 exactly-once。

## 可选的 Connector Host 入口

`Ingot.Edge.ConnectorHost` 是使用方可自行部署的现场入口与 SQLite outbox。数据源适配与 Host 均由使用方部署和运行。适配程序可提交以下请求：

```http
POST http://<host>:8001/api/v1/connector-events
Authorization: Bearer <connector-host-token>
Content-Type: application/json
```

请求体为 `ProductionEvent[]`。Host 校验本地令牌和事件，分配本地序号、持久化并向 Platform 发送标准批次。适合需要本地断网缓冲或不能直接访问 Platform 的网络边界。直接 Platform 批次和 Host 入口使用不同令牌；部署方负责选择、监控和运维其中的路径。

默认 Compose 不启动 Host。需要本地入口时启用 `connector-host` profile：

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

## 查询与事件链

- `GET /api/v1/events`：按 Edge、事件类型、对象、关联信息、`correlationId` 和时间查询；
- `GET /api/v1/events/stream`：生产事件 SSE；
- `GET /api/v1/cycles/{correlationId}`：同一生产周期号的完整事件链；服务端内部按 500 条分页读取，不以单页上限截断周期；
- `get_cycle_trace`：完整读取周期参与计算，按发生时间和中心摄入顺序生成有界时间线摘要，并提供完整周期记录链接；
- `check_data_quality`：检查周期配对、生产信息为空、序号间断和最新事件时间。

检测记录使用独立的 `InspectionRecord` 契约和 API。当前周期工具基于生产事件构建周期事件链。

## 保留生产信息项

适配器必须在能取得这些记录时写入下列 `context` 键。所有值均为字符串；未知值不要猜测，不要写入密钥或大对象。

| 用途 | 键 | 说明 |
|---|---|---|
| 阶段归属 | `recipe_id` | 配方稳定标识 |
| 阶段归属 | `recipe_version` | 配方版本；源系统无版本时可省略 |
| 阶段归属 | `recipe_template` | 中心侧阶段映射可用的配方模板 |
| 阶段归属 | `recipe_step` | 源系统观测到的步序记录 |
| 阶段归属 | `recipe_step_name` | 源系统原始步序名称 |
| 分组维度 | `product_code` | 产品或规格编码 |
| 分组维度 | `operation_code` | 工序编码 |
| 分组维度 | `mold_id` | 模具稳定标识 |
| 分组维度 | `mold_shot_count` | 当前模具累计模次 |
| 分组维度 | `preform_lot` | 预制件批次 |
| 分组维度 | `cavity_id` | 腔位或穴号 |

`recipe_step` 必须与过程数据在同一次扫描周期读取；当 step 发生变化时，适配器除继续上报过程数据外，还应额外发送一条事件，确保中心可以按 `occurred_at` 重建阶段边界。

## 周期与检测关联

`InspectionRecord.operationRunId` 默认等于该次加工运行的 `ProductionEvent.correlationId`。检测记录引用的周期必须能通过 `/api/v1/events?correlationId=<operationRunId>` 找到对应事件链路。若源系统无法使用同一标识，必须在后续版本引入显式映射；在此之前不得让检测记录和过程事件各自独立命名。

## 阶段事件命名

中心阶段含义使用 `phase.{code}.started` 与 `phase.{code}.completed` 表示，例如 `phase.anneal.started`。这些事件复用现有 `.started` / `.completed` 配对逻辑。边缘默认只上报 `recipe_step` 记录，由中心 `PhaseMapping` 解释为 `phase_code`；只有源系统原生记录就是阶段时，边缘才应照实上报 phase 事件。

## 扩展规则

- `eventType` 和 `data` 使用稳定业务名称；
- 数值单位作为明确字段随事件传递，核心不推断单位；
- 含义不兼容时提升 `eventTypeVersion` 或新增事件类型；
- `context` 只存储建立查询关联所需的稳定字符串，不写入密钥或大对象；
- 数据源协议、映射代码、缓冲与重试逻辑由使用方维护。
