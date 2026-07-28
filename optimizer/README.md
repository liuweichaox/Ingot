# Ingot 工艺优化服务

本目录实现系统设计中的代理模型和序贯实验推荐内核。它根据平台提供的研发项目快照与有效观察，返回下一批**待工程师审核**的实验参数；它不保存业务状态，也不直接控制设备。

整体边界和分层见 [`docs/design.md`](../docs/design.md)。

## 当前能力

- 连续可控变量、工程单位、参数硬约束和实测结果安全约束；
- `<=`、`>=`、靶值和范围目标；
- 目标权重、BoTorch/GPyTorch 多输出 GP 与 95% 预测区间；
- 带结果可行性约束的批量 `qLogNEHVI` / `qLogNEI`；
- 两种决策意图：`reach-specification` 用于逼近规格，`validate-hypothesis` 用于安全地最大化假设关键变量的可辨识信息；
- “设定参数→实际轨迹→质量结果”两级 GP；
- 安全基线局部冷启动、pending experiments 和幂等批次；
- 只能从真实历史配方池中选择的离线回放；
- 无状态 `POST /v1/suggestions` HTTP 契约；
- 合成数字孪生演示。

NumPy/SciPy GP 只保留为冷启动与回归基线；三个有效观察后，生产路径进入 BoTorch 内核。

## 本地验证

使用 Python 3.11 或更高版本。推荐使用 `uv`：

```bash
cd optimizer
uv sync --extra service --extra viz --group dev
uv run pytest
uv run python demo.py
uv run uvicorn service:app --port 8110
```

Windows 如果默认 `python` 指向旧版环境，可显式使用 Python Launcher：

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\python -m pip install -e ".[service,viz]" pytest httpx2
.\.venv\Scripts\python -m pytest
```

`demo.py` 只验证优化机制，不代表真实工艺效果。运行结果可能随数值库版本变化，不在文档中固化成功率或节省炉数。

本地开发默认使用 `8110`，避免与设备采集示例常用的 `8100` 冲突。Docker Compose 内部的优化服务仍使用 `8100`。

## 无状态接口

```http
POST /v1/suggestions
Content-Type: application/json
```

```json
{
  "campaign": {
    "name": "LENS-A",
    "process_profile": "generic",
    "decision_intent": "reach-specification",
    "variables": [
      {"name": "soak_temp", "low": 320, "high": 360, "unit": "C"}
    ],
    "objectives": [
      {"name": "form_error", "kind": "le", "threshold": 0.5, "weight": 2, "unit": "um"}
    ],
    "constraints": [
      {
        "variable": "soak_temp",
        "operator": "<=",
        "limit": 355,
        "safety_critical": true
      }
    ],
    "outcome_constraints": [
      {
        "name": "crack_rate",
        "operator": "<=",
        "limit": 0.02,
        "minimum_probability": 0.95,
        "safety_critical": true
      }
    ]
  },
  "observations": [
    {
      "params": {"soak_temp": 340},
      "outcomes": {"form_error": 0.8},
      "constraint_outcomes": {"crack_rate": 0.005},
      "process_features": {"mold_temp.cycle.overshoot": 2.1}
    }
  ],
  "pending_points": [{"soak_temp": 342}],
  "top_k": 3,
  "seed": 7
}
```

响应包含推荐参数、每项目标的均值与 95% 区间、预计距规格距离、可行概率、采集值、模型版本和推荐理由。平台把整批结果直接创建为普通实验计划。

当 `decision_intent` 为 `validate-hypothesis` 时，还必须提供 `hypothesis_variables`。平台只会在假设已经定义目标、预期方向和最小有效效应后发起该请求；服务不会把相关性当作因果结论。

## 真实历史回放

```python
from ingot_optimizer import Campaign, Objective, Variable
from ingot_optimizer.replay import replay_history_pool

campaign = Campaign(
    "历史件",
    [Variable("soak_temp", 320, 360, "C")],
    [Objective("form_error", "le", threshold=0.5, unit="um")],
)
history = [
    {
        "params": {"soak_temp": 330},
        "outcomes": {"form_error": 1.2},
    },
    {
        "params": {"soak_temp": 345},
        "outcomes": {"form_error": 0.4},
    },
]
print(replay_history_pool(campaign, history))
```

历史池回放只回答“在已经做过的配方里，模型能否更早排到达标点”。它不为未做过的配方伪造结果，也不能单独证明上线后一定节省多少炉。重复配方应先按既定统计方法聚合。

## 产品接线

`.NET` 平台是唯一业务记录源：

1. 实验 `RunKey` 映射到现场的运行标识，平台自动把过程特征、配方实值和检验结果组成每次真实运行的观察；
2. 平台将项目定义、有效观察和约束完整发送给本服务；
3. 本服务无状态计算下一批参数；
4. 平台创建带输入哈希、模型版本和预测区间的普通实验；
5. 全部运行完成后自动固化正式实验结果；若该实验验证某假设，结果置信区间会自动更新假设的支持、否决或不确定结论；实验仍沿用已有批准、运行和完成流程。

具体 PLC、仪器、视觉、文件或 API 的接入只负责把数据映射为该契约，不改变优化服务的产品边界。
