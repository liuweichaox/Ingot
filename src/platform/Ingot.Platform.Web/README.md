# Ingot Platform Web

Ingot Platform Web 是面向工艺工程师的 AI 工艺研发工作界面，基于 React 19、Tailwind CSS、Headless UI 与 Vite 构建。

界面围绕工艺研发闭环组织：

- **工作台**：查看研发项目、待办任务、近期实验与验证状态。
- **AI 助手**：基于当前项目的数据、机理和知识组织分析，给出带依据的下一步建议。
- **运行与追溯**：查看实验或生产周期、过程事件、配方、设备和工装上下文。
- **质量管理**：维护检测定义与质量方案，录入并分析质量结果。
- **分析中心**：比较实验和周期、检查数据质量、研究参数与结果之间的关系。
- **数据资产**：管理工艺数据模型、配方版本、分析方案、采集配置和边缘节点。
- **系统管理**：查看系统状态、用户、订阅和日志。

页面应让工艺工程师随时看清当前目标、已有证据、下一步实验、验证状态和可复用结论。底层协议、存储和服务结构不应成为用户完成任务的前提。

## 本地开发

要求 Node.js `>=22.13.0`。

先启动 PostgreSQL 与 Platform API：

```bash
export INGOT_POSTGRES_PASSWORD="development-postgres-password"
export INGOT_EDGE_TOKEN="development-edge-token-0001"
export INGOT_OPERATOR_TOKEN="development-operator-token-0001"
export INGOT_CONNECTOR_TOKEN="development-connector-token-0001"
docker compose -f docker-compose.app.yml up -d postgres
dotnet run --project src/platform/Ingot.Platform.Api
```

再启动前端：

```bash
cd src/platform/Ingot.Platform.Web
npm ci
npm run dev
```

开发地址为 `http://localhost:3000`。

## 验证

```bash
npm run build
npm test
npm run lint
```
