# 部署

本指南部署 Platform Web、Platform API 和 PostgreSQL。数据源适配由使用方部署在适合现场的网络与运行环境中，并通过标准事件 API 向 Platform 提交记录。

## 部署前检查

- 为 PostgreSQL、事件接入和 Chat 准备独立机密；
- 如部署可选的 `Ingot.Edge.ConnectorHost`，为它准备独立本地入口令牌；
- 为每个 `edgeId` 创建独立事件令牌；
- 为每个 Chat 用户 配置模型、模型密钥、独立令牌和所需数据范围；
- 将 Platform API 与数据库置于受控网络，生产环境使用 TLS；
- 确认使用方适配程序不会把设备凭据、原始大对象或敏感文本写入事件关联信息。

## Docker Compose

```bash
export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"

docker compose -f docker-compose.app.yml up -d --build
docker compose -f docker-compose.app.yml ps
```

检查服务：

```bash
curl http://localhost:8000/health
```

默认 Compose 只启动 PostgreSQL、Platform API 与 Platform Web，且保持 Chat 关闭。

## 启用生产 Chat

生产 Chat 需要完整配置后才能启用：

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true

docker compose -f docker-compose.app.yml up -d --build

curl http://localhost:8000/api/v1/chat/capabilities \
  -H "X-Ingot-User: operator" \
  -H "Authorization: Bearer ${INGOT_CHAT_OPERATOR_TOKEN}"
```

该 Compose 配置为 `operator` 启用完整数据范围。生产部署应按角色和网络边界配置实际所需的数据范围，并将所有机密注入 Secret Store 或受保护环境变量。

## 数据源接入

数据源适配程序自行负责：

1. 读取设备、仪器或业务系统；
2. 映射稳定的事件类型、对象、关联信息和单位；
3. 保存本地序号与未确认批次；
4. 以 `POST /api/v1/events:batch` 和 Edge 令牌提交事件；
5. 按 `ackSeq` 重试未确认记录；
6. 监控认证失败、延迟和重复率。

`Ingot.Edge.ConnectorHost` 是可选路径：使用方可在现场部署它以获得本地 SQLite outbox，并向其提交 `ProductionEvent[]`；Host 以至少传递一次向 Platform 上报。默认 Compose 不启动 Host。需要该路径时：

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

直接 Platform 批次与 Host 入口使用不同令牌，部署方应按网络边界选择、监控和运维其中的路径。

Platform 不要求特定语言、进程模型或现场操作系统。完整字段与重试含义见[生产事件规范](rfc-production-events.md)。

## 备份与升级

- 按组织恢复目标备份 PostgreSQL；
- 升级前运行 `./scripts/verify.sh` 并验证 Compose 配置；
- 先在隔离环境用真实但脱敏的事件批次验证兼容性；
- 事件类型含义不兼容时提升 `eventTypeVersion` 或新增事件类型；
- 模型或提示版本升级前应通过 Chat 评测，并保留运行、工具和相关记录审计记录。

## 运行边界

Ingot 提供生产记录存储、查询和对话。设备协议、现场安全、网络隔离、凭据轮换、适配程序可用性和设备控制由部署方及现场系统负责。
