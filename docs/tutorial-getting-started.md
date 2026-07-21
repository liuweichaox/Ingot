# 快速开始

本教程启动 Platform、提交一批标准生产事件，并用 Ingot Chat 查询周期数据。数据源适配由使用方实现；只需将结果映射为公开事件契约。

## 1. 准备环境

- .NET SDK 10
- Node.js 22.13 或更高版本
- Docker Engine 26 或更高版本，以及 Docker Compose
- OpenSSL

## 2. 启动 Platform

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot

export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"

docker compose -f docker-compose.app.yml up -d --build
```

等待健康检查通过：

```bash
curl http://localhost:8000/health
```

打开 <http://localhost:3000> 访问 Platform Web。

默认 Compose 启动 PostgreSQL、Platform API 和 Platform Web。若适配程序能直连 Platform，直接使用本教程的批次 API。

### 可选：启用 Connector Host

现场网络需要本地 SQLite outbox 时，可启用 Connector Host profile：

```bash
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml --profile connector-host up -d connector-host
```

Host 监听 <http://localhost:8001>，接收 `ProductionEvent[]`，并以至少传递一次将标准批次发送到 Platform。使用方负责这个本地入口的部署和运维。

## 3. 提交第一批生产事件

使用你自己的数据源适配进程调用 Platform。示例使用一个 `cycle.started` 事件：

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
      "source": "edge/EDGE-001/demo/FURNACE-01",
      "subject": { "type": "asset", "id": "FURNACE-01" },
      "context": { "workpiece_id": "WP-001" },
      "data": {},
      "correlationId": "CYCLE-001",
      "seq": 1
    }]
  }'
```

响应中的 `ackSeq` 表示 Platform 已安全接收或识别为重复的连续序号。调用方应保存本地序号，仅重试未确认事件。

## 4. 启用生产 Chat

默认 Compose 保持 Chat 关闭。以下配置启用 OpenAI、本地平台身份 `operator` 和完整数据范围：

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true

docker compose -f docker-compose.app.yml up -d --build
```

生产部署必须接入统一认证，并以每个平台用户实际所需的数据范围替代全局范围。Chat API 与 Platform Web 不使用独立用户名或密码。

## 5. 查询事件并使用 Chat

```bash
curl "http://localhost:8000/api/v1/events?edgeId=EDGE-001&correlationId=CYCLE-001"
```

然后在 Platform Web 中打开 **Chat** 并提问：

```text
这个周期发生了什么，数据是否完整？
```

Chat 会返回只读工具活动、注意事项和数据引用。完整事件字段见[生产事件规范](rfc-production-events.md)，Chat 行为见[Ingot Chat](chat.md)。
