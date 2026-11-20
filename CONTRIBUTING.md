# 参与 Ingot

[English](CONTRIBUTING.en.md)

Ingot 接受通用代码、算法、获授权的公开回放数据、测试、文档和工艺知识贡献；具体设备适配器由部署者在仓库外维护。所有贡献都必须保留证据与安全声明的边界，不得夸大运行效率。

提交贡献即表示贡献者同意遵守[行为准则](CODE_OF_CONDUCT.md)。

除非贡献者另有明确书面说明，提交至本仓库并被接纳的贡献按 [Apache License 2.0](LICENSE) 提供。

## 开始之前

可复现的缺陷提交 Issue；使用问题进入 Discussions；安全问题按 [SECURITY.md](SECURITY.md) 私密报告。问题报告写明版本或提交号、环境、复现步骤、预期与实际行为，并提供已去除敏感信息的日志。

1. 在 Issues 中确认没有重复问题；
2. 较大功能先创建讨论 Issue，说明场景、输入、输出和验证方式；
3. 安全漏洞使用[安全策略](SECURITY.md)，不要公开提交；
4. 不上传工厂凭据、设备地址、客户数据或未授权运行数据。

## 开发环境

按本次改动选择最小环境。只改文档或单个应用时，不需要先安装整套开发工具。

| 改动范围 | 最少工具 | 本地准备 |
|---|---|---|
| Markdown、README | Python 3；运行公开用语门禁时还需 Bash | `python3 scripts/verify-documentation-style.py` |
| `apps/platform` | Node.js 22.22+、npm | `npm --prefix apps/platform ci` |
| `apps/website` | Node.js 22.22+、npm | `npm --prefix apps/website ci` |
| `apps/docs-site` | Node.js 22.22+、npm | `npm --prefix apps/docs-site ci` |
| .NET 服务 | .NET SDK 10 | `dotnet restore Ingot.sln` |
| Optimizer | Python 3.12、uv 0.12.5 | `uv sync --project optimizer --extra service --group dev --locked` |

表中命令在仓库根目录执行。安装依赖后，在对应应用目录运行 `npm run dev` 本地预览、`npm run lint` 和 `npm test`，也可用 `npm --prefix <应用目录>` 从仓库根目录执行。

- 工作台需要运行中的 Platform API。Vite 默认监听 `http://localhost:3000`，代理目标由 `INGOT_PLATFORM_API_TARGET` 指定，默认为 `http://127.0.0.1:8000`。`npm test` 运行 node:test 和 Vitest，另用 `npm run build` 验证生产构建。生产代理规则见 [`apps/platform/nginx.conf`](apps/platform/nginx.conf)。
- 官网文案位于 `apps/website/app/IngotSite.tsx`，中英文由各自入口渲染，措辞及素材以[品牌规范](docs/brand.md)为依据。官网和文档站的 `npm test` 分别构建并检查静态页面与文档导出。公网发布见[部署说明](deploy/README.md)。

提交前的完整门禁需要 .NET SDK 10、Node.js 22.22+、Python 3.12、uv 0.12.5、Docker Compose 和 Bash。它会验证三个前端、.NET、Optimizer、部署清单及架构和产品边界；准备好这些工具后，在仓库根目录运行 `./scripts/verify.sh`。门禁要求不因本地迭代只安装单个应用而改变。

## 工程原则

- 每项能力都要说明如何改善“达到规格所需运行数”或闭环可信度；
- 采集、检验和优化只保留一套以真实运行为核心的正式业务记录；
- 明确区分计划值、实际值、过程轨迹和结果；
- 模型输出必须带版本、不确定性和数据来源；
- LLM 不生成数值工艺参数；
- 设备协议、点位映射和厂商适配器不进入核心仓库；核心只保留事件契约、验证、诊断和通用业务能力；
- 不把模拟结果写成真实工艺收益；
- 公共能力变更同步更新中英文 README、文档和官网。

新增运行期依赖应服务于明确的工程需求，评估维护状态、许可证兼容性和厂内可部署性。固定版本或使用受控版本范围，按实际功能、版本、传递依赖和发布方式核对许可，在镜像和发布产物中保留所需声明。云端专有服务不得成为采集、记录、检验或数值分析的必需条件。

依赖使用代码与清单、锁文件同批评审，纳入构建、漏洞、许可与供应链核查。主要版本升级执行完整验证与相关回放，及时移除闲置依赖，在发布前处理许可或维护状态变化。精确版本以对应提交的 `.csproj`、`Directory.Build.props`、`package-lock.json`、`optimizer/uv.lock`、Dockerfile 和 Compose 清单为依据。发布时使用实际生成的 SBOM 或依赖清单。

## 核心与现场适配器边界

Ingot 的核心仓库只维护跨现场通用的能力：领域模型、事件契约、证据装配、分析、优化、权限、审计、诊断与测试。具体设备协议、点位映射、厂商 SDK、单位换算和现场特有的重试策略属于现场适配器，由部署者根据现场情况编写和维护，可以使用 AI 辅助生成。

这是有意的取舍：现场连接是长尾问题，协议、设备、网络和现场约束各不相同。把它们通用化会把核心变成难以维护的驱动集合，降低代码质量，却仍然覆盖不了真实现场。适配器外部化后，本仓库只对稳定契约负责。

