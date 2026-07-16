# 开发扩展

本文面向准备修改源码、增加驱动或替换默认实现的开发者，说明代码阅读入口、扩展边界和提交前检查事项。

如果你只是想把系统跑起来，先看：

- [快速开始](tutorial-getting-started.md)
- [配置](tutorial-configuration.md)

## 代码阅读入口

推荐阅读顺序：

1. `src/DataAcquisition.Edge.Agent/Program.cs`
2. `src/DataAcquisition.Infrastructure/DataAcquisitions/DataAcquisitionService.cs`
3. `src/DataAcquisition.Infrastructure/Queues/QueueService.cs`
4. `src/DataAcquisition.Infrastructure/Clients/HslStandardPlcDriverProvider.cs`
5. `src/DataAcquisition.Infrastructure/DataStorages/InfluxDbDataStorageService.cs`

如果你想先理解模块边界，再看：

- [模块](modules.md)
- [设计](design.md)

## 扩展 PLC 驱动

新增驱动时，不应该改成“往工厂里继续堆 `switch`”。

当前推荐扩展方式是：

1. 实现新的 `IPlcDriverProvider`
2. 视情况复用 `PlcClientServiceBase`，或提供新的 `IPlcClientService`
3. 在宿主层注册 provider
4. 为新的 `Driver` 写示例配置和文档

实现时建议遵守这些约束：

- `Driver` 名称稳定且完整
- `Host` / `Port` 是否生效要明确
- `ProtocolOptions` 只开放真实支持的键
- 不要默默接受未使用的配置
- 不要把驱动私有逻辑泄漏到上层采集流程

## 扩展存储

项目当前采用“队列聚合后直写存储”的模式。

### 替换存储后端

实现：

- `IDataStorageService`

当前主入口是：

- `SaveBatchAsync(List<DataMessage>)`

扩展时要注意：

- 不要绕过 `QueueService` 直接写存储
- 明确你的成功返回语义
- 文档与测试要同步说明失败行为

### 调整队列语义

如果你要修改 `QueueService`，建议先明确：

- 批次边界如何定义
- 失败是否继续处理后续批次
- 日志和指标如何体现失败

不要把失败语义藏进实现细节里。

## 修改采集逻辑

如果你要调整采集行为，先理解这些边界：

- `HeartbeatMonitor` 决定当前 PLC 是否可采
- `ChannelCollector` 负责通道级采集流程
- `ChannelMetricReader` 负责字段读取
- `MetricExpressionEvaluator` 负责表达式计算
- `AcquisitionStateManager` 负责条件采集状态恢复

不要把：

- PLC 底层读写细节
- 业务周期判断
- 存储写入逻辑

重新揉回一个类里。

## 修改配置系统

当前配置系统的设计目标是：

- JSON 文件可读
- 可热更新
- 可离线校验
- 对驱动契约有显式约束

如果你要继续扩展配置，建议优先保持：

- 顶层字段稳定
- 驱动差异收敛到 `ProtocolOptions`
- 校验规则和文档同步演进

## 测试建议

如果你新增能力，至少补其中一种：

- 单元测试
- 集成测试
- 配置校验测试

优先级最高的是：

- 驱动配置校验
- 队列批次与失败语义
- 条件采集状态恢复

## 提交前检查

在提交代码前，至少执行：

```bash
dotnet build Ingot.sln --no-restore
dotnet test tests/DataAcquisition.Core.Tests/DataAcquisition.Core.Tests.csproj
dotnet run --project src/DataAcquisition.Edge.Agent -- --validate-configs
```

## 相关文档

- [贡献指南](../CONTRIBUTING.md)
- [模块](modules.md)
- [设计](design.md)
