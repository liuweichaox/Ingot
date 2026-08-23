# Ingot 工艺研发服务

> 文档状态：当前数值服务开发说明。

本目录实现 Ingot 方法工具箱中的代理模型和序贯实验推荐能力。它根据平台提供的研发项目快照与有效观察，返回下一批**待工程师审核**的实验参数；它不保存业务状态，也不直接控制设备。GP/BO 是适合昂贵小样本序贯实验的当前方法，不是所有工艺问题的唯一答案，也不重新定义产品核心价值。

整体边界和分层见 [`docs/design.md`](../docs/design.md)，方法选择原则见 [`docs/optimization.md`](../docs/optimization.md)。

## 当前能力

- 连续可控变量、工程单位、参数硬约束和实测结果安全约束；
- 材料、设备、模具/工装和配方类别等离散因素作为显式 `context` 分层；不得把类别编号伪装成连续控制量；
- `<=`、`>=`、靶值和范围目标；
- 目标权重、BoTorch/GPyTorch 多输出 GP 与 95% 预测区间；
- 可声明结果物理边界；正式 PASS/FAIL 目标固定在 0–1，后验均值、区间和采集函数都遵守该边界；
- 先按 GP 结果安全概率过滤候选，再按可见观察执行响应面、GP 概率与机理特征准入；
- 两种决策意图：`reach-specification` 用于逼近规格，`validate-hypothesis` 用于安全地最大化假设关键变量的可辨识信息；
- “设定参数→实际轨迹→质量结果”两级 GP；
- 由项目版本化配置声明的安全派生特征，不根据行业、设备或变量名称触发隐藏逻辑；
- 安全基线局部冷启动、pending experiments 和幂等批次；
- 只能从真实历史配方池中选择的离线回放；
- 无状态 `POST /v1/suggestions` HTTP 契约；
- 合成数字孪生演示。

NumPy/SciPy GP 只保留为冷启动与回归基线；三个有效观察后，生产路径进入 BoTorch 内核。达到规格时，正则化线性响应面是默认方法。样本达到最低容量后，系统对线性面与二次面做配对留一预测，并比较归一化的目标排序误差；只有二次面的改善连续三个扩展历史都超过一个标准误才升级，证据不明时保留线性面。信息优先的模型辨别只属于工程师显式选择的“验证假设”路径，不自动消耗达到规格的实验预算。只有非线性证据已经成立且可见观察达到每个原始控制量至少六条，GP 后验达标概率才接管选点。声明式机理特征也必须满足容量和配对预测证据；未通过时从代理模型中移除。所有准入只读取已经揭示的观察和候选参数，不读取候选结果，也不按数据集名称分支。GP 始终负责预测区间和结果安全概率；当前后继策略已经使旧冻结验收失效，仍需新的未见数据验收。

当前数值优化只直接搜索连续可控变量。需要比较多个离散水平时，平台应按分类上下文建立独立活动，或先采用适用的完整/部分因子设计；不同材料、设备或工装的数据不会因为编号相邻而被模型视为相似。

## 本地验证

Python 环境统一由 `uv 0.11.32` 管理，不使用手工创建的 venv 或 pip 安装项目依赖：

```bash
cd optimizer
uv sync --extra service --extra viz --group dev --locked
uv run --locked pytest
uv run --locked uvicorn service:app --port 8110
```

如果本机还没有符合要求的 Python，交给 `uv` 安装并选择 3.12：

```bash
uv python install 3.12
uv sync --python 3.12 --extra service --extra viz --group dev --locked
```

依赖版本以 `uv.lock` 为准。修改 `pyproject.toml` 后运行 `uv lock` 并同时提交锁文件；CI 和容器均拒绝未同步的锁文件。

本地开发默认使用 `8110`，避免与 Docker Compose 中优化服务的 `8100` 端口冲突。

## 公开数据回归

仓库使用固定公开实验数据检查历史池回放、随机与响应面基线比较，以及不利结果是否仍保持正确声明边界。检查保留的冻结验收完整性和当前算法的指纹失效状态：

```bash
./scripts/verify-optimizer-acceptance.sh
```

普通 CI 中的 `optimizer/tests/test_optimizer_acceptance.py` 运行快速、确定性的完整性与无泄漏检查。冻结的 450 次正式回放已完整保留；算法变化后不得重跑成新的验收。数据来源、当前失败结果和更新规则见[公开数据实验效率验证](../tools/public-validation/README.md)。

当前方法选择策略的开发回归使用四个强基线和机理特征消融：

