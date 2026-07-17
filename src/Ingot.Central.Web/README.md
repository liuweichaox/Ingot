# Ingot Central Web

Vue 3 + Vite 控制面，包含节点、生产事件、指标和日志页面。

## 本地开发

1) 启动 PostgreSQL 与中心后端

```bash
docker compose -f docker-compose.events.yml up -d postgres
dotnet run --project src/Ingot.Central.Api
```

2) 启动前端（dev server）

```bash
cd src/Ingot.Central.Web
npm install
npm run dev
```

说明：

- dev server：`http://localhost:3000`
- 事件页：`http://localhost:3000/events`
- `vite.config.mjs` 将 `/api`、`/metrics` 和 `/health` 代理到 `http://localhost:8000`
- 生产构建：`npm run build`