因此：

- 不向 `src/` 提交设备协议驱动、点位映射表或厂商 SDK 封装；
- 适配器只依赖公开的事件契约、验证器和契约测试，不引用 Platform 内部类型；
- 核心负责事件契约、错误码、诊断和验证；可复用的适配器模板与契约测试属于独立工具，不包含协议实现；
- 只服务单一工厂、单一设备或单一协议的代码属于适配器，放在仓库外；
- 不提交没有用户路径、测试和真实部署场景的推测性模块；需要时再实现；
- 判断通用能力的标准是：换一个现场、设备或产品后，代码不需要改，或者只需要改配置。

## 变更流程

```bash
git checkout -b codex/short-description
```

实现时：

- 修复缺陷先增加能复现问题的测试；
- 新行为覆盖成功、拒绝和边界路径；
- 数据库变化提供迁移和失败恢复；
- 算法变化提供确定种子、基线和可复现评估；
- UI 保持工艺工程师语言，不暴露原始 JSON 编辑器。

代码与注释风格：

- 遵循仓库根目录 `.editorconfig`：C# 与 Python 使用 4 空格，JavaScript、JSX、JSON、YAML 与 Shell 使用 2 空格；统一 UTF-8、LF 和文件末尾换行；
- 优先用领域命名、明确的类型、职责清晰的模块、易读的控制流和具体的测试名称表达意图。遇到需要解释代码在做什么的注释，先改善代码结构；
- 在外部约束、设计取舍、兼容要求或失败后果容易被误解时，用简短的注释说明原因。同一文件内保持注释语言一致，新写的 C# 业务与契约说明默认用中文，Optimizer Docstring 用英文。协议名、配置键和标识符保留原文；
- 公共 C# 接口及门禁指定的 Application 和 Agent 公共类须有类型级 XML `summary`，Optimizer 的公共类型和入口函数须有 Docstring。说明调用方需要知道的约定；
- 新增迁移须写明用途。已提交且受校验和保护的迁移保持原样。

提交前运行：

```bash
./scripts/verify.sh
```

如果本机缺少 Docker 或其他运行时，在 PR 中明确列出未执行的检查。

## 验证与文档

只安装本次工作涉及的前端应用即可，完整验证会检查三个应用。Windows 上可使用 PowerShell 执行 .NET、npm 和 uv 命令；Bash 门禁使用 WSL 或具备所需工具的 Bash 环境。不要在 Windows 与 WSL 之间共用 `node_modules`。

迭代时运行与修改相关的测试与应用 lint，再执行完整门禁。变更架构、产品范围、公开术语或文档时，还要直接运行以下阻断检查：

```bash
./scripts/verify-architecture.sh
./scripts/verify-product-scope.sh
./scripts/verify-product-language.sh
python3 scripts/verify-documentation-style.py
```

- 同一规则在一个主要位置维护，其他页面用链接引用。命令写明适用终端、工作目录、前置条件、预期结果与失败后如何定位。示例使用可识别的占位值，诊断命令默认只读，备份、恢复和升级操作说明数据保护要求。
- 用实现和运行配置核实现有行为。服务、端口和 profile 查 `docker-compose.app.yml`，默认值查 `.env.example`、宿主配置与验证器，API 与授权查端点和授权规则，存储查 SQL 迁移。[当前状态](docs/status.md)汇总已交付能力。区分已实现、部署者待验证和条件性目标，结论只写到实际测试或现场验收所覆盖的范围。用获授权的数据支撑现场适用性、安全和收益声明。
- `docs/<slug>.md` 与 `docs/<slug>.en.md` 同步维护标题层级、步骤和行为边界。同语种使用相对链接。公开页面在 `apps/docs-site/lib/public-docs.json` 登记，在 `apps/docs-site/lib/docs.ts` 分配一个导航组，并更新 `apps/docs-site/tests/export.test.mjs` 的目录与索引断言。正文由 Markdown 渲染，搜索索引和品牌素材由 `prepare-content.mjs` 生成与复制。
- 改名或合并时保留已发布入口或提供同语言的静态跳转，并检查页内锚点。跳转页不加入内容目录、导航、搜索和站点地图。用 `npm --prefix apps/docs-site test` 核对双语导出、链接、目录和搜索索引，再检查语言切换、桌面和窄屏导航、任务入口及源码与反馈链接。生成物 `out/` 和 `public/search-index.json` 由构建流程产生。

## Pull Request

保持单个 PR 聚焦一个可独立审查的结果。先说明问题与最终行为，再提供证据；尚未执行的检查和已知限制直接注明。

PR 应包含：

- 问题和真实使用场景；
- 方案与不采用的替代方案；
- 公共契约或数据模型变化；
- 算法、设备或安全影响；
- 测试和回放结果；
- 部署或迁移要求；
- UI 变化截图。

使用简洁的命令式提交，例如：

```text
feat(optimizer): add calibrated outcome constraints
fix(edge): preserve FX3U cycle correlation after reconnect
docs: document historical replay protocol
```

## 贡献方向

- 适配器契约测试、事件映射示例和适配器编写指南；
- 真实制造场景下的工艺特征和物理先验；
- 贝叶斯优化、迁移学习与校准；
- 具有明确授权、许可和来源说明的回放数据与基准；
- 现场可用性、诊断和文档。
