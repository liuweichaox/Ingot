<a id="readme-top"></a>

<div align="center">
  <a href="https://ingotstack.com">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="images/logo/ingot-lockup-dark.svg">
      <img src="images/logo/ingot-lockup.svg" alt="Ingot" width="360">
    </picture>
  </a>

  <h3>可信生产事实与工艺调查平台</h3>

  <p>
    汇集生产记录，形成可追溯事实；用 Ingot Chat 提问、查看证据，并在需要时深入调查。
  </p>

  <p>
    <a href="https://ingotstack.com"><strong>访问官网</strong></a>
    ·
    <a href="https://docs.ingotstack.com"><strong>浏览文档</strong></a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/issues">报告问题</a>
  </p>

  <p>
    <a href="https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml"><img src="https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
    <a href="LICENSE"><img src="https://img.shields.io/github/license/liuweichaox/Ingot.svg?style=flat&amp;logo=github" alt="MIT License"></a>
    <a href="https://github.com/liuweichaox/Ingot/issues"><img src="https://img.shields.io/github/issues/liuweichaox/Ingot.svg?style=flat&amp;logo=github" alt="GitHub Issues"></a>
  </p>

  <p><a href="README.en.md">English</a> · 简体中文</p>
</div>

<details>
  <summary>目录</summary>
  <ol>
    <li><a href="#关于-ingot">关于 Ingot</a></li>
    <li><a href="#核心能力">核心能力</a></li>
    <li><a href="#架构">架构</a></li>
    <li><a href="#快速开始">快速开始</a></li>
    <li><a href="#事件接入">事件接入</a></li>
    <li><a href="#chat">Chat</a></li>
    <li><a href="#公共接口">公共接口</a></li>
    <li><a href="#项目结构">项目结构</a></li>
    <li><a href="#文档">文档</a></li>
    <li><a href="#参与贡献">参与贡献</a></li>
    <li><a href="#许可证">许可证</a></li>
  </ol>
</details>

## 关于 Ingot

Ingot 是制造现场的可信生产事实与工艺调查平台。它将设备、仪器、MES、ERP 或自定义系统的关键记录汇集为可查询、可验证、可追溯的生产事实。使用方自行将数据映射为标准 `ProductionEvent` 或 `InspectionRecord` 并调用公开 HTTP API；Ingot 不内置设备协议，也不替代现场控制、排产、库存或质量处置系统。

**Ingot Chat** 是工程师使用 Ingot 的主要入口。它运行在 Central Web 中，只读取已有生产事实：日常模式用于查事实和数据完整性；深入调查模式让工艺、质量与反证三个角色审查同一批已验证证据，给出待工程师确认的候选解释。

## 核心能力

| 能力 | 已实现内容 |
|---|---|
| 标准事件接入 | 使用方通过 `POST /api/v1/events:batch` 提交带来源、对象、上下文、关联 ID 与序号的 `ProductionEvent` 批次 |
| 检测事实 | 人工或仪器客户端通过 `POST /api/v1/inspection-records` 提交独立检测记录 |
| 可信事实库 | 生产事件、检测记录、对象、上下文、关联 ID、查询和 SSE |
| Ingot Chat | 自然语言问题、页面上下文、流式回答、历史记录、限制条件和证据回链 |
| Chat 事实工具 | `check_data_quality` 与 `get_cycle_trace` |
| 深入调查 | 工艺、质量与反证角色有界协作；最多 3 轮、9 次发言，只产生带证据的候选解释 |
| 运维能力 | 健康检查、Prometheus 指标和结构化日志 |

### 安全边界

- Chat 只调用显式注册的只读事实工具；不执行 SQL、脚本、Shell、文件写入或开放网络请求。
- 回答中的数字和关键结论必须来自工具结果并携带证据引用；数据不足时明确说明限制。
- 标准事件 API 只接受经过契约和令牌校验的批次；数据源协议、凭据、重试策略与现场运行方式由使用方负责。
- Ingot 不写入 PLC、CNC、机器人或其他现场控制器。
- 密钥只通过环境变量或 Secret Store 注入，不写入源码、日志或仓库配置。

## 架构

```text
设备、仪器、MES、ERP 或自定义系统
  └─ 使用方实现数据适配
       └─ ProductionEvent[] / InspectionRecord
            └─ Central API ──► PostgreSQL 生产事实
                                    ├─ 查询与 SSE
                                    └─ Central Web · Ingot Chat
```

使用方拥有数据源协议与运行方式；Ingot 拥有统一事实契约、访问控制、查询和证据回链。详细说明见[宏观架构](docs/architecture.md)与[生产事件规范](docs/rfc-production-events.md)。

<p align="right"><a href="#readme-top">返回顶部</a></p>

## 快速开始

### 环境要求

- .NET SDK 10
- Node.js 22.13 或更高版本
- Docker Engine 26 或更高版本，以及 Docker Compose
- OpenSSL，用于生成本地凭据

### 启动 Central

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot

export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"

