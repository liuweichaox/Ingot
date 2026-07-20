# 配置

Platform 使用环境变量和受保护配置存储。生产环境不要把密码、令牌或模型密钥提交到仓库。

## 必需配置

```json
{
  "Urls": "http://0.0.0.0:8000",
  "ConnectionStrings": {
    "Events": "Host=postgres;Database=ingot;Username=ingot;Password=<secret>"
  },
  "EventIngest": {
    "RequireToken": true,
    "EdgeTokens": {
      "EDGE-001": "<secret>"
    }
  },
  "InspectionSubmission": {
    "RequireToken": true,
    "UserTokens": {
      "OPERATOR-001": "<strong-secret>"
    }
  },
  "Cors": {
    "AllowedOrigins": ["https://platform.example.com"]
  },
  "Chat": {
    "Enabled": true,
    "RequireToken": true,
    "Provider": "OpenAI",
    "FastModel": "<fast-model>",
    "ReasoningModel": "<reasoning-model>",
    "UserTokens": {
      "operator": "<strong-secret>"
    },
    "MaxToolCalls": 8,
    "MaxRunSeconds": 60
  }
}
```

`EventIngest:EdgeTokens` 的键必须与批次请求中的 `edgeId` 一致。每个数据源适配运行应使用独立、可轮换的令牌。

## Chat 数据范围与生产启用

```json
{
  "ChatDataAccess": {
    "Users": {
      "operator": { "AllowAll": true, "EdgeIds": [] }
    }
  }
}
```

Chat 只注册 `check_data_quality` 与 `get_cycle_trace`。每个用户 必须配置记录访问范围。生产 Compose 的 `operator` 示例使用 `AllowAll=true`；受限部署可在受保护配置中为用户 列出明确的 `EdgeIds`。

## 环境变量

```bash
ConnectionStrings__Events='Host=postgres;Database=ingot;Username=ingot;Password=<secret>'
EventIngest__RequireToken=true
EventIngest__EdgeTokens__EDGE-001='<secret>'
InspectionSubmission__RequireToken=true
InspectionSubmission__UserTokens__OPERATOR-001='<strong-secret>'
Cors__AllowedOrigins__0='https://platform.example.com'
Chat__Enabled=true
Chat__RequireToken=true
Chat__Provider=OpenAI
Chat__FastModel='<fast-model>'
Chat__ReasoningModel='<reasoning-model>'
OPENAI_API_KEY='<secret>'
Chat__UserTokens__operator='<strong-secret>'
ChatDataAccess__Users__operator__AllowAll=true
```

使用 Docker Compose 时，使用以下生产变量名称：

```bash
INGOT_CHAT_ENABLED=true
INGOT_CHAT_PROVIDER=OpenAI
INGOT_CHAT_FAST_MODEL='<fast-model>'
INGOT_CHAT_REASONING_MODEL='<reasoning-model>'
OPENAI_API_KEY='<secret>'
INGOT_CHAT_OPERATOR_TOKEN='<strong-secret>'
INGOT_CHAT_OPERATOR_ALLOW_ALL=true
```

生产验证器要求事件和检测提交令牌、CORS 来源，以及 Chat Provider 为 `OpenAI`、同时存在 Fast/Reasoning 模型、`OPENAI_API_KEY`、Chat 用户令牌和每个用户 的数据范围。模型密钥只从 Secret Store 或环境变量读取。日志默认不记录完整问题、回答或敏感工具参数。

## 运行上限

- `Chat:MaxToolCalls`：单次对话最多调用的只读工具数；
- `Chat:MaxRunSeconds`：单次对话最长运行时间；
- 事件批次：每批 1 至 500 条；
- 事件来源：必须以 `edge/{edgeId}/` 开头；
- Chat 未启用或认证未配置时，Platform 事件、查询和检测链路保持可用。

参见[部署](tutorial-deployment.md)和[生产事件规范](rfc-production-events.md)。