```bash
./scripts/benchmark-optimizer-development.sh
```

该命令只读取已经披露的数据，用于开发和防止退化。当前开发问题是区分适用的线性与二次结构。形成新的效果证据时，必须先提交后继算法，再选择全新数据并冻结目标、预算和判定规则；不能用修改后的算法重跑旧数据并把结果称为独立验证。

## 无状态接口

```http
POST /v1/suggestions
Content-Type: application/json
```

```json
{
  "campaign": {
    "name": "PROCESS-A",
    "feature_set_id": "project-a-features",
    "feature_set_version": 1,
    "derived_features": [
      {
        "name": "control_balance",
        "operator": "absolute_difference",
        "inputs": ["control_a", "control_b"],
        "normalization_offset": 0,
        "normalization_scale": 20
      }
    ],
    "decision_intent": "reach-specification",
    "variables": [
      {"name": "control_a", "low": 100, "high": 140, "unit": "unit"},
      {"name": "control_b", "low": 100, "high": 140, "unit": "unit"}
    ],
    "objectives": [
      {"name": "deviation", "kind": "le", "threshold": 0.5, "weight": 2, "unit": "unit"}
    ],
    "constraints": [
      {
        "variable": "control_a",
        "operator": "<=",
        "limit": 135,
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
      "params": {"control_a": 120, "control_b": 115},
      "outcomes": {"deviation": 0.8},
      "constraint_outcomes": {"crack_rate": 0.005},
      "process_features": {"measured_response.overshoot": 2.1}
    }
  ],
  "pending_points": [{"control_a": 122, "control_b": 116}],
  "top_k": 3,
  "seed": 7
}
```

响应包含推荐参数、每项目标的均值与 95% 区间、预计距规格距离、可行概率、采集值、模型版本和推荐理由。平台还会用历史实际条件的最小间距检查候选条件是否真的可区分；低于该分辨率的多个浮点值不会被包装成多个实验条件。通过检查后，平台才把整批结果创建为普通实验计划。

`derived_features` 只能使用固定的数值运算符，并按声明顺序引用控制变量或此前的派生特征。运算在工程单位中进行，再由 `normalization_offset` 和 `normalization_scale` 归一化。组成问题还可用带固定属性系数的 `weighted_mean` 和 `weighted_standard_deviation`；权重必须非负且总和为正。服务拒绝任意 Python 表达式、未知输入、前向引用和旧式隐藏 `process_profile`。

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
        "occurred_at": 1_750_000_000.0,
        "params": {"soak_temp": 330},
        "outcomes": {"form_error": 1.2},
    },
    {
        "occurred_at": 1_750_000_060.0,
        "params": {"soak_temp": 345},
        "outcomes": {"form_error": 0.4},
    },
]
print(replay_history_pool(campaign, history))
```

历史池回放只回答“在已经做过的配方里，模型能否更早排到达标点”。每一行必须提供有限数值 `occurred_at`，并按时间严格递增；缺失、重复或乱序会被拒绝，服务不会自动排序，以免掩盖未来数据泄漏。它不为未做过的配方伪造结果，也不能单独证明上线后一定节省多少试验次数。重复配方应先按既定统计方法聚合。

在线建议、历史回放和合成回放共用同一个引擎选择入口：少于 3 条有效观察时使用序贯冷启动并允许 NumPy GP 先验；达到 3 条后统一切换 BoTorch，安全结果约束继续由所选引擎的 `suggest` 路径强制执行。调用方不得自行实例化具体引擎。

合成回放的 truth 函数必须返回 `SyntheticTruthResult`，明确提供 `outcomes`、`constraint_outcomes` 和可选的 `process_features`。成功判定同时要求目标达标和全部结果安全约束通过。

## 产品接线

`.NET` 平台是唯一业务记录源：

1. 实验 `ExecutionKey` 映射到现场的运行标识，平台自动把过程特征、控制参数实值和检验结果组成每次真实运行的观察；
2. 平台将项目定义、有效观察和约束完整发送给本服务；
3. 本服务无状态计算下一批参数；
4. 平台创建带输入哈希、模型版本和预测区间的普通实验；
5. 全部运行完成后自动固化正式实验结果；若该实验验证某假设，结果置信区间会自动更新假设的支持、否决或不确定结论；实验仍沿用已有批准、运行和完成流程。

具体 PLC、仪器、视觉、文件或 API 的接入只负责把数据映射为该契约，不改变优化服务的产品边界。
