# 参与 Ingot 开发

[English](CONTRIBUTING.en.md)

感谢你为 Ingot 提交代码、测试、文档或设计建议。所有变更都必须保持生产事实可信、Chat 只读、桌面 Agent 权限受控、连接器可审计和中英文文档一致。

## 开发原则

- `Ingot.Domain`、`Ingot.Application` 和 `Ingot.Agent` 不依赖具体数据库、模型供应商或设备协议。
- 设备和系统差异通过连接器规格与连接器包表达，不把厂商 SDK 加入平台核心。
- Chat 模型负责问题理解和回答组织；事实查询、数据校验、权限、运行上限和证据验证使用确定性代码。
- Chat 事实工具保持只读。Agent 制品工具只能写入 Actor 隔离连接器工作区和平台控制的版本化记录。
- 不提供任意 SQL 执行、脚本执行、Shell、开放网络、文件路径或设备控制能力。
- 公共契约不保留重复字段、隐式别名或静默兼容逻辑。

## 本地环境

要求：

- .NET SDK 10
- Node.js 22.13 或更高版本
- Docker 与 Docker Compose

安装依赖：

```bash
dotnet restore Ingot.sln
npm --prefix src/Ingot.Central.Web ci
npm --prefix desktop ci
npm --prefix site ci
npm --prefix docs-site ci
```

本地服务启动方式见[快速开始](docs/tutorial-getting-started.md)。

## 变更要求

### Chat、Agent 与模型

- 核心只依赖 `IModelClient`、`IAnalysisTool` 和其他 `Ingot.Agent` 接口。
- 模型输出必须是强类型结果，并在执行前经过确定性校验。
- 新工具必须提供稳定名称、版本、JSON Schema、访问类型、超时、取消和结果上限。
- 每个关键数字和结论必须能解析到真实 `EvidenceRef`。
- `/api/v1/chat/*` 只接受只读事实查询；`/api/v1/agent/*` 只接受带固定桌面客户端标识的连接器代码生成。
- Chat 与 Agent 的运行、历史、事件和权限必须按产品面与 Actor 隔离。

### 连接器

- 连接器统一输出 `ProductionEvent[]`，不得把源协议模型泄漏到核心契约。
- 连接器规格必须包含协议、端点、认证、数据契约、采样策略和验收条件。
- 源码包必须包含 Dockerfile、`connector.manifest.json` 和测试文件。
- 生成代码只能由 Connector Builder 在禁网、只读源码的受限 Docker 子容器中运行固定构建和测试入口；禁止宿主构建、任意命令和运行时拉取镜像。
- Agent 和 Builder 不连接真实数据源；测试必须使用工作区内的固定样本与模拟输入。
- 测试通过后必须停止在打包批准门，由操作者审查当前修订并显式批准；平台只生成可校验 ZIP，不部署、启动或调度连接器。

### API 与存储

- API 输入必须在控制器边界完成类型和权限校验。
- PostgreSQL 用于中心事实；SQLite WAL 用于现场事件、outbox、Agent 运行和制品。
- 数据库改动必须包含初始化或迁移、并发语义、失败处理和集成测试。
- 日志、指标和追踪不得记录密钥、完整提示或敏感工具参数。

### Web 与文档

- Central Web 只展示 Chat，能力和只读工具由 `/api/v1/chat/capabilities` 驱动；代码生成只能在 Ingot Agent Desktop 中展示。
- 新增图表能力时必须先定义 `ChartSpec` 类型白名单、确定性校验、渲染器和测试；禁止执行模型生成的前端代码。
- 官网只描述已经实现的能力，示例事实必须明确标识。
- 修改公开能力、配置、接口或术语时，同步更新 README、中英文 `docs/`、官网和文档站。

## 测试

提交前运行完整门禁：

```bash
./scripts/verify.sh
```

门禁包括：

- .NET 构建与单元、集成测试；
- Central Web 构建、测试、Lint 和生产依赖审计；
- 官网与文档站静态构建、链接测试、Lint 和生产依赖审计；
- 架构依赖、Shell 语法、Compose 配置和差异格式检查。

新增行为至少包含正常路径、拒绝路径和权限边界测试。修复缺陷时先增加能够复现问题的测试。

## Pull Request

1. Fork 仓库并从最新 `main` 创建功能分支。
2. 保持变更范围单一，不混入无关格式化或重构。
3. 更新实现、测试和相关中英文文档。
4. 执行 `./scripts/verify.sh`。
5. 提交 Pull Request，并说明：
   - 问题与目标；
   - 公共契约或数据模型变化；
   - 安全与权限影响；
   - 验证结果；
   - 部署或配置要求。

普通缺陷和功能建议使用 [GitHub Issues](https://github.com/liuweichaox/Ingot/issues)。安全问题不得创建公开 Issue，请遵循[安全策略](SECURITY.md)。
