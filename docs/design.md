# 设计说明

## Chat 设计

```text
问题与页面上下文
  → 类型化查询计划
  → 权限、范围与工具白名单校验
  → check_data_quality / get_cycle_trace
  → 数字与证据校验
  → 流式回答
```

Chat 的模型负责语言理解和回答组织。Central 的确定性代码负责数据查询、权限、工具执行、数字和证据验证。Chat 运行以 `surface=chat` 保存，只能通过 `/api/v1/chat/*` 访问。

## Ingot Agent 设计

```text
结构化连接器需求
  → 类型化代码计划
  → Actor 工作区工具
  → 固定 build/test
  → 有界修复
  → awaiting-package-approval
  → 人工批准
  → SHA-256 ZIP
```

桌面端通过 Tauri 2 的 Rust 网络边界调用 Agent API。服务端只接受 `X-Ingot-Client: ingot-agent-desktop`。`Ingot.Agent` 提供模型中立工作流；`Ingot.Agent.Infrastructure` 提供模型、SQLite Store 和工具适配；`Ingot.Connector.Builder` 负责工作区、固定容器入口、批准门和内容寻址打包。

模型不能选择宿主路径、Shell、镜像或工作目录。Agent 和 Builder 不连接数据源；禁网测试只读取工作区固定样本与模拟输入。构建/测试错误输出有长度上限，仅用于修复当前工作区代码。测试通过不会自动打包。

## 连接器契约

默认模板使用 stdin JSONL 输入和 stdout ProductionEvent JSONL 输出。外部部署运行时负责批处理、Connector Token、向 Connector Host 提交、进程监管和升级。Connector Host 校验事件并先写 SQLite，再通过 outbox 上报 Central。

## 隔离与恢复

- Chat 和 Agent 的创建、列表、读取、SSE、取消及历史互不可见。
- 所有运行按 Actor 鉴权；Agent 源码和包再按工作区边界隔离。
- SSE 使用单调事件序号，客户端通过 `Last-Event-ID` 恢复。
- 服务重启后，中断运行进入明确终态，不静默继续未完成写操作。
- 包下载重新计算 SHA-256；桌面端校验后才保存。

参见 [Chat](chat.md) 与 [Ingot Agent 桌面端](desktop-agent.md)。
