# 设计说明

## Ingot Chat 设计

```text
问题与页面上下文
  → 类型化查询计划
  → 权限、范围与工具白名单校验
  → check_data_quality / get_cycle_trace
  → 数字与证据校验
  → 流式回答
```

Chat 的模型负责语言理解和回答组织。Central 的确定性代码负责数据查询、权限、工具执行、数字和证据验证。Chat 运行按 Actor 隔离，只能通过 `/api/v1/chat/*` 访问。

## 事件接入设计

使用方将任意来源映射为 `ProductionEvent` 批次，并调用 `/api/v1/events:batch`。Central 负责以下确定性工作：

1. 校验 Edge 令牌、`edgeId`、批次大小和事件字段；
2. 校验 `source` 前缀、事件 ID 与序号唯一性；
3. 以 `eventId` 和 `(edgeId, seq)` 处理重复提交；
4. 保存中心事实并返回 `ackSeq`；
5. 提供查询、SSE 和按 `correlationId` 的周期事实链。

数据源协议、现场缓存、重试时机和进程监管由使用方决定。标准契约避免把这些实现细节耦合到 Central。

## 隔离与恢复

- Chat 的创建、列表、读取、SSE 和取消均按 Actor 鉴权；
- SSE 使用单调事件序号，客户端通过 `Last-Event-ID` 恢复；
- 服务重启后，中断的 Chat 运行进入明确终态；
- 事件提交按批校验，调用方应保存已提交序号并基于 `ackSeq` 处理重试；
- Chat 不持有数据源凭据，也不调用现场网络或设备接口。

参见 [Ingot Chat](chat.md) 与[生产事件规范](rfc-production-events.md)。
