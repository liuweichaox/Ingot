# 部署

## Central 与 Connector Host

仓库提供单机 Docker Compose 基线：

```bash
export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker pull mcr.microsoft.com/dotnet/sdk:10.0
docker compose -f docker-compose.app.yml up -d --build
```

| 服务 | 职责 |
|---|---|
| `central-web` | 生产事实页面与 Chat |
| `central-api` | Chat、桌面 Agent、事实、检测、Webhook 和 SSE API |
| `connector-host` | 协议无关的标准事件入口与现场 outbox |
| `postgres` | 中心生产事实 |

默认端口只绑定主机回环地址。公网部署必须使用独立认证网关、TLS、限流和明确的入口白名单。

## Chat 部署

Chat 运行在 Central API，Central Web 只提供界面。生产环境配置 `Chat` Provider、模型、Actor Token 和数据范围。Chat 只访问中心事实服务，不获得数据库凭据、文件系统或设备网络权限。

## Ingot Agent 部署

Ingot Agent Desktop 由用户从 [GitHub Releases](https://github.com/liuweichaox/Ingot/releases/latest) 下载。桌面端配置 Central URL、Actor 和 Token，通过 Rust 原生边界请求桌面专用 Agent API。

Agent 模型、SQLite 运行存储、源码工作区、Builder 和包目录部署在 Central 侧：

- `Data/agent.db`：Chat/Agent 运行、SSE 事件与制品元数据；
- `Data/connector-workspaces`：Actor 隔离源码、命令结果和打包批准元数据；
- `Data/connector-packages`：经测试和批准生成的内容寻址 ZIP。

Central API 使用 Docker socket 启动受限构建子容器。Docker socket 是高权限运维边界，只能暴露给受信 Central API；正式环境应使用专用主机、rootless daemon 或等价受控构建服务。模型无法访问 socket 或提供 Docker 参数。

固定 build/test 子容器使用 `--network none`、`--pull never`、只读根文件系统、资源上限、capability 移除和 `no-new-privileges`。启动前预取并审核 SDK 镜像，生产可将 `INGOT_CONNECTOR_BUILDER_IMAGE` 固定为 digest。

## Connector Host 数据

- `Data/connector-host/events.db`：现场事件与 outbox；
- `Data/connector-host/context.db`：业务上下文；
- `ingot-postgres-data`：中心事实 PostgreSQL 卷。

Connector Host outbox 默认上限为 500,000 条。达到 `Events:MaxBacklogRows` 时删除最旧未上报事件，并产生 `diagnostic.backlog_dropped` 和 `event_backlog_dropped_total`。生产监控必须覆盖两者。

## 外部连接器

生成包采用 stdin JSONL → stdout ProductionEvent JSONL 契约，不包含生产凭据、HTTP 提交客户端或进程监管。外部部署环境负责：

- 运行和重启连接器；
- 提供数据源及 Connector Host 凭据；
- 组成批次并调用 `POST /api/v1/connector-events`；
- 处理日志、升级、回滚与网络策略。

Ingot 不部署、启动或调度连接器，不控制设备。

## 发布门禁

源码、运行记录、批准元数据、包、SQLite 文件和 PostgreSQL 卷必须纳入保留、备份与恢复策略。发布前执行：

```bash
./scripts/verify.sh
```

官网和文档站通过 `deploy/compose.yml` 与 `deploy/deploy.sh` 独立部署，见 [`deploy/README.md`](../deploy/README.md)。
