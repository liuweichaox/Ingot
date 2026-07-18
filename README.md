<a id="readme-top"></a>

<div align="center">
  <a href="https://ingotstack.com">
    <picture>
      <source media="(prefers-color-scheme: dark)" srcset="images/logo/ingot-lockup-dark.svg">
      <img src="images/logo/ingot-lockup.svg" alt="Ingot" width="360">
    </picture>
  </a>

  <h3>可信生产事实与连接器工程平台</h3>

  <p>
    Central Web 通过 Chat 查询事实、查找数据问题；<br>
    Ingot Agent 桌面端生成、构建、测试并打包数据连接器代码。
  </p>

  <p>
    <a href="https://ingotstack.com"><strong>访问官网</strong></a>
    ·
    <a href="https://docs.ingotstack.com"><strong>浏览文档</strong></a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/releases/latest"><strong>下载 Ingot Agent</strong></a>
    ·
    <a href="https://github.com/liuweichaox/Ingot/issues">报告问题</a>
  </p>

  <p>
    <a href="https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml"><img src="https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
    <a href="LICENSE"><img src="https://img.shields.io/github/license/liuweichaox/Ingot" alt="MIT License"></a>
    <a href="https://github.com/liuweichaox/Ingot/issues"><img src="https://img.shields.io/github/issues/liuweichaox/Ingot" alt="Issues"></a>
  </p>

  <p><a href="README.en.md">English</a> · 简体中文</p>
</div>

<details>
  <summary>目录</summary>
  <ol>
    <li>
      <a href="#关于-ingot">关于 Ingot</a>
      <ul>
        <li><a href="#chat-与-agent">Chat 与 Agent</a></li>
        <li><a href="#核心能力">核心能力</a></li>
        <li><a href="#安全边界">安全边界</a></li>
        <li><a href="#技术栈">技术栈</a></li>
      </ul>
    </li>
    <li><a href="#架构">架构</a></li>
    <li><a href="#开始使用">开始使用</a></li>
    <li><a href="#使用方式">使用方式</a></li>
    <li><a href="#公共接口">公共接口</a></li>
    <li><a href="#项目结构">项目结构</a></li>
    <li><a href="#文档">文档</a></li>
    <li><a href="#参与贡献">参与贡献</a></li>
    <li><a href="#许可证">许可证</a></li>
    <li><a href="#致谢">致谢</a></li>
  </ol>
</details>

## 关于 Ingot

Ingot 面向需要统一接入设备、仪器和业务系统的制造现场。外部连接器把不同来源转换为统一的 `ProductionEvent`，Connector Host 负责认证、契约校验、本地持久化和有界上报，Central 保存可查询、可验证、可追溯的生产事实。

### Chat 与 Agent

| 产品 | 运行位置 | 负责的事情 |
|---|---|---|
| **Chat** | Central Web | 通过对话查询生产事实、检查数据质量、下钻周期事件链，并返回证据引用 |
| **Ingot Agent** | 下载并安装的桌面软件 | 根据数据源规格生成连接器源码，在受限环境中构建、测试和修复，经人工确认后打包 |

Chat 不生成代码，也不修改生产事实。Ingot Agent 不承担生产分析对话，不部署连接器，不控制设备。两者名称、入口、权限和运行环境相互独立。

### 核心能力

| 能力 | 已实现内容 |
|---|---|
| Central Web Chat | 自然语言问题、页面上下文、只读工具活动、流式回答、历史记录和证据回链 |
| Chat 事实工具 | `check_data_quality`、`get_cycle_trace` |
| Ingot Agent 桌面端 | 连接器规格补全、Actor 隔离源码工作区、代码生成与修复、固定构建/测试入口、人工打包批准、SHA-256 ZIP |
| 标准事件入口 | `Ingot.Connector.Host` 接收 `ProductionEvent[]`，不加载设备协议 SDK |
| 事实平台 | 生产事件、检测记录、对象、上下文、关联 ID、查询与 SSE |
| 有界上报 | SQLite WAL 现场事件日志与 outbox、PostgreSQL 中心事实库、积压上限和丢弃审计 |
| 运维观测 | 健康检查、Prometheus 指标和结构化日志 |

### 安全边界

