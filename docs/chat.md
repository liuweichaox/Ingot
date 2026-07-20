# Ingot Chat

Ingot Chat 是工程师使用 Ingot 的主要入口：它帮助人们查询已保存的生产数据、检查数据质量并定位周期数据问题。Ingot 是制造数据采集与工艺分析平台；Chat 是平台中的对话和调查工作台。它以只读方式访问生产记录并保留现场设备状态。

## 能力

- 理解自然语言问题以及当前资产或周期页面上下文；
- 生成受控的结构化查询计划；
- 调用 `check_data_quality` 检查事件完整性、上下文缺失、事件新鲜度和可用范围；
- 调用 `get_cycle_trace` 按 `correlationId` 返回排序后的周期事件链；
- 流式展示计划、只读工具活动、回答、限制条件和证据引用；
- 保存当前 Actor 可见的对话历史，并支持取消和 SSE 续读。

当前可用工具为 `check_data_quality` 与 `get_cycle_trace`。

## 深入调查：有界多 Agent 协作

日常问题使用 `standard` 模式。对于需要多角度审查的复杂问题，可以显式启用 `deep` 深入调查模式。

- **工艺角色**审查周期、状态变化和参数差异；
- **质量角色**审查检测结果、样本范围和质量关联；
- **反证角色**主动寻找数据缺口、混杂因素和替代解释；
- 三个角色只读取本次已验证的工具结果，不能自行访问数据库、网络或设备；
- 默认最多 3 轮、9 次发言，硬上限为 5 轮、15 次；第一轮至少两名角色成功，才会给出候选解释；
- 输出包含支持依据、反证和限制条件，只能称为候选解释，不能确认根因或因果关系。

这不是无约束群聊。它是一个有固定角色、轮次、证据范围和终止条件的只读调查流程；工程师始终负责最终判断。

## 执行流程

```text
用户问题与页面上下文
  → 意图和时间范围解析
  → 权限、工具与数据范围校验
  → 只读数据工具
  → 数字、单位和证据校验
  → 回答、限制条件与数据引用
```

模型负责理解问题和组织语言；确定性代码负责数据查询、权限、范围限制、工具执行和证据验证。Chat 使用受控数据工具，不生成 SQL、Flux 或脚本。

## 生产启用

生产 Compose 默认关闭 Chat。启用时同时配置模型、模型密钥、Actor 令牌和数据范围：

```bash
export INGOT_CHAT_ENABLED=true
export INGOT_CHAT_PROVIDER=OpenAI
export INGOT_CHAT_FAST_MODEL="<fast-model>"
export INGOT_CHAT_REASONING_MODEL="<reasoning-model>"
export OPENAI_API_KEY="<secret>"
export INGOT_CHAT_OPERATOR_TOKEN="$(openssl rand -hex 24)"
export INGOT_CHAT_OPERATOR_ALLOW_ALL=true
export INGOT_CHAT_ENABLE_DEEP_INVESTIGATION=true
```

Platform Web 与 HTTP API 使用 Actor `operator` 和 `INGOT_CHAT_OPERATOR_TOKEN`。生产部署应按每个 Actor 所需事实范围配置访问权限。

## 使用

1. 打开 Platform Web，进入 **Chat**。
2. 输入问题，例如“这个周期发生了什么，数据是否完整？”
3. 可选择资产或周期并填写页面上下文。
4. 查看只读工具活动、结论、限制条件和证据。
5. 从证据引用返回生产事件或周期数据。

## HTTP API

| 接口 | 用途 |
|---|---|
| `GET /api/v1/chat/capabilities` | 查询启用状态、模式、只读工具、模型和运行上限 |
| `POST /api/v1/chat/runs` | 创建对话运行 |
| `GET /api/v1/chat/runs` | 按当前 Actor 分页查询历史 |
| `GET /api/v1/chat/runs/{runId}` | 查询运行快照、计划、工具、证据和回答 |
| `GET /api/v1/chat/runs/{runId}/stream` | 通过 SSE 接收事件；使用 `Last-Event-ID` 续读 |
| `POST /api/v1/chat/runs/{runId}:cancel` | 取消运行 |

```bash
curl -X POST http://localhost:8000/api/v1/chat/runs \
  -H "Content-Type: application/json" \
  -H "X-Ingot-Actor: operator" \
  -H "Authorization: Bearer ${INGOT_CHAT_OPERATOR_TOKEN}" \
  -d '{
    "question": "这个周期发生了什么，数据是否完整？",
    "pageContext": { "kind": "cycle", "id": "CYCLE-001" },
    "mode": "deep"
  }'
```

## 安全边界

- 只读工具白名单仅包含已注册的事实查询工具；
- 工具继承当前 Actor 的数据范围；
- 禁止任意 SQL、脚本、Shell、文件系统和开放网络访问；
- 工具结果中的指令性文本按普通事实处理，不能改变系统策略；
- 回答数字必须来自工具结果，关键结论必须包含可解析证据；
- 数据不足、范围冲突或质量不合格时明确给出限制，不生成确定性结论；
- Chat 不修改配置、事件、检测记录或设备状态。

配置见[配置说明](tutorial-configuration.md)，事实契约见[生产事件规范](rfc-production-events.md)。
