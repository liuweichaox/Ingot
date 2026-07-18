# Ingot Agent 桌面端

Ingot Agent 是下载安装的桌面软件，只用于生成连接器代码。它根据数据源规格编写真实源码，在禁网环境执行受控构建和样本测试，根据错误修复代码，并在人工批准后生成可下载的 SHA-256 ZIP。Agent 不连接真实数据源。

Ingot Agent 不提供生产事实问答；生产查询与问题定位由 Central Web 的 [Chat](chat.md) 完成。

## 下载与技术栈

- 下载：[GitHub Releases 最新版本](https://github.com/liuweichaox/Ingot/releases/latest)
- 桌面框架：Tauri 2
- 原生边界：Rust
- 界面：React 19、TypeScript、Vite
- 服务端模型运行：Central 中的 Microsoft Agent Framework 与 OpenAI Responses API
- 受控构建：平台固定 Docker 构建和测试入口

本地开发：

```bash
cd desktop
npm install
npm run desktop:dev
```

生成发布包：

```bash
cd desktop
npm run icons
npm run desktop:build
```

## 输入

创建任务前填写：

- 数据源类型与协议；
- 端点和认证方式说明；
- 输入字段、类型、单位与示例；
- 采样或触发策略；
- 期望的 `ProductionEvent` 类型、对象、上下文和关联规则；
- 构建与测试验收条件。

生产凭据不应写入提示、源码或打包制品。运行时凭据由外部部署环境提供。

## 工作流

```text
结构化连接器需求
  → 创建桌面 Agent 运行
  → Actor 隔离源码工作区
  → 生成连接器源码与测试
  → 固定容器构建
  → 固定容器测试
  → 根据受限错误输出修复
  → 人工审查源码和结果
  → 授权 Actor 批准打包
  → 生成并校验 SHA-256 ZIP
```

桌面端通过 SSE 接收运行事件；连接中断时使用最后事件序号续读，并通过快照轮询恢复状态。源码查看为只读，模型只能通过受控工作区工具修改文件。

## Agent API 边界

桌面端使用 `/api/v1/agent/*`，并在请求中携带：

```text
X-Ingot-Client: ingot-agent-desktop
X-Ingot-Actor: <actor>
Authorization: Bearer <actor-token>
```

Agent API 仅支持 `standard` 连接器代码生成用途。运行创建、历史、读取、SSE 和取消与 Chat 严格隔离；Chat Token 或 Chat 运行不能进入 Agent 工作流。

主要接口：

| 接口 | 用途 |
|---|---|
| `GET /api/v1/agent/capabilities` | 验证桌面端、代码生成能力与运行上限 |
| `POST /api/v1/agent/runs` | 创建连接器代码生成运行 |
| `GET /api/v1/agent/runs` | 查询当前 Actor 的 Agent 历史 |
| `GET /api/v1/agent/runs/{runId}` | 查询运行、制品和工作区状态 |
| `GET /api/v1/agent/runs/{runId}/stream` | 订阅并续读 SSE 事件 |
| `POST /api/v1/agent/runs/{runId}:cancel` | 取消运行 |
| `GET /api/v1/connector-workspaces/{id}` | 查询源码与构建/测试状态 |
| `GET /api/v1/connector-workspaces/{id}/files` | 列出可审查源码文件 |
| `GET /api/v1/connector-workspaces/{id}/file?path=` | 读取工作区文本文件 |
| `POST /api/v1/connector-workspaces/{id}:approve-package` | 授权 Actor 批准打包 |
| `POST /api/v1/connector-workspaces/{id}:package` | 生成 SHA-256 ZIP |
| `GET /api/v1/connector-workspaces/{id}/package` | 下载 ZIP 并校验 SHA-256 `ETag` |

## 连接器运行契约

默认 .NET 模板的 `connector.manifest.json` 声明：

```text
input:  stdin/json-lines
output: stdout/production-event-json-lines
```

连接器逐行读取源 JSON，并逐行输出标准 ProductionEvent JSON。生成包不包含 Connector Host Token、HTTP 提交客户端、重试队列或生产部署配置。外部部署运行时负责：

1. 向连接器 stdin 提供源 JSONL；
2. 读取并校验 stdout ProductionEvent JSONL；
3. 组成 `ProductionEvent[]` 批次；
4. 使用独立 Connector Token 调用 `POST /api/v1/connector-events`；
5. 管理进程、凭据、重试、日志和升级。

## 安全边界

- 桌面端是唯一 Agent 用户入口；Central Web 不展示代码生成界面；
- Agent 仅处理连接器规格、源码、构建、测试、修复和打包；
- Agent 和 Builder 不连接数据源；测试只使用工作区内的固定样本与模拟输入；
- 模型不能提交任意 Shell、选择容器镜像、改变工作目录或访问宿主任意路径；
- 工作区按 Actor 隔离，并拒绝绝对路径、路径穿越、内部元数据目录和超限内容；
- 固定构建/测试子容器禁网，启用只读根文件系统、资源上限、capability 移除和 `no-new-privileges`；
- 测试通过后状态为 `awaiting-package-approval`，没有授权 Actor 的显式批准不能打包；
- 下载时校验服务端 SHA-256，内容不一致时拒绝保存；
- Agent 不部署连接器，不修改生产事实，不写检测结果，也不控制设备。

部署和构建环境见[部署](tutorial-deployment.md)，扩展连接器模板见[开发指南](tutorial-development.md)。