docker compose -f docker-compose.app.yml up -d --build
```

| 服务 | 地址 |
|---|---|
| Central Web | <http://localhost:3000> |
| Central API | <http://localhost:8000> |
| Central 健康检查 | <http://localhost:8000/health> |

默认 Compose 启动 PostgreSQL、Central API 与 Central Web；Chat 与 Connector Host 均按需启用。`INGOT_OPERATOR_TOKEN` 用于检测事实提交，Chat 使用独立的 `INGOT_CHAT_OPERATOR_TOKEN`。

### 启用生产 Chat

生产 Compose 默认关闭 Chat。启用前请一次性提供模型、模型密钥、Chat Actor 令牌和数据范围：

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true
export INGOT_CHAT_ENABLE_DEEP_INVESTIGATION=true

docker compose -f docker-compose.app.yml up -d --build
```

浏览器和 Chat HTTP API 使用 Actor `operator` 与 `INGOT_CHAT_OPERATOR_TOKEN`。生产环境应为每个 Actor 配置所需的数据范围；上述 Compose 示例启用 `operator` 的全部事实范围。

完整步骤见[快速开始](docs/tutorial-getting-started.md)和[配置](docs/tutorial-configuration.md)。

## 事件接入

为每类数据源实现一个符合自身运行环境的适配器。适配器将源数据映射为 `ProductionEvent`，持久化本地序号并使用 Edge 凭据提交至 Central。每个事件的 `source` 必须以 `edge/{edgeId}/` 开头。

```bash
curl -X POST http://localhost:8000/api/v1/events:batch \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${INGOT_EDGE_TOKEN}" \
  -d '{
    "edgeId": "EDGE-001",
    "events": [{
      "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
      "eventType": "cycle.started",
      "eventTypeVersion": 1,
      "occurredAt": "2026-07-18T08:00:00Z",
      "recordedAt": "2026-07-18T08:00:00Z",
      "source": "edge/EDGE-001/furnace/FURNACE-01",
      "subject": { "type": "asset", "id": "FURNACE-01" },
      "context": { "workpiece_id": "WP-001" },
      "data": {},
      "correlationId": "CYCLE-001",
      "seq": 1
    }]
  }'
```

每批支持 1 至 500 条事件。Central 以 `eventId` 和 `(edgeId, seq)` 去重，并返回 `ackSeq`。契约、校验规则和重试建议见[生产事件规范](docs/rfc-production-events.md)。

当适配程序位于现场网络、需要本地 SQLite outbox 或无法直连 Central 时，使用方可启用 **Ingot.Connector.Host** 作为可选的本地事件入口。它接收 `ProductionEvent[]`、持久化并以至少一次语义上报 Central；该路径由使用方选择、部署和运维。

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

见[生产事件规范](docs/rfc-production-events.md)。

## Chat

完成生产 Chat 配置后，打开 <http://localhost:3000>，进入 **Chat**，输入问题并可附带资产或周期上下文：

```text
这个周期发生了什么，数据是否完整？
```

Chat 使用受控的只读事实工具，返回结论、限制条件与原始事实引用。它不修改事件、检测记录、配置或设备状态。

完整能力与 API 见[Chat](docs/chat.md)。

## 公共接口

| 接口 | 用途 |
|---|---|
| `POST /api/v1/events:batch` | 提交标准生产事件批次 |
| `GET /api/v1/events` | 查询生产事件 |
| `GET /api/v1/events/stream` | 通过 SSE 订阅生产事件 |
| `GET /api/v1/cycles/{correlationId}` | 查询一个关联 ID 的事件时间线 |
| `POST /api/v1/inspection-records` | 提交人工或仪器检测记录 |
| `GET /api/v1/chat/capabilities` | 查询 Chat 是否启用、可用工具和运行上限 |
| `POST /api/v1/chat/runs` | 创建 Chat 对话运行 |
| `GET /api/v1/chat/runs/{runId}` | 查询回答、计划、工具活动和证据 |
| `GET /api/v1/chat/runs/{runId}/stream` | 通过 SSE 接收 Chat 事件并支持续读 |
| `POST /api/v1/chat/runs/{runId}:cancel` | 取消 Chat 运行 |

## 项目结构

```text
Ingot/
├── src/
│   ├── Ingot.Central.Api/            Central HTTP、鉴权与 SSE
│   ├── Ingot.Central.Infrastructure/ 中心事实、检测、Webhook 与 Chat 事实工具
│   ├── Ingot.Contracts/              事件、检测与 Chat HTTP 契约
│   ├── Ingot.Domain/                 生产事件、对象引用和领域校验
│   ├── Ingot.Application/            应用服务抽象
│   └── Ingot.Infrastructure/         存储、日志、指标与运行时实现
├── site/                             官网
├── docs/                             中英文 Markdown 文档
├── docs-site/                        静态文档站
└── tests/                            自动化测试
```

## 文档

- [文档首页](docs/index.md)
- [快速开始](docs/tutorial-getting-started.md)
- [事件接入](docs/rfc-production-events.md)
- [Chat](docs/chat.md)
- [部署](docs/tutorial-deployment.md)
- [配置](docs/tutorial-configuration.md)
- [架构](docs/architecture.md)
- [常见问题](docs/faq.md)

## 参与贡献

欢迎提交 Issue、讨论和 Pull Request。请先阅读[贡献指南](CONTRIBUTING.md)与[安全策略](SECURITY.md)。

## 许可证

基于 [MIT License](LICENSE) 发布。

<p align="right"><a href="#readme-top">返回顶部</a></p>

