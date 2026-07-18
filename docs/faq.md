# 常见问题

## Ingot 是否直接连接设备？

不直接连接。使用方实现数据源适配，将设备、仪器或业务系统的数据映射为 `ProductionEvent`，并调用 `POST /api/v1/events:batch`。这样可以按现场安全、网络和运维要求选择语言与运行环境。

如需本地 SQLite outbox，使用方可部署 `Ingot.Connector.Host` 并向其提交事件；它是可选的本地入口，适配实现与运行由使用方负责。

## 数据源适配程序需要满足什么要求？

必须使用稳定的 `edgeId`、本地递增 `seq`、全局唯一 `eventId`，并让 `source` 以 `edge/{edgeId}/` 开头。应保存未确认批次，以 `ackSeq` 处理重试；不得把密钥或大对象写入 `context`。

## Chat 能做什么？

Chat 可以查询已保存的生产事实、检查数据完整性、返回周期事件链并展示证据。它不写入事件、检测记录、配置或设备，不执行任意 SQL、脚本或开放网络请求。

## 如何在生产环境启用 Chat？

默认 Compose 保持 Chat 关闭。启用时同时设置 `INGOT_CHAT_ENABLED=true`、`INGOT_CHAT_PROVIDER=OpenAI`、Fast/Reasoning 模型、`OPENAI_API_KEY`、`INGOT_CHAT_OPERATOR_TOKEN` 和 `INGOT_CHAT_OPERATOR_ALLOW_ALL`。Central Web 与 Chat API 使用 Actor `operator` 和 Chat Actor 令牌。完整配置见[配置](tutorial-configuration.md)。

## Chat 能确认根因吗？

Chat 输出已验证事实、限制条件和候选调查方向，并将相关性与因果关系明确区分。

## 如何控制数据访问范围？

为每个 Chat Actor 配置允许的 `EdgeIds`。事件接入使用与 `edgeId` 匹配的独立令牌。生产环境应轮换令牌并避免使用全局访问。

## 事件重复提交会怎样？

Central 按 `eventId` 和 `(edgeId, seq)` 识别重复。调用方仍应保持本地确认状态，避免把未确认和新事件混在同一无序批次中。

## 平台是否控制设备？

不控制。PLC、CNC、机器人、安全联锁、设备认证和现场操作始终由现有现场系统负责。

更多信息见[生产事件规范](rfc-production-events.md)、[配置](tutorial-configuration.md)和[Ingot Chat](chat.md)。
