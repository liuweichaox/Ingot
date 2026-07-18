# 快速开始

## 启动 Central 与 Connector Host

环境要求：.NET 10、Node.js 22.13+、Docker Engine 26+ 和 Docker Compose。

```bash
export INGOT_POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export INGOT_EDGE_TOKEN="$(openssl rand -hex 24)"
export INGOT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CONNECTOR_TOKEN="$(openssl rand -hex 24)"
docker compose -f docker-compose.app.yml up -d --build
```

打开 `http://localhost:3000`。Central Web 提供事件、检测、日志、指标与 Chat；Connector Host 在 `http://localhost:8001` 接收标准事件。

## 使用 Chat

Chat 默认关闭。配置 `Chat:Enabled`、模型、Actor Token 和 Actor 数据范围后重新创建 Central API。进入 Central Web 的 **Chat**，输入问题并可附带资产或周期上下文。

```text
这个周期发生了什么，数据是否完整？
```

Chat 只调用 `check_data_quality` 与 `get_cycle_trace`，返回工具活动、结论、限制条件和证据。完整接口见 [Chat](chat.md)。

## 下载 Ingot Agent

从 [GitHub Releases 最新版本](https://github.com/liuweichaox/Ingot/releases/latest) 下载 Ingot Agent Desktop。首次启动时填写：

1. Central API 地址；
2. Actor ID；
3. Actor Token。

桌面端先验证 Agent 能力，再创建结构化连接器代码任务。它展示 SSE 进度、源码、禁网容器构建/样本测试输出和制品。Agent 不连接数据源。测试通过后，授权 Actor 审查并批准打包；桌面端生成 ZIP、校验服务端 SHA-256 后下载。

完整流程见 [Ingot Agent 桌面端](desktop-agent.md)。Central Web 不提供 Agent 代码生成界面。

## 验证标准事件入口

外部连接器把源数据转换为 `ProductionEvent[]`，使用 `INGOT_CONNECTOR_TOKEN` 向 `POST http://localhost:8001/api/v1/connector-events` 提交。默认 Agent 模板只负责 stdin JSONL 到 stdout ProductionEvent JSONL 转换；外部运行时负责批处理、鉴权和提交。

## 完整门禁

```bash
./scripts/verify.sh
```
