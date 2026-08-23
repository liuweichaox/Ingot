<div align="center">
  <a href="https://ingotstack.com">
    <img src="apps/website/public/brand/ingot-lockup.svg" alt="Ingot" width="340">
  </a>

  <p><strong>Open-source industrial process optimization / 开源工业工艺优化系统</strong></p>
  <p>用真实生产数据持续优化工艺。</p>

  [![CI](https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml/badge.svg)](https://github.com/liuweichaox/Ingot/actions/workflows/ci.yml)
  [![License: MIT](https://img.shields.io/badge/license-MIT-E8AD56.svg)](LICENSE)
  [![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
  [![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB.svg)](https://www.python.org/)
  [![BoTorch](https://img.shields.io/badge/optimizer-BoTorch-5FD4C8.svg)](https://botorch.org/)

  [三分钟演示](#三分钟看懂-ingot) · [工作原理](#一条完整闭环) · [快速开始](#快速开始) · [验证结果](#现在能做什么) · [参与讨论](https://github.com/liuweichaox/Ingot/discussions)

  简体中文 · [English](README.en.md)
</div>

![Ingot：从真实运行到工艺优化与验证](apps/website/public/og.png)

## Ingot 是什么

Ingot 是一套开源工业工艺优化系统，将现场运行数据和质量结果转化为可复现的实验观察、下一步参数建议和经过验证的工艺操作域。

系统沿同一条数据闭环连接现场采集、运行识别、质量结果、工艺追因和实验设计，再根据问题选择 DOE、响应面或受约束贝叶斯优化方法推荐下一组工艺条件。证据快照、人工审批和影子验证用于约束优化结果如何进入生产；Agent 只负责查询和解释，不是优化或控制主体。

## 三分钟看懂 Ingot

模拟数据讲的是一个具体问题：`RUN-2026-0821-005` 的镜片面形误差达到 **0.48 μm**，超过 **0.35 μm** 上限；相邻合格运行是 **0.22 μm**。不需要数据库或 Docker，在两个终端运行：

```bash
node scripts/platform-demo.mjs
```

```bash
npm --prefix apps/platform ci
npm --prefix apps/platform run demo
```

打开 `http://127.0.0.1:3001`，使用 `demo / demo` 登录。工作台会给出四步入口：打开超差运行 → 核对已复核质量结果 → 与合格运行比较 → 查看候选原因、混杂因素和下一项验证实验。用户可以在三分钟内走完“看清这次运行——找到关键差异——决定下一步”的主线。

## 为什么做 Ingot

Ingot 围绕一条可复核的工艺优化链路建设：

> **把每次真实运行变成可比较、可验证的工程证据，持续选择更值得验证的下一组工艺条件，并沉淀适用范围明确的工艺操作域。**

项目保留的目标表述是：**把每次真实运行变成可比较、可验证的工程证据，帮助工艺工程师减少无效实验，更快找到达到目标的工艺条件。** 这是需要由公开回放和真实项目持续验证的目标，不是对所有工艺和算法的既成效果承诺。

传统工艺研发中，大量判断依赖个人记忆、零散表格和不可复现的试验顺序。即使设备已经产生数据，生产条件、过程曲线和质量结果也常常无法对应到同一次真实运行，计算机因此无法可靠地参与工程判断。

Ingot 先建立可信的数据闭环，再帮助工程师：

- 找到一次运行实际用了什么设备、产品、工艺规范、材料和工装；
- 把实际设定、过程轨迹和检验结果关联到同一次运行；
- 比较可比运行，发现差异发生在哪个变量、阶段或上下文；
- 区分候选原因、混杂因素和证据不足；
- 把工程判断转成可证伪、可审核的实验；
- 在安全边界内选择更有价值的下一步实验；
- 保存经过验证的结论、适用范围和失效条件。

系统负责整理运行事实、比较候选方法并提出参数建议；工艺工程师负责定义目标和安全边界、执行验证并判断结果能否进入生产。

## 一条完整闭环

```text
工艺配置 → 现场接入 → 生产运行 → 质量管理 → 工艺追因 → 工艺研发
    ↑                                                      ↓
    └────────── 已验证工艺规范、工艺操作域与知识返回生产 ──────────┘
```

| 阶段 | 解决的问题 |
|---|---|
| 工艺配置 | 平台应记录哪些变量、单位、目标、质量规则和安全边界？ |
| 现场接入 | 控制系统、仪器、视觉、检验和业务数据如何映射成稳定业务语义？ |
| 生产运行 | 这次真实运行用了什么条件，过程实际发生了什么？ |
| 质量管理 | 检验结果能否准确关联到同一次运行并完成独立复核？ |
| 工艺追因 | 数据是否可信，哪些差异值得验证，哪些仍受混杂或证据不足限制？ |
| 工艺研发 | 下一步做什么实验最有价值，并且没有越过已声明的安全边界？ |

采集不是终点，优化算法也不是起点。可信运行事实是所有分析和建议的共同地基。

## 按问题选择计算方法

Ingot 不把某一种“先进算法”固定成所有问题的答案：

- 数据覆盖、缺失和漂移使用数据质量统计；
- 正常/异常差异使用稳健统计、匹配比较和阶段分析；
- 设备、材料、工装等上下文使用分层统计、方差分量或混合效应模型；
- 原因判断使用对照、重复、区组、随机化和干预实验；
- 昂贵的小样本参数搜索使用高斯过程和受约束贝叶斯优化；
- 机理明确而数据较少时融合物理特征或先验；
- LLM 用于解析工程问题、调用只读工具和组织证据说明，不直接生成数值工艺设定。

详细方法边界见[分析与优化](docs/optimization.md)。

## 产品组成

- **Edge**：连接控制系统、仪器、网关和业务数据源，完成语义映射、执行边界识别、断网缓存与补传。
- **Process Executions**：把连续信号组织成可追溯的真实运行和阶段轨迹。
- **Manufacturing**：记录产品、工艺规范、设备、材料、组件和工装等运行上下文。
- **Inspections**：保存质量目标、安全结果、附件和人工复核。
- **Research**：组织问题、候选原因、假设、实验、结果和工艺操作域。
- **Optimizer**：执行可重复的数值建模、约束判断和序贯实验建议。
- **Agent**：帮助工程师查询、组织和解释经过验证的系统事实。

这些模块围绕同一条证据链工作，不建立互相冲突的平行业务记录。

## 和已有工具的边界

| 类别 | Ingot 使用它解决什么 | Ingot 不是什么 |
|---|---|---|
| MES、SCADA、Historian | 接收运行、设备和过程事实 | 不替代生产执行、监控或实时控制 |
| LIMS、QMS、ELN | 关联检验结果、审核和研发上下文 | 不替代完整样品、合规或文档管理 |
| DOE、响应面、贝叶斯优化 | 在统一约束和证据快照下选择实验方法 | 不声称某一种算法适合所有工艺 |
| AI Agent | 查询、组织和解释已授权事实 | 不直接写入参数、批准实验或控制设备 |

历史回放或方法准入不通过时，Optimizer 可以退回传统 DOE 或正则化响应面。公开冻结结果用于界定声明边界，而不是替代真实项目验证。

## 现在能做什么

仓库已实现从现场数据到下一项实验建议的主要路径：

- 按真实运行关联设备、产品、工艺规范、材料、工装、过程轨迹和检验结果；
- 在一屏内比较超差与合格运行，显示关键差异、缺失和数据来源；
- 把候选原因转成带对照、重复、区组和安全边界的可执行实验；
- 根据已完成实验给出下一项具体参数建议，并说明它是沿当前稳定趋势继续尝试，还是在探索可能更好的参数组合；
- 保留输入、版本、来源、约束和实验结果，使建议可以复核和重放。

### 当前证据边界

| 冻结评估 | 观察结果 | 可以得出的结论 |
|---|---|---|
| 450 次开发数据配对回放 | 优于随机和最大最小空间填充；总体结果与正则化二次响应面相同；未在所有数据集上不劣于线性响应面 | 软件与响应面建模链路可复现；核心选点优势尚未证明 |
| 400 次未见数据回放 | 总体成功率 85.5%；优于所测线性和二次响应面，但未通过相对随机与最大最小空间填充的预注册门槛 | 不能据此声称对经典方法具有普遍优势 |
| 预注册过程特征消融 | 没有显示稳定的增量贡献 | 机理特征贡献尚未证明 |

完整比较方法、置信区间和失败分项见[优化器实验效率验证](tools/public-validation/README.md)；真实试点验收见[场景验证](docs/rollout.md)。

## 系统架构

```mermaid
flowchart LR
    Sources["控制系统 / 仪器 / 视觉 / 检验 / MES"] --> Edge["Edge ConnectorHost\n映射 · 执行边界 · 缓存"]
    Edge --> Platform["Platform API\n运行 · 上下文 · 检验 · 研发 · 证据"]
    Platform --> Web["Platform Web\n工程师工作台"]
    Platform --> Optimizer["Optimizer\nDOE · 响应面 · GP/BO · 参数建议"]
    Platform --> Agent["Agent\n查询 · 组织 · 解释"]
    Engineer["工艺工程师"] --> Web
    Web --> Platform
    Optimizer --> Platform
    Agent --> Platform
```

Platform 是厂内业务记录源；Optimizer 是无状态数值服务；Agent 只能使用授权工具访问结构化事实。Edge 与 Platform 即使部署在同一台机器，也保持独立进程、存储和故障恢复。

## 快速开始

使用完整 Docker Compose 栈只需要 Git、Docker Engine 或 Docker Desktop，以及 Docker Compose v2。只有从源码开发时才需要 .NET SDK 10、Node.js 22.22+ 和 uv 0.12.5。

如果只想先查看界面和完整模拟业务数据，可以直接使用[模拟数据快速预览](docs/getting-started.md#模拟数据快速预览)；准备真实试点或生产部署时再使用下面的完整 Compose 栈。

首次启动前请修改 `.env` 中的数据库密码、Edge 令牌和管理员配置。默认认证模式为 `Local`；
生产环境不要使用 `Disabled`，除非这是明确隔离的演示部署并同时设置了
`INGOT_ALLOW_INSECURE_DEMO=true`。

```bash
git clone https://github.com/liuweichaox/Ingot.git
cd Ingot
cp .env.example .env
docker compose -f docker-compose.app.yml up -d --build
```

首次构建会下载 .NET、Node、Python、PyTorch 和 TimescaleDB 镜像，耗时取决于网络。命令结束后用
`docker compose -f docker-compose.app.yml ps -a` 确认 `platform-migrate` 成功退出、四个 HTTP/数据库核心服务均为 `healthy`，且 `platform-worker` 持续为 `running`；不要把“仍在下载镜像”误认为应用已经启动。

启动后访问：

```text
http://localhost:3000       工程工作台
http://localhost:8000/health
http://localhost:8000/openapi/v1.json
http://localhost:8100/ready
```

本地认证使用 `.env` 中的 `INGOT_ADMIN_USERNAME` 和 `INGOT_ADMIN_PASSWORD`。若管理员密码留空，Migrator 只在空用户表首次创建管理员时把随机口令输出到 `platform-migrate` 日志。启动与排障细节见[快速开始](docs/getting-started.md)和[部署运维](docs/deployment.md)。

首次使用应先完成一条真实或代表性的数据闭环，再进入追因和研发：建立工艺配置 → 接入现场数据 → 完成一次运行 → 关联检验 → 检查数据可信度 → 比较运行 → 设计验证实验。

完整步骤见[快速开始](docs/getting-started.md)。

## 开发验证

```bash
dotnet restore Ingot.sln
dotnet build Ingot.sln
dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj
npm --prefix apps/platform ci
npm --prefix apps/platform test
uv sync --project optimizer --extra service --group dev --locked
```

完整 CI 门禁：

```bash
./scripts/verify.sh
```

使用固定公开制造数据运行完整离线基准：

```bash
./scripts/verify-optimizer-acceptance.sh
```

该基准验证可复现的软件和方法链路，不替代真实项目的影子验证或受控在线验证。当前结果与判定规则见[公开数据实验效率验证](tools/public-validation/README.md)。

运行当前优化策略在已披露数据上的开发回归：

```bash
./scripts/benchmark-optimizer-development.sh
```

开发回归用于确认方法选择已经修复已知退化，不作为新的效果证明。公开验收只使用“开发回归、未见数据验收、真实试点”三个状态；内部历史批次和完整失败结果仍保留在验证目录中供审计。

## 仓库结构

```text
src/edge/          现场采集与可靠上送
src/platform/      中心 API 与业务模块
src/agent/         模型辅助查询与证据说明
src/shared/        领域模型与公共契约
optimizer/         数值分析与贝叶斯优化服务
tests/             .NET 核心测试
apps/platform/     React 工艺研发工作台
apps/website/      官方网站
apps/docs-site/    文档站
docs/              中英文项目文档
deploy/            部署资产
scripts/           验证与运维脚本
```

## 文档

- [文档首页](docs/index.md)
- [快速开始](docs/getting-started.md)
- [系统设计](docs/design.md)
- [分析与优化](docs/optimization.md)
- [公开数据实验效率验证](tools/public-validation/README.md)
- [机理知识设计](docs/mechanism-knowledge.md)
- [数据接入](docs/data-connection.md)
- [场景验证](docs/rollout.md)
- [发展规划](docs/project-plan.md)
- [部署运维](docs/deployment.md)
- [常见问题](docs/faq.md)
- [品牌规范](docs/brand.md)
- [开源依赖](docs/open-source-dependencies.md)

## 路线图

- [x] 真实运行、实际条件、过程特征与检验结果形成可追溯观察
- [x] 候选原因、假设、实验和工艺操作域使用同一研发记录主线
- [x] 受约束 GP/BO 建议、待执行点避让和安全冷启动
- [x] 用明确许可的公开制造数据固化可复现基准和声明边界
- [x] 完成一组未参与开发的公开物理实验评估、强基线比较和机理特征消融，并保留“尚未证明”的结果
- [ ] 公布真实制造历史项目的无泄漏逐次回放
- [ ] 完成新项目影子建议和工程师拒绝原因分析
- [ ] 完成受控在线实验并公布预注册结果
- [ ] 用第二个明显不同的工艺验证核心契约的通用性
- [ ] 将只读与提议能力开放为模型无关的 Agent 协议
- [ ] 发布制造智能证据与实验协议的 Schema、验证器和参考实现候选版

近期先证明历史证据装置可信，中期再开放 Agent 协议，长期才争取形成开放规范。路线图以真实证据和验收闸门为准，详细顺序见[发展规划](docs/project-plan.md)。

## 参与贡献

如果你也在研究工业数据如何进入可审计的实验闭环，可以 Star 本仓库以关注后续验证结果，并通过 [Discussions](https://github.com/liuweichaox/Ingot/discussions) 分享工艺场景、数据契约或方法建议。

欢迎贡献设备适配、统计方法、实验设计、优化算法、真实回放、测试、文档和工艺知识。开始前请阅读[贡献指南](CONTRIBUTING.md)、[行为准则](CODE_OF_CONDUCT.md)和[安全策略](SECURITY.md)。

Ingot 使用 [MIT License](LICENSE)。
