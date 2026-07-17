# 常见问题

本文档汇总项目使用过程中的高频问题，并尽量避免重复教程中的基础内容。

如果你是第一次接触项目，先看：

- [文档首页](index.md)
- [快速开始](tutorial-getting-started.md)
- [配置](tutorial-configuration.md)

## 项目定位

### Ingot 的定位是什么

它是一个边缘优先的生产事件基础设施，PLC 是当前首个源适配器。

它负责：

- 从 PLC 读取数据
- 将高频遥测按批次写入 TSDB
- 将周期、参数和诊断变化写成不可变生产事件
- 提供本地事件查询、SSE、周期关联与业务上下文
- 暴露本地日志、指标和诊断接口
- 在条件采集场景下恢复 active cycle 上下文

### Ingot 的系统边界是什么

当前边界是：

- 主产品是 `Edge Agent`
- 中心侧页面是辅助控制面，不是采集主链路
- 遥测默认直接写 InfluxDB，失败批次记录后丢弃
- 生产事件先写入 `events.db`，不依赖 InfluxDB 成功才成立
- 中心事件通过本地 outbox 自动上行，Central 使用 PostgreSQL 幂等汇聚多个 Edge
- 中心不可用不影响本地采集与事件产生，恢复后会继续发送待确认事件

遥测仍不提供本地补写；生产事件提供受 `Events:MaxBacklogRows` 和本地磁盘容量约束的离线积压。

## 配置与驱动

### 应使用哪个驱动名称

使用 [驱动目录](hsl-drivers.md) 中列出的完整 `Driver` 名称。

不要使用旧式别名、缩写或自己猜的名称。

### 为什么配置校验失败

常见原因：

- JSON 格式不合法
- 缺少必填字段
- `SourceCode`（v1 为 `PlcCode`）为空或重复
- v2 引用了不存在的 Profile、对象类型或事件类型
- EventRule 缺少 Profile 要求的上下文键
- `Driver` 不在内置目录里
- `ProtocolOptions` 包含当前驱动不支持的键

建议先执行：

```bash
dotnet run --project src/Ingot.Edge.Agent -- --validate-configs
```

### 修改配置后需要重启吗

通常不需要。

设备配置目录由文件监视器监听，配置变更后会自动重新加载。

但有一个前提：

- 新配置必须先通过校验

### 如何添加新的 PLC 协议

如果内置目录里没有你要的协议，需要新增 provider。

推荐扩展方式：

1. 实现新的 `IPlcDriverProvider`
2. 复用 `PlcClientServiceBase` 或提供自己的 `IPlcClientService`
3. 在启动时注册 provider
4. 为新的 `Driver` 写文档和示例配置

如果只是使用内置 Hsl 驱动，通常不需要改核心代码。

## 采集与存储

### 为什么遥测直接写 TSDB，事件却写 SQLite

两类数据价值和速率不同：

- 高频遥测在内存中聚合并直接写 TSDB，失败时丢弃当前批次
- 低频生产事件先写 `events.db`，保证重启后仍可查询
- TSDB 中的事件副本只是可重建投影

### InfluxDB 挂了会不会影响采集

会影响当前批次写入。

当前预期是：

- 遥测失败批次会被记录并丢弃
- 事件仍可先落本地事件库
- 后续采集任务继续运行并继续尝试写入

这意味着你应该把 TSDB 写入失败当成运行告警，而不是等待系统后续补偿。

### 本地还会生成哪些文件

默认主要有：

- `Data/logs.db`
- `Data/acquisition-state.db`

含义：

- `logs.db` 用于本地日志查询和排障
- 默认保留 30 天，可通过 `Logging:RetentionDays` 调整；设置为 `<= 0` 时关闭清理
- `acquisition-state.db` 用于条件采集的 active cycle 状态恢复

### 为什么还要保存 active cycle 状态

因为条件采集在进程重启后需要恢复上下文。

当前 active cycle 会同时保存在：

- 内存
- `Data/acquisition-state.db`

这不是为了补写历史数据，而是为了让系统知道重启前是否存在未结束周期。

## 周期采集

### 条件采集的第一次读取会不会误触发

不会。

当前行为是：

- 首拍只建立基线
- 不会把初始化状态当成真实边沿

### 为什么会看到 `RecoveredStart` 或 `Interrupted`

这代表系统在周期进行中发生过重启或恢复。

它们是恢复诊断事件，不应直接当作正式业务周期统计口径。

正式周期统计时，仍应以成对的 `Start` / `End` 为准。

## 运行与排障

### 如何确认系统是否正常运行

至少检查这几项：

```bash
curl http://localhost:8001/health
curl http://localhost:8001/metrics
```

再检查：

- Edge 日志里是否出现 PLC 连接错误
- Edge 日志里是否出现 TSDB 写入错误
- InfluxDB 是否有 measurement 写入

### 为什么建议 Edge 使用宿主机进程部署

因为现场 PLC 网络通常比 Web 服务更接近真实网络环境问题。

宿主机进程部署更容易定位：

- 网卡选择
- 路由
- VLAN
- 防火墙
- PLC 可达性

中心组件和 InfluxDB 更适合容器化。

### 中心服务挂了会怎样

中心侧不可用时：

- 节点注册和心跳上报会失败
- 中心页面不可用
- 事件保留在 Edge 的 `events.db` 待上行队列
- EventShipper 指数退避；中心恢复后从未确认的 `Seq` 继续
- 可能发生安全重发，但 Central 会按 `EventId` 和 `(EdgeId, Seq)` 去重

但这不应该成为采集主链路的停止条件。

## 扩展与开发

### 如何替换存储后端

实现 `IDataStorageService`，然后在宿主层替换默认注册。

### 如何调整失败语义

修改 `QueueService`，并同步更新：

- 自动化测试
- README
- 设计文档

### 为什么项目继续使用 JSON 配置

因为这里的目标是：

- 简单
- 可读
- 易于热更新
- 易于在 .NET 环境中校验和绑定

比“换成 YAML/TOML”更重要的是：

- 有稳定的配置契约
- 有校验
- 有示例
- 有清晰错误提示

## 相关文档

- [配置](tutorial-configuration.md)
- [部署](tutorial-deployment.md)
- [驱动目录](hsl-drivers.md)
- [设计](design.md)
