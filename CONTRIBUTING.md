# 参与 Ingot

[English](CONTRIBUTING.en.md)

感谢你帮助 Ingot 用更少的真实实验优化制造工艺。我们欢迎代码、设备适配、算法、匿名回放数据、测试、文档和工艺知识贡献。

提交贡献即表示你同意遵守[行为准则](CODE_OF_CONDUCT.md)。

## 开始之前

1. 在 Issues 中确认没有重复问题；
2. 较大功能先创建讨论 Issue，说明场景、输入、输出和验证方式；
3. 安全漏洞使用[私有漏洞报告](SECURITY.md)，不要公开提交；
4. 不上传工厂凭据、设备地址、客户数据或未授权实验数据。

## 开发环境

要求：

- .NET SDK 10；
- Node.js 22.22+；
- uv 0.11.32（由 uv 管理 Python 3.11+ 环境）；
- Docker 与 Docker Compose。

```bash
dotnet restore Ingot.sln
npm --prefix apps/platform ci
npm --prefix apps/website ci
npm --prefix apps/docs-site ci
uv sync --project optimizer --extra service --group dev --locked
uv run --project optimizer --locked pytest
```

## 工程原则

- 每项能力都要说明如何改善“达到规格所需实验数”或闭环可信度；
- 采集、检验、实验和优化只保留一套正式业务记录；
- 明确区分计划值、实际值、过程轨迹和结果；
- 模型输出必须带版本、不确定性和数据来源；
- LLM 不生成数值工艺参数；
- 设备协议不进入核心领域模型；
- 不把模拟结果写成真实工艺收益；
- 公共能力变更同步更新中英文 README、文档和官网。

## 变更流程

```bash
git checkout -b feature/short-description
```

实现时：

- 修复缺陷先增加能复现问题的测试；
- 新行为覆盖成功、拒绝和边界路径；
- 数据库变化提供迁移和失败恢复；
- 算法变化提供确定种子、基线和可复现评估；
- UI 保持工艺工程师语言，不暴露原始 JSON 编辑器。

代码与注释风格：

- 遵循仓库根目录 `.editorconfig`：C# 与 Python 使用 4 空格，JavaScript、JSX、JSON、YAML 与 Shell 使用 2 空格；统一 UTF-8、LF 和文件末尾换行；
- 同一文件内保持注释语言一致；新增的 C# 业务与契约代码默认使用中文说明，Optimizer Python 模块沿用英文 Docstring，协议名、配置键和代码标识保持原文；
- 注释说明业务约束、设计原因、失败边界或不明显的不变量，不逐行复述代码，也不保留已失效的注释代码；
- C# 公共类型或成员需要说明时使用 XML 文档注释，Python 公共模块或函数需要说明时使用 Docstring；完整句子使用一致的标点。
- 所有公共 C# 接口必须有类型级 `summary`，Optimizer 的公共类型与入口函数必须有 Docstring；提交检查会拒绝缺失项。
- 每个支持注释的源码、测试、脚本和构建文件至少包含一处职责、约束或失败边界说明；JSON 等不支持注释的纯数据格式，以及受已提交校验和保护的历史迁移除外。新增迁移仍须在首次提交时写明用途。

提交前运行：

```bash
./scripts/verify.sh
```

如果本机缺少 Docker 或其他运行时，在 PR 中明确列出未执行的检查。

## Pull Request

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

- 更多真实设备协议适配；
- 真实制造场景下的工艺特征和物理先验；
- 贝叶斯优化、迁移学习与校准；
- 匿名真实回放数据和基准；
- 现场可用性、诊断和文档。