- Chat 只调用显式注册的只读事实工具，不执行 SQL、脚本、Shell、文件写入或开放网络请求。
- Chat 的数字和结论必须来自工具结果并携带可解析的证据引用；数据不足时返回限制条件。
- Ingot Agent 只能修改当前 Actor 的连接器工作区，不能选择任意宿主路径、命令、镜像或工作目录。
- Agent 不连接数据源；连接器构建和测试只在禁网环境运行平台固定入口，并使用工作区内测试样本。测试通过后仍需人工批准才能生成 SHA-256 ZIP。
- Ingot 不部署、启动或调度生成的连接器，也不控制 PLC、CNC、机器人或其他现场控制器。
- 密钥仅通过环境变量或 Secret Store 注入，不写入源码、日志、制品或仓库配置。

### 技术栈

- [.NET 10](https://dotnet.microsoft.com/) 与 ASP.NET Core
- Microsoft Agent Framework 与 OpenAI Responses API，在 Central 服务端执行桌面 Agent 发起的连接器代码生成流程
- PostgreSQL 18 与 SQLite WAL
- Vue 3、Vite 与 Element Plus
- Next.js 16 与 React 19
- Docker Compose、Prometheus 与 Serilog

<p align="right"><a href="#readme-top">返回顶部</a></p>

## 架构

```text
Central Web
  ├─ Chat ──只读查询──► Central API ──► PostgreSQL 生产事实
  └─ 事件、检测、日志与指标界面

Ingot Agent 桌面端
  └─ 连接器规格 → 隔离源码工作区 → 固定构建/测试 → 人工批准 → SHA-256 ZIP

外部部署的连接器
  └─ source payload → ProductionEvent[] → Ingot.Connector.Host
                                           └─ SQLite event log/outbox → Central API
```

依赖边界由 [`scripts/verify-architecture.sh`](scripts/verify-architecture.sh) 校验。详细设计见[宏观架构](docs/architecture.md)与[设计说明](docs/design.md)。

<p align="right"><a href="#readme-top">返回顶部</a></p>

## 开始使用

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
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"

docker compose -f docker-compose.app.yml up -d --build
```

| 服务 | 地址 |
|---|---|
| Central Web | <http://localhost:3000> |
| Central API | <http://localhost:8000> |
| Connector Host | <http://localhost:8001> |
| Central 健康检查 | <http://localhost:8000/health> |
| Connector Host 健康检查 | <http://localhost:8001/health> |

### 下载 Ingot Agent

从 [GitHub Releases 最新版本](https://github.com/liuweichaox/Ingot/releases/latest) 下载并安装与操作系统匹配的 Ingot Agent。桌面端只配置 Central URL、Actor 和 Token；模型、工作区与构建环境由 Central 部署。Central Web 不提供 Agent 代码生成入口。

完整步骤见[快速开始](docs/tutorial-getting-started.md)、[Chat](docs/chat.md)与[Ingot Agent 桌面端](docs/desktop-agent.md)。

<p align="right"><a href="#readme-top">返回顶部</a></p>

## 使用方式

### 在 Central Web 使用 Chat

打开 <http://localhost:3000>，进入 **Chat**，输入问题并可附带资产或周期上下文，例如：

```text
这个周期发生了什么，数据是否完整？
```

Chat 会生成受控查询计划，调用只读工具，并返回结论、限制条件和原始事实引用。Chat 不生成连接器代码。

### 在 Ingot Agent 生成连接器

```text
数据源规格与样本
  → 连接器代码
  → 固定构建入口
  → 固定测试入口
  → 失败修复
  → 人工批准
  → SHA-256 ZIP
```

默认 .NET 连接器模板从 stdin 读取源 JSONL，并向 stdout 输出 ProductionEvent JSONL。包内不包含 Connector Host 凭据、重试队列或 HTTP 提交客户端；外部部署运行时负责批处理、鉴权和向 Connector Host 提交事件。

### 提交标准生产事件

```bash
curl -X POST http://localhost:8001/api/v1/connector-events \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${INGOT_CONNECTOR_TOKEN}" \
  -d '[{
    "eventId": "0198a820-7f91-7a5d-8c44-111111111111",
    "eventType": "cycle.started",
    "eventTypeVersion": 1,
    "occurredAt": "2026-07-18T08:00:00Z",
    "recordedAt": "2026-07-18T08:00:00Z",
    "source": "connector/FURNACE-01",
    "subject": { "type": "asset", "id": "FURNACE-01" },
    "context": { "workpiece_id": "WP-001" },
    "data": {},
    "correlationId": "CYCLE-001",
    "seq": 0
  }]'
