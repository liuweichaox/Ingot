# 贡献指南

感谢你关注 Ingot。

本项目的核心目标不是构建功能泛化的中心平台，而是持续强化 PLC 数据采集主链路的稳定性、正确性、实时性和可观测性。所有贡献都应优先服务这一目标。

## 贡献原则

- Edge Agent 是主产品，Central API / Web 是诊断和管理辅助面
- 不要绕过 `QueueService` 直接写存储
- 不要重新引入 `Type + switch` 这类硬编码工厂
- 新 PLC 协议优先通过 `IPlcDriverProvider` 扩展
- 配置入口保持稳定，使用完整 `Driver` 名称
- 文档和示例必须跟代码一起更新

## 本地开发

```bash
dotnet restore
dotnet test tests/Ingot.Core.Tests/Ingot.Core.Tests.csproj
dotnet build Ingot.sln --no-restore
```

如果要联调完整链路，建议同时准备：

- InfluxDB 2.x
- `src/Ingot.Edge.Agent/Configs/TEST_PLC.json`
- `src/Ingot.Simulator`

## 贡献范围

欢迎这些类型的贡献：

- 新 PLC 驱动或现有驱动增强
- 采集链路可靠性修复
- TSDB 写入与队列语义改进
- 文档、示例配置、教程
- 自动化测试

## 添加新的 PLC 驱动

推荐路径：

1. 优先继承 `PlcClientServiceBase`，或按需实现 `IPlcConnectionClient` / `IPlcDataAccessClient` / `IPlcTypedWriteClient`
2. 实现 `IPlcDriverProvider`
3. 在启动时通过 DI 注册 provider
4. 为新驱动增加文档和示例配置
5. 为关键配置项补测试

要求：

- 使用稳定的 `Driver` 名称
- 不要引入别名体系
- 对 `Host` / `Port` 契约保持诚实，不要静默忽略配置
- 只开放当前驱动真正支持的 `ProtocolOptions`
- 对未支持的 `ProtocolOptions` 保持显式失败

## 扩展存储后端

- 替换存储后端：实现 `IDataStorageService.SaveBatchAsync`
- 调整队列语义：修改 `QueueService` / `QueueBatchPersister`

要求：

- 失败语义必须清晰
- 日志和指标不能缺位
- README / 设计文档 / 测试要同步更新

## 提交前检查

提交 PR 前至少确认：

- 代码可以构建
- 相关测试通过
- 新增行为有最小测试覆盖
- README / 教程 / 示例配置已同步
- 没有把本地运行产物提交进版本库

## PR 内容建议

PR 描述建议包含：

- 变更背景
- 设计取舍
- 风险点
- 验证方式
- 如果涉及配置变更，给出迁移说明

## 不建议的改动

- 为了“统一”而抹平不同 PLC 协议的真实差异
- 为了扩展某个驱动，把公共配置模型做得越来越臃肿
- 在没有测试和文档的情况下引入新配置项
- 用宣传口径代替工程边界，例如承诺不可证明的“绝对零丢失”
