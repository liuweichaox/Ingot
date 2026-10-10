# 文档维护指南

本文面向文档贡献者，定义文档分工、事实核对和发布前检查。用户入口见[文档首页](index.md)，开发环境见[贡献指南](../CONTRIBUTING.md)。

## 文档组织原则

README 回答“项目是什么、适合谁、如何开始、在哪里获得帮助”。文档站按入门、操作指南、系统概念和参考资料组织。同一业务规则保留一个主要说明位置，其他页面通过链接引用；不要在不同页面维护相互矛盾的规则或配置副本。

- **入门**：提供最短可执行路径、前置条件、检查点和下一步。
- **操作指南**：围绕具体任务说明输入、步骤、完成标志和失败处理。
- **系统概念**：解释职责、数据流、设计原因和约束，区分当前实现与目标架构。
- **参考资料**：提供可查阅的对象定义、配置来源、表职责和术语。

现有 URL 保持稳定。移动内容时保留原入口或增加重定向，并检查页面内锚点。

## 参考项目

结构参考以下项目，Ingot 的业务内容、配置和能力声明以本仓库为依据：

- [Best-README-Template](https://github.com/othneildrew/Best-README-Template)：README 的项目介绍、安装、使用、贡献、许可证和支持入口。
- [OpenTelemetry Documentation](https://opentelemetry.io/docs/)：按读者任务组织入门，同时把概念与实现参考分开。
- [Gitea Documentation](https://docs.gitea.com/)：自托管项目的安装、使用、管理与维护文档。
- [Docker Get started](https://docs.docker.com/get-started/)：先说明前置条件，再给操作路径和检查点。

参考信息架构与写作方法，不复制项目介绍或移植不适合 Ingot 的运行能力。引用他人原文、代码或资产时，核对相应许可并保留必要署名。

## 事实来源与能力声明

产品定位与用词以[品牌规范](brand.md)为准。当前能力以[当前状态](status.md)和对应实现为准；规划只在[发展规划](project-plan.md)声明。

核对操作内容时直接检查：

- 服务、端口、profile 和依赖：`docker-compose.app.yml`。
- 配置键与默认值：`.env.example`、宿主配置及配置验证器。
- API 路径与授权：API 控制器、端点注册和授权规则；启动后的 OpenAPI 见[快速开始](getting-started.md)。
- 数据职责与迁移：`src/platform/Ingot.Platform.Infrastructure/Migrations/sql` 和[数据模型](data-model.md)。
- 验证范围：实际执行的测试与门禁；测试通过只证明覆盖到的软件行为。

说明“已实现”“部署者需配置或验证”“计划能力”的区别。不要把单机 Compose 写成高可用，也不要把最低样本数写成充分的统计证据。收益、因果、安全和现场适用性必须有对应证据。

## 双语与文档站

中文文件使用 `docs/<slug>.md`，英文文件使用 `docs/<slug>.en.md`。标题层级、步骤顺序、代码示例和边界含义应一致；正文允许按各语言习惯表达。链接优先使用同语言相对路径，根目录文档用 `../`。

`docs/` 是文档内容的单一来源，文档站从这些文件渲染。新增公开页时更新 `apps/docs-site/lib/public-docs.json` 与导航，确保两种语言都能访问、搜索和切换。不要把人工维护的正文复制到 React 组件或生成目录。

## 操作示例写法

操作步骤至少包含前置条件、工作目录、命令、完成标志和失败后的定位入口。明确 Bash 与 PowerShell 差异；不要让读者猜测命令在哪个终端执行。

使用占位值说明参数，并明确替换要求。示例不含真实生产数据、令牌或设备标识。备份恢复和升级操作说明数据保护要求；诊断命令默认只读，破坏性清理不能作为常规排障步骤。

## 提交前验证

在仓库根目录执行：

```bash
./scripts/verify-architecture.sh
./scripts/verify-product-scope.sh
./scripts/verify-product-language.sh
python3 scripts/verify-documentation-style.py
npm --prefix apps/docs-site run lint
npm --prefix apps/docs-site test
git diff --check
```

文档站测试包含静态构建与导出检查。人工检查桌面和窄屏阅读、任务入口、目录锚点、语言切换、搜索以及源码/反馈链接。更改公开行为、运行配置或架构时，还应运行相关测试；提交 PR 前按仓库要求运行 `./scripts/verify.sh`。

在变更说明中写出实际通过的检查与未完成原因，不把未执行的命令写成通过。最后核对英文页和所有相对链接。
