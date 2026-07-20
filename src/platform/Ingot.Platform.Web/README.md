# Ingot Platform Web

基于 Vue 3 与 Vite 的 Platform 操作界面，提供数据接入节点、生产事件、指标、日志和 **Ingot Chat**。

## 产品入口

- **Ingot Chat**：基于可信生产数据进行对话查询、问题定位和分析；回答提供数据证据与限制条件。
- **数据接入节点**：查看用户自行部署的数据适配器接入状态。
- **生产事件、指标与日志**：查看平台已接入的生产数据和运行状态。

Ingot Chat 仅访问当前身份有权读取的生产数据，不提供设备控制、配置修改或数据写入。

## 本地开发

要求 Node.js `>=22.13.0`。

### 1. 启动 PostgreSQL 与 Platform API

```bash
export INGOT_POSTGRES_PASSWORD="development-postgres-password"
export INGOT_EDGE_TOKEN="development-edge-token-0001"
export INGOT_OPERATOR_TOKEN="development-operator-token-0001"
export INGOT_CONNECTOR_TOKEN="development-connector-token-0001"
docker compose -f docker-compose.app.yml up -d postgres
dotnet run --project src/platform/Ingot.Platform.Api
```

### 2. 启动前端

```bash
cd src/platform/Ingot.Platform.Web
npm ci
npm run dev
```

## 运行地址

- dev server：`http://localhost:3000`
- 事件页：`http://localhost:3000/events`
- Ingot Chat：`http://localhost:3000/chat`
- `vite.config.mjs` 将 `/api`、`/metrics` 和 `/health` 代理到 `http://localhost:8000`

Ingot Chat 服务默认关闭。页面通过 `GET /api/v1/chat/capabilities` 获取当前可用的回答方式；对话、历史、流式结果和取消请求均使用 `/api/v1/chat/*`。

## 验证

```bash
npm run build
npm test
npm run lint
```
