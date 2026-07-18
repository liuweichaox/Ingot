# 宏观架构

Ingot 包含三个相互独立的产品面：Central Web、Ingot Agent 桌面端和 Connector Host。Central Web 展示生产事实并提供 Chat；桌面 Agent 只进行连接器代码工程；Connector Host 只接收标准事件。

```text
Central Web
  ├─ 事实页面 → Central API → PostgreSQL
  └─ Chat → 只读工具 → Central fact services

Ingot Agent Desktop
  → Agent API
  → Actor 隔离工作区
  → 固定容器 build/test
  → 人工批准
  → SHA-256 ZIP

外部连接器
  → Ingot.Connector.Host
  → SQLite event log/outbox
  → Central API
```

## 产品边界

- Chat 运行在 Central Web，只查询事实、检查数据质量并返回证据。
- Ingot Agent 运行在 Tauri 桌面应用，只生成、构建、测试、修复和打包连接器代码。
- `/api/v1/chat/*` 与 `/api/v1/agent/*` 使用不同产品标识、权限和运行历史。
- Agent API 要求 `X-Ingot-Client: ingot-agent-desktop`；浏览器不能进入代码生成工作流。
- Connector Host 不包含设备协议或寄存器语义，只接收 `ProductionEvent[]`。
- 生成的连接器由外部运行时部署、启动和监管。
- 实时控制、安全联锁和设备写操作不属于 Ingot。

## 存储

- PostgreSQL：中心生产事件、检测记录和查询事实。
- SQLite Agent Store：Chat/Agent 运行、SSE 事件和制品元数据，按产品面和 Actor 隔离。
- 受控目录：桌面 Agent 的 Actor 工作区、打包批准元数据与内容寻址 ZIP。
- Connector Host SQLite：现场事件日志、上下文和有界 outbox。

## 网络

Central、Connector Host 和 PostgreSQL 在默认 Compose 中绑定主机回环地址。Chat 仅通过 Central 事实服务读取数据。Agent 模型调用由配置的模型 API 承担；固定构建/测试子容器始终禁网。外部连接器提交事件时使用独立 Connector Token。

详细工作流见 [Chat](chat.md)、[Ingot Agent 桌面端](desktop-agent.md)与[部署](tutorial-deployment.md)。
