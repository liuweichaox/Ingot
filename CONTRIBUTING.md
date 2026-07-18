# 参与 Ingot 开发

[English](CONTRIBUTING.en.md)

感谢你为 Ingot 提交代码、测试、文档或设计建议。所有变更都必须保持生产事实可信、Ingot Chat 只读、数据接入契约稳定和中英文文档一致。

## 开发原则

- `Ingot.Domain`、`Ingot.Application` 和 `Ingot.Agent` 不依赖具体数据库、模型供应商或设备协议。
- 设备和系统差异由用户的接入程序或现有系统处理，不把厂商 SDK 加入平台核心。
- Chat 模型负责问题理解和回答组织；事实查询、数据校验、权限、运行上限和证据验证使用确定性代码。
- Chat 事实工具保持只读。
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
npm --prefix site ci
npm --prefix docs-site ci
```

本地服务启动方式见[快速开始](docs/tutorial-getting-started.md)。

## 变更要求

### Ingot Chat 与模型

- 核心只依赖 `IModelClient`、`IAnalysisTool` 和其他 `Ingot.Agent` 接口。
- 模型输出必须是强类型结果，并在执行前经过确定性校验。
- 新工具必须提供稳定名称、版本、JSON Schema、访问类型、超时、取消和结果上限。
- 每个关键数字和结论必须能解析到真实 `EvidenceRef`。
- `/api/v1/chat/*` 只接受只读事实查询。
- Chat 的运行、历史、事件和权限必须按 Actor 隔离。

### 连接器

- 连接器统一输出 `ProductionEvent[]`，不得把源协议模型泄漏到核心契约。
- 数据源接入程序由用户实现和部署，并通过标准事件契约向 Ingot 提交生产事实。
- 接入程序必须通过公开事件契约将事实提交到 Ingot，并且不得把源协议模型泄漏到核心契约。
- 接入文档必须说明鉴权、幂等、时间戳、单位、质量字段和可恢复错误。

### API 与存储

- API 输入必须在控制器边界完成类型和权限校验。
- PostgreSQL 用于中心事实；SQLite WAL 用于现场事件、outbox 和 Chat 运行。
- 数据库改动必须包含初始化或迁移、并发语义、失败处理和集成测试。
- 日志、指标和追踪不得记录密钥、完整提示或敏感工具参数。

### Web 与文档

- Central Web 的 AI 入口统一为 Ingot Chat，能力和只读工具由 `/api/v1/chat/capabilities` 驱动。
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