```

事件契约见[生产事件规范](docs/rfc-production-events.md)。

### 执行完整门禁

```bash
./scripts/verify.sh
```

<p align="right"><a href="#readme-top">返回顶部</a></p>

## 公共接口

| 接口 | 用途 |
|---|---|
| `GET /api/v1/chat/capabilities` | 查询 Chat 是否启用、只读工具与运行上限 |
| `POST /api/v1/chat/runs` | 创建 Chat 对话运行 |
| `GET /api/v1/chat/runs` | 查询当前 Actor 的 Chat 历史 |
| `GET /api/v1/chat/runs/{runId}` | 查询状态、计划、工具活动、证据和回答 |
| `GET /api/v1/chat/runs/{runId}/stream` | 通过 SSE 接收运行事件并支持续读 |
| `POST /api/v1/chat/runs/{runId}:cancel` | 取消 Chat 运行 |
| `POST /api/v1/connector-events` | Connector Host 标准事件入口 |
| `POST /api/v1/events:batch` | Connector Host 到 Central 的批量事件入口 |
| `POST /api/v1/inspection-records` | 人工或仪器检测记录入口 |

Ingot Agent 桌面端的代码生成、工作区和打包接口属于桌面应用内部边界，不作为 Central Web Chat 能力公开。完整契约见 [Chat](docs/chat.md) 与 [Ingot Agent 桌面端](docs/desktop-agent.md)。

## 项目结构

```text
Ingot/
├── desktop/                           Ingot Agent Desktop；Tauri、Rust、React 和 TypeScript
├── src/
│   ├── Ingot.Agent/                  Chat/Agent 产品面、模型中立工作流与验证
│   ├── Ingot.Agent.Infrastructure/   服务端模型适配、运行存储和工具实现
│   ├── Ingot.Connector.Builder/      隔离工作区、固定构建/测试与打包
│   ├── Ingot.Connector.Host/         标准事件接入、SQLite 日志与 outbox
│   ├── Ingot.Central.Infrastructure/ 中心事实、检测、Webhook 与只读 Chat 工具
│   ├── Ingot.Central.Api/            Central HTTP、鉴权与 SSE
│   ├── Ingot.Central.Web/            Chat 与生产事实操作界面
│   ├── Ingot.Contracts/              公共 HTTP、事件和检测契约
│   ├── Ingot.Domain/                 领域模型与生产事件校验
│   └── Ingot.Infrastructure/         事件、上下文、日志与指标基础设施
├── tests/                             .NET 自动化测试
├── docs/                              中英文源文档
├── docs-site/                         docs.ingotstack.com
├── site/                              ingotstack.com
└── scripts/                           架构与发布门禁
```

## 文档

- [文档首页](docs/index.md)
- [快速开始](docs/tutorial-getting-started.md)
- [Chat](docs/chat.md)
- [Ingot Agent 桌面端](docs/desktop-agent.md)
- [宏观架构](docs/architecture.md)
- [设计说明](docs/design.md)
- [模块说明](docs/modules.md)
- [配置说明](docs/tutorial-configuration.md)
- [部署指南](docs/tutorial-deployment.md)
- [开发指南](docs/tutorial-development.md)
- [常见问题](docs/faq.md)
- [生产事件规范](docs/rfc-production-events.md)
- [安全策略](SECURITY.md)

<p align="right"><a href="#readme-top">返回顶部</a></p>

## 参与贡献

1. Fork 项目。
2. 创建分支：`git checkout -b feature/your-change`。
3. 完成代码、测试和中英文文档。
4. 运行 `./scripts/verify.sh`。
5. 提交变更并发起 Pull Request。

所有贡献必须保持 Chat 只读、Agent 桌面代码生成、事实契约和中英文文档一致。完整要求见[贡献指南](CONTRIBUTING.md)。安全问题请按[安全策略](SECURITY.md)私下报告。

<p align="right"><a href="#readme-top">返回顶部</a></p>

## 许可证

项目采用 MIT License，详见 [LICENSE](LICENSE)。

## 致谢

- README 信息架构参考 [othneildrew/Best-README-Template](https://github.com/othneildrew/Best-README-Template)。
- 桌面端代码生成运行时采用 Microsoft Agent Framework。

项目地址：<https://github.com/liuweichaox/Ingot>

<p align="right"><a href="#readme-top">返回顶部</a></p>
