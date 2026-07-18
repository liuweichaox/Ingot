# 配置说明

配置分为 Chat、Ingot Agent、Connector Builder、Connector Host 和中心事实服务。桌面端只保存 Central URL、Actor 与 Token；模型、工作区和 Builder 均在 Central 部署配置。

## Chat

```json
{
  "Chat": {
    "Enabled": false,
    "Provider": "Deterministic",
    "FastModel": "deterministic-v1",
    "ReasoningModel": "deterministic-v1",
    "MaxToolCalls": 8,
    "MaxRunSeconds": 60,
    "RequireToken": true,
    "ActorTokens": {},
    "ModelPricing": {}
  },
  "ChatDataAccess": {
    "Actors": {
      "operator": { "AllowAll": false, "EdgeIds": ["EDGE-01"] }
    }
  }
}
```

Chat 只注册 `check_data_quality` 与 `get_cycle_trace`。每个 Actor 必须配置事实访问范围；生产环境优先列出明确的 `EdgeIds`，只有受信的全局角色可使用 `AllowAll=true`。

## Ingot Agent

```json
{
  "Agent": {
    "Enabled": false,
    "DatabasePath": "Data/agent.db",
    "Provider": "Deterministic",
    "FastModel": "deterministic-v1",
    "ReasoningModel": "deterministic-v1",
    "MaxToolCalls": 24,
    "MaxIterations": 8,
    "MaxRunSeconds": 300,
    "RequireToken": true,
    "ActorTokens": {},
    "PackagingApprovers": ["operator"],
    "ModelPricing": {}
  }
}
```

Agent 只接受来自 Ingot Agent Desktop 的 `standard` 连接器代码生成运行。`PackagingApprovers` 必须引用已配置 Actor；只有这些 Actor 可以打开人工打包批准门。

正式环境启用 Chat 或 Agent 时，Provider 使用 `OpenAI` 并配置模型名称与 `OPENAI_API_KEY`。`Deterministic` 仅用于开发和自动化测试。Actor Token、模型密钥和连接器密钥通过环境变量或 Secret Store 注入。

模型用量使用供应商响应中的输入和输出 token。只有每个实际模型都配置同一币种的 `ModelPricing` 时才计算 `estimatedCost`；否则返回 `null`。

## Connector Builder

```json
{
  "ConnectorBuilder": {
    "WorkspaceRoot": "Data/connector-workspaces",
    "ArtifactRoot": "Data/connector-packages",
    "ContainerCommand": "docker",
    "ContainerWorkspaceVolume": "",
    "DotnetSdkImage": "mcr.microsoft.com/dotnet/sdk:10.0",
    "CommandTimeoutSeconds": 120,
    "MaxFileBytes": 524288,
    "MaxWorkspaceFiles": 256,
    "MaxWorkspaceBytes": 8388608,
    "MaxOutputCharacters": 32000
  }
}
```

Builder 只在禁网子容器中执行平台固定的构建和测试入口，测试输入仅来自工作区内的固定样本与模拟数据。Agent 和 Builder 不连接数据源。模型不能选择命令、镜像、宿主路径或工作目录。工作区按 Actor 隔离，单文件上限 512 KiB，最多 256 个可见文件和 8 MiB 源码。

## Connector Host

```json
{
  "ConnectorHost": { "MaxBatchSize": 1000 },
  "Context": { "DatabasePath": "Data/context.db" },
  "Events": {
    "DatabasePath": "Data/events.db",
    "MaxBacklogRows": 500000
  }
}
```

Connector Host 接受标准 `ProductionEvent[]`。达到 outbox 上限时删除最旧未上报记录，并记录 `diagnostic.backlog_dropped` 与丢弃指标。

## 环境变量

.NET 层级配置使用双下划线：

```text
Chat__Enabled=true
Chat__Provider=OpenAI
Chat__FastModel=<model>
Chat__ReasoningModel=<model>
Chat__ActorTokens__operator=<secret-store-reference>
Agent__Enabled=true
Agent__Provider=OpenAI
Agent__FastModel=<model>
Agent__ReasoningModel=<model>
Agent__ActorTokens__operator=<secret-store-reference>
Agent__PackagingApprovers__0=operator
ChatDataAccess__Actors__operator__AllowAll=false
ChatDataAccess__Actors__operator__EdgeIds__0=EDGE-01
OPENAI_API_KEY=<secret-store-reference>
ConnectorBuilder__DotnetSdkImage=mcr.microsoft.com/dotnet/sdk:10.0
ConnectorHost__IngestToken=<secret-store-reference>
Edge__EventIngestToken=<secret-store-reference>
ConnectionStrings__Events=<secret-store-reference>
```

生产环境还必须配置 CORS、数据库、事件摄入和检测提交凭据。完整启动要求见[部署](tutorial-deployment.md)。
