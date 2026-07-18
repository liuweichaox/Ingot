# Ingot Central Web

基于 Vue 3 与 Vite 的 Central 操作界面，提供 Chat 工艺分析、边缘节点、生产事件、指标和日志。Chat 通过只读工具查找生产数据问题，页面能力由服务端能力接口、Actor 权限和运行状态驱动。

## 本地开发

要求 Node.js `>=22.13.0`。

### 1. 启动 PostgreSQL 与 Central API

```bash
export INGOT_POSTGRES_PASSWORD="development-postgres-password"
export INGOT_EDGE_TOKEN="development-edge-token-0001"
export INGOT_OPERATOR_TOKEN="development-operator-token-0001"
export INGOT_CONNECTOR_TOKEN="development-connector-token-0001"
docker compose -f docker-compose.app.yml up -d postgres
dotnet run --project src/Ingot.Central.Api
```

### 2. 启动前端

```bash
cd src/Ingot.Central.Web
npm ci
npm run dev
```

## 运行地址

- dev server：`http://localhost:3000`
- 事件页：`http://localhost:3000/events`
- Chat 工艺分析：`http://localhost:3000/chat`
- `vite.config.mjs` 将 `/api`、`/metrics` 和 `/health` 代理到 `http://localhost:8000`

Chat 服务默认关闭。页面通过 `GET /api/v1/chat/capabilities` 获取实际开放的模式、角色、只读工具和运行限制，不在前端推断服务端能力。

## 验证

```bash
npm run build
npm test
npm run lint
```
