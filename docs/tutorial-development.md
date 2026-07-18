# 开发指南

## 产品面

- Central Web 只提供事实页面和 Chat；不要在浏览器中增加 Agent 代码生成入口。
- `/api/v1/chat/*` 只处理只读分析用途，工具仅为 `check_data_quality` 与 `get_cycle_trace`。
- `/api/v1/agent/*` 只处理连接器代码生成，必须验证 `X-Ingot-Client: ingot-agent-desktop`。
- Chat 与 Agent 的创建、历史、读取、流和取消必须按产品面与 Actor 隔离。

## 模块依赖

1. `Ingot.Contracts`：公共 Chat、Agent、连接器、事件与检测契约；
2. `Ingot.Agent`：产品面、工作流、计划、预算和验证；
3. `Ingot.Agent.Infrastructure`：模型、SQLite Store 和工具；
4. `Ingot.Connector.Builder`：Actor 工作区、固定容器构建/测试、批准和打包；
5. `Ingot.Central.Infrastructure`：中心事实与 Chat 只读工具；
6. `Ingot.Connector.Host`：协议无关标准事件入口；
7. `desktop`：Tauri 2 桌面 Agent 客户端。

## 连接器扩展

不要把设备协议 SDK 或读取器链接进平台核心。具体协议由 Ingot Agent 写入连接器工作区。通用扩展应修改连接器规格、模板、确定性转换测试和 Builder 固定入口。

默认模板必须保持：

```text
stdin/json-lines → stdout/production-event-json-lines
```

连接器验收覆盖工作区固定样本与模拟输入的解析、字段映射、单位、事件类型、对象、上下文、关联 ID 和 `ProductionEvent` 校验。Agent 与 Builder 不连接真实数据源。生成包不得包含生产 Token、HTTP 提交客户端或自动部署脚本。

## 桌面开发

```bash
cd desktop
npm install
npm run desktop:dev
```

桌面端所有 Central 请求必须经过 Rust 原生边界，添加固定桌面客户端标识，限制允许的 Central URL，并校验下载 SHA-256。React 层不得直接持有不受控网络或文件系统权限。

## 门禁

```bash
./scripts/verify.sh
```

提交同时更新中英文文档与测试。详细约束见 [Chat](chat.md) 和 [Ingot Agent 桌面端](desktop-agent.md)。
